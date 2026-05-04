using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IGitHubMetadataClient"/> for tests. Default
/// answer is "exists, public, small" so the contract tests that exercise the
/// happy path don't need to set anything up. Tests that want a 404/forbidden
/// can mutate the public fields before issuing the request.
/// </summary>
public sealed class FakeGitHubMetadataClient : IGitHubMetadataClient
{
    public bool Exists { get; set; } = true;
    public bool Forbidden { get; set; }
    public long SizeKilobytes { get; set; } = 256;
    public string? DefaultBranch { get; set; } = "main";

    public Task<GitHubRepoMetadata> GetMetadataAsync(GitHubRepoCoordinates coords, string? token, CancellationToken ct)
        => Task.FromResult(new GitHubRepoMetadata(Exists, Forbidden, SizeKilobytes, DefaultBranch));
}
