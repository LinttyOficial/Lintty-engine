using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Repos.Preflight;

namespace Lintty.WebInspector.Tests.Fakes;

/// <summary>
/// Test fake for <see cref="IRepoTargetDiscovery"/>. Tests script the
/// discovery results keyed on <c>"owner/name"</c> so a single fake
/// instance can serve multiple repos in the same test method.
///
/// <para>
/// Tests never hit api.github.com (golden rule of the contract suite).
/// Defaults to "no candidates" so a test that doesn't care simply sees
/// <see cref="PreflightStatus.NoDotnetProject"/>.
/// </para>
/// </summary>
public sealed class FakeRepoTargetDiscovery : IRepoTargetDiscovery
{
    /// <summary>Discovery results keyed by <c>"owner/name"</c>. Mutate
    /// from tests before issuing the request.</summary>
    public Dictionary<string, RepoTargetDiscoveryResult> ResultsByFullName { get; set; } = new();

    /// <summary>Repos that should fail discovery with the configured
    /// error category. Useful for the 404 / private / transient cases.</summary>
    public Dictionary<string, RepoTargetDiscoveryError> ThrowOnFullName { get; set; } = new();

    /// <summary>Token observed on the most recent call. Tests use this
    /// to assert the preflight service piped the user token through.</summary>
    public string? LastTokenSeen { get; private set; }

    public Task<RepoTargetDiscoveryResult> DiscoverAsync(
        string ownerLogin,
        string repoName,
        string? branch,
        string? userToken,
        CancellationToken ct)
    {
        LastTokenSeen = userToken;
        var key = $"{ownerLogin}/{repoName}";
        if (ThrowOnFullName.TryGetValue(key, out var err))
        {
            throw new RepoTargetDiscoveryException(
                err,
                $"Fake discovery failure for {key}: {err}");
        }
        if (ResultsByFullName.TryGetValue(key, out var hit))
        {
            return Task.FromResult(hit);
        }
        return Task.FromResult(new RepoTargetDiscoveryResult(
            SlnFiles: Array.Empty<string>(),
            CsprojFiles: Array.Empty<string>(),
            LinttyYmlFiles: Array.Empty<string>(),
            Truncated: false));
    }
}
