using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Github;

namespace Lintty.WebInspector.Tests.Fakes;

/// <summary>
/// Test fake for <see cref="IGitHubOrgsClient"/>. Tests script the orgs +
/// repos this fake "sees" before issuing the request; the fake never hits
/// <c>api.github.com</c> (golden rule of the contract suite). Defaults are
/// "happy path with minimal data" so a test that only cares about the
/// happy path doesn't have to set anything up.
///
/// <para>
/// <b>Token tracking.</b> The fake records every <c>accessToken</c> it was
/// invoked with (last-write wins) so tests can assert that the endpoint
/// piped the right token through (e.g. that
/// <c>GetActiveTokenAsync</c>'s return value reaches the client unchanged).
/// </para>
///
/// <para>
/// <b>Failure simulation.</b> <see cref="ThrowOnListReposForLogin"/> +
/// <see cref="ThrowOnGetMetadataForRepo"/> let tests inject
/// <see cref="GitHubApiException"/>s by login/full-name without rebuilding
/// the whole fake; mirrors how
/// <c>FakeGitHubMetadataClient.Forbidden</c> works for the manual-add
/// path.
/// </para>
/// </summary>
public sealed class FakeGitHubOrgsClient : IGitHubOrgsClient
{
    /// <summary>Orgs returned from <see cref="ListOrgsAsync"/>. Mutate from
    /// tests before issuing the request.</summary>
    public List<GitHubOrgSummary> Orgs { get; set; } = new();

    /// <summary>Repos keyed by org login. <see cref="ListReposAsync"/>
    /// returns the entry for the supplied login or an empty list if the
    /// login has no entry.</summary>
    public Dictionary<string, List<GitHubRepoSummary>> ReposByOrgLogin { get; set; } = new();

    /// <summary>Metadata keyed by full name (e.g. <c>"acme/engine"</c>).
    /// <see cref="GetRepoMetadataAsync"/> returns the entry or <c>null</c>
    /// (the contract's "404" path).</summary>
    public Dictionary<string, GitHubRepoDetails> MetadataByFullName { get; set; } = new();

    /// <summary>When set, <see cref="ListReposAsync"/> with this login
    /// throws a <see cref="GitHubApiException"/> with the configured
    /// status. Used by the org-not-in-user-orgs / token-revoked
    /// negative-path tests.</summary>
    public Dictionary<string, HttpStatusCode> ThrowOnListReposForLogin { get; set; } = new();

    /// <summary>Same shape for <see cref="GetRepoMetadataAsync"/>.</summary>
    public Dictionary<string, HttpStatusCode> ThrowOnGetMetadataForRepo { get; set; } = new();

    public string? LastTokenSeen { get; private set; }

    public Task<IReadOnlyList<GitHubOrgSummary>> ListOrgsAsync(
        string accessToken, CancellationToken ct)
    {
        LastTokenSeen = accessToken;
        IReadOnlyList<GitHubOrgSummary> result = Orgs.ToArray();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<GitHubRepoSummary>> ListReposAsync(
        string accessToken, string orgLogin, CancellationToken ct)
    {
        LastTokenSeen = accessToken;
        if (ThrowOnListReposForLogin.TryGetValue(orgLogin, out var status))
        {
            throw new GitHubApiException(status, $"GitHub returned {(int)status} for org {orgLogin}");
        }
        if (ReposByOrgLogin.TryGetValue(orgLogin, out var list))
        {
            IReadOnlyList<GitHubRepoSummary> result = list.ToArray();
            return Task.FromResult(result);
        }
        IReadOnlyList<GitHubRepoSummary> empty = Array.Empty<GitHubRepoSummary>();
        return Task.FromResult(empty);
    }

    public Task<GitHubRepoDetails?> GetRepoMetadataAsync(
        string accessToken, string repoFullName, CancellationToken ct)
    {
        LastTokenSeen = accessToken;
        if (ThrowOnGetMetadataForRepo.TryGetValue(repoFullName, out var status))
        {
            throw new GitHubApiException(status, $"GitHub returned {(int)status} for {repoFullName}");
        }
        return Task.FromResult(
            MetadataByFullName.TryGetValue(repoFullName, out var meta) ? meta : null);
    }
}
