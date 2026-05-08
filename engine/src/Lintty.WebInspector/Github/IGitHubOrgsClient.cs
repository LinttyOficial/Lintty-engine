using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Github;

/// <summary>
/// Read-side wrapper around the GitHub orgs/repos REST API. ADR 0007
/// Apêndice E §E.7 / §E.8 — Sprint 3 PR 7. Distinct from
/// <see cref="Lintty.WebInspector.Auth.IGitHubOAuthClient"/> (which owns the
/// OAuth dance + the <c>/user</c> profile fetch) and
/// <see cref="Lintty.WebInspector.Validation.IGitHubMetadataClient"/> (which
/// owns the unauthenticated public-repo pre-flight for manual add). Three
/// hosted clients, three different jobs — keeps each surface narrow enough
/// that a fake in tests is cheap.
///
/// <para>
/// <b>Token handling.</b> The client never reads from
/// <see cref="Lintty.WebInspector.Auth.IGitHubUserTokenStore"/> on its own;
/// every method takes an <c>accessToken</c> parameter. The caller (an
/// endpoint) is responsible for fetching the token via
/// <c>GetActiveTokenAsync</c> and translating the <c>null</c> return into a
/// <c>403 github_not_connected</c>. This keeps the HTTP client unaware of
/// "is the user connected?" semantics — easier to reason about, easier to
/// fake.
/// </para>
///
/// <para>
/// <b>Pagination.</b> GitHub paginates with <c>?per_page</c> + <c>Link</c>
/// headers. V0 ships single-page calls (<c>per_page=100</c>) for both
/// orgs and repos — adequate for the founder's "3 orgs, ~30 repos each"
/// demo footprint and avoids the Link-header parser complexity. A user
/// with &gt;100 orgs or an org with &gt;100 repos surfaces the truncation
/// in V0 logs (warning) and is fixed in V1.1 alongside server-side caching
/// (Apêndice E §E.13 / E6).
/// </para>
/// </summary>
public interface IGitHubOrgsClient
{
    /// <summary>
    /// <c>GET /user/orgs?per_page=100</c>. Lists the GitHub organizations
    /// the supplied <paramref name="accessToken"/> can see. With the
    /// connect-flow scopes (<c>repo</c> + <c>read:org</c>), this returns
    /// the orgs the user is a member of, including private ones.
    /// </summary>
    /// <param name="accessToken">Decrypted user token from
    /// <see cref="Lintty.WebInspector.Auth.IGitHubUserTokenStore.GetActiveTokenAsync"/>.</param>
    /// <param name="ct">Cancellation propagated from the request pipeline.</param>
    Task<IReadOnlyList<GitHubOrgSummary>> ListOrgsAsync(string accessToken, CancellationToken ct);

    /// <summary>
    /// <c>GET /orgs/{login}/repos?type=all&amp;per_page=100</c>. Lists every
    /// repo (public and private) under <paramref name="orgLogin"/> that
    /// the token's user can access. The endpoint validates that the
    /// caller is actually a member of the org BEFORE invoking this; the
    /// client itself does not — see the
    /// <c>Lintty.WebInspector.Endpoints.GithubEndpoints</c> contract.
    /// </summary>
    Task<IReadOnlyList<GitHubRepoSummary>> ListReposAsync(
        string accessToken,
        string orgLogin,
        CancellationToken ct);

    /// <summary>
    /// <c>GET /repos/{owner}/{name}</c>. Fetches the canonical metadata
    /// for a single repo identified by <paramref name="repoFullName"/>
    /// (e.g. <c>"acme/engine"</c>). Returns <c>null</c> on 404 to mirror
    /// <see cref="Lintty.WebInspector.Auth.IGitHubOAuthClient.GetUserAsync"/>'s
    /// "absence as null" pattern; the import endpoint translates
    /// <c>null</c> into a 404 <c>repo_not_found</c>.
    /// </summary>
    /// <exception cref="GitHubApiException">When GitHub returns a non-404
    /// 4xx (typically 401/403 — token revoked or scope insufficient). The
    /// caller maps the <c>StatusCode</c> to the right HTTP envelope.</exception>
    Task<GitHubRepoDetails?> GetRepoMetadataAsync(
        string accessToken,
        string repoFullName,
        CancellationToken ct);
}

/// <summary>
/// Wire shape for a single org from <c>GET /user/orgs</c>. <see cref="Id"/>
/// is the stable numeric id (preferred for joins / cache keys);
/// <see cref="Login"/> is the URL slug; <see cref="AvatarUrl"/> is optional.
/// </summary>
public sealed record GitHubOrgSummary(long Id, string Login, string? AvatarUrl);

/// <summary>
/// Wire shape for a single repo from <c>GET /orgs/{login}/repos</c>. Mirrors
/// the GitHub REST envelope minus the fields the dashboard never needs (push
/// stats, license, etc.). <see cref="DefaultBranch"/> is required by the
/// import flow even on listing because the import body only carries
/// <c>(orgLogin, fullName)</c>, not the branch — same call serves "render
/// the table" and "pre-populate the import payload".
/// </summary>
public sealed record GitHubRepoSummary(
    long Id,
    string Name,
    string FullName,
    bool IsPrivate,
    string DefaultBranch,
    string HtmlUrl);

/// <summary>
/// Wire shape for <c>GET /repos/{owner}/{name}</c>. Sibling of
/// <see cref="GitHubRepoSummary"/> — same fields plus
/// <see cref="CloneUrl"/>, which the listing endpoint omits to keep the
/// table response narrow but the import path needs to populate
/// <c>repos.github_url</c>. (Both URLs come from GitHub; we use
/// <c>html_url</c> + <c>/owner/name</c> in the listing to skip the
/// <c>.git</c> noise but pin the canonical clone URL on import.)
/// </summary>
public sealed record GitHubRepoDetails(
    long Id,
    string Name,
    string FullName,
    bool IsPrivate,
    string DefaultBranch,
    string CloneUrl);
