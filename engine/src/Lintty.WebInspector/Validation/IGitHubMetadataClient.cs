using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Validation;

/// <summary>
/// Result of a GitHub repo metadata pre-flight (HEAD/GET to api.github.com/repos/...).
/// We only care about three things at this stage: does it exist (or is the
/// token good enough), is it small enough to clone, and what's the default
/// branch (so the worker doesn't need its own GitHub call later).
/// </summary>
public sealed record GitHubRepoMetadata(
    bool Exists,
    bool Forbidden,
    long SizeKilobytes,
    string? DefaultBranch);

public interface IGitHubMetadataClient
{
    /// <summary>
    /// Hits <c>GET https://api.github.com/repos/{owner}/{repo}</c> with the
    /// optional PAT. Should never throw on 404 / 403 — those are mapped to
    /// flags on the result so the endpoint can return a clean 400.
    /// </summary>
    Task<GitHubRepoMetadata> GetMetadataAsync(GitHubRepoCoordinates coords, string? token, CancellationToken ct);
}
