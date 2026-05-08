using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Repos.Preflight;

/// <summary>
/// Walks a remote GitHub repo via the Tree API to enumerate the candidate
/// scan targets — <c>.sln</c>, <c>.csproj</c>, and <c>lintty.yml</c> — without
/// cloning. Sprint 3 PR S1: feeds <see cref="IRepoPreflightService"/> when
/// the dashboard renders the "choose scan target" picker.
///
/// <para>
/// <b>Why no clone.</b> The clone is owned by the worker (LGPD §6 — never
/// touch repo bytes outside the worker sandbox). Tree API responses are
/// metadata only (paths + SHAs), so the discovery path stays in the
/// existing per-tenant HTTP boundary.
/// </para>
///
/// <para>
/// <b>Token handling.</b> Mirror <see cref="Lintty.WebInspector.Github.IGitHubOrgsClient"/>:
/// the caller supplies the access token; the implementation never reads
/// from <see cref="Lintty.WebInspector.Auth.IGitHubUserTokenStore"/>. A
/// <c>null</c> token is valid only for public repos — GitHub will return
/// 404 on private ones, which the implementation surfaces as
/// <see cref="RepoTargetDiscoveryException"/> with
/// <see cref="RepoTargetDiscoveryError.NotAccessible"/>.
/// </para>
/// </summary>
public interface IRepoTargetDiscovery
{
    /// <summary>
    /// Resolves the branch SHA, fetches the recursive tree, filters for
    /// <c>.sln</c>, <c>.csproj</c>, and <c>lintty.yml</c> blobs.
    /// </summary>
    /// <param name="ownerLogin">Repo owner login (org or user).</param>
    /// <param name="repoName">Repo name (without owner prefix).</param>
    /// <param name="branch">Branch / tag / sha to walk. Falls back to
    /// the GitHub-reported default branch when null/empty.</param>
    /// <param name="userToken">Decrypted user token from
    /// <see cref="Lintty.WebInspector.Auth.IGitHubUserTokenStore.GetActiveTokenAsync"/>.
    /// <c>null</c> → unauthenticated (public-only).</param>
    /// <param name="ct">Cancellation propagated from the request pipeline.</param>
    Task<RepoTargetDiscoveryResult> DiscoverAsync(
        string ownerLogin,
        string repoName,
        string? branch,
        string? userToken,
        CancellationToken ct);
}

/// <summary>
/// Discovery result. Empty lists are valid — they mean "no candidates of
/// that kind"; the preflight service maps the empty case to
/// <c>no_dotnet_project</c>.
/// </summary>
/// <param name="SlnFiles">Repo-relative POSIX paths of <c>.sln</c> blobs,
/// alphabetic. May contain root-level (no slash) and nested entries.</param>
/// <param name="CsprojFiles">Repo-relative POSIX paths of <c>.csproj</c>
/// blobs, alphabetic.</param>
/// <param name="LinttyYmlFiles">Repo-relative POSIX paths of
/// <c>lintty.yml</c> blobs, alphabetic. The root-level entry (path equals
/// <c>"lintty.yml"</c>) takes precedence in
/// <see cref="IRepoPreflightService"/>'s status logic.</param>
/// <param name="Truncated"><c>true</c> when GitHub returned a truncated
/// tree (>100k items in the repo) OR when our per-category cap (100)
/// fired. The dashboard surfaces this so the user knows the picker may be
/// missing entries.</param>
public sealed record RepoTargetDiscoveryResult(
    IReadOnlyList<string> SlnFiles,
    IReadOnlyList<string> CsprojFiles,
    IReadOnlyList<string> LinttyYmlFiles,
    bool Truncated);

/// <summary>
/// Stable error categories thrown from
/// <see cref="IRepoTargetDiscovery.DiscoverAsync"/>. Map 1:1 to HTTP
/// envelopes the preflight endpoint returns.
/// </summary>
public enum RepoTargetDiscoveryError
{
    /// <summary>GitHub returned 404 for the repo or the branch — the
    /// preflight endpoint surfaces this as 404.</summary>
    NotAccessible = 0,

    /// <summary>GitHub returned 401 or 403 with a token — token revoked
    /// or scope insufficient. Endpoint surfaces 403.</summary>
    Forbidden = 1,

    /// <summary>5xx or transport failure — endpoint surfaces 502 / 503.</summary>
    Transient = 2,
}

/// <summary>
/// Typed exception carrying a stable <see cref="Error"/> the endpoint
/// can branch on without parsing <see cref="System.Exception.Message"/>.
/// </summary>
public sealed class RepoTargetDiscoveryException : System.Exception
{
    public RepoTargetDiscoveryError Error { get; }

    public RepoTargetDiscoveryException(RepoTargetDiscoveryError error, string message)
        : base(message)
    {
        Error = error;
    }

    public RepoTargetDiscoveryException(RepoTargetDiscoveryError error, string message, System.Exception inner)
        : base(message, inner)
    {
        Error = error;
    }
}
