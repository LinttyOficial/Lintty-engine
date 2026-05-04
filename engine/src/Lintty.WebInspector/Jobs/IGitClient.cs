using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Result of a clone attempt. The PAT is intentionally NOT held by the
/// implementation: it lives in a local for the duration of the call and is
/// released to the GC the moment the method returns. Callers must not log
/// the URL with the token embedded.
/// </summary>
public sealed record GitCloneResult(bool Success, string? ErrorMessage);

public interface IGitClient
{
    /// <summary>
    /// Shallow-clones <paramref name="coords"/> at <paramref name="reference"/>
    /// (branch, tag, or commit) into <paramref name="destination"/>.
    /// </summary>
    Task<GitCloneResult> CloneAsync(
        GitHubRepoCoordinates coords,
        string? reference,
        string? token,
        string destination,
        CancellationToken ct);
}
