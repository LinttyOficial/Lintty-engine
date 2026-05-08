using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Repos.Preflight;

/// <summary>
/// Computes "what would happen if I scanned this repo right now?" by walking
/// the GitHub Tree API and combining it with any user-curated selection
/// (<c>repos.scan_projects</c>). Sprint 3 PR S1 — closes the
/// <c>AmbiguousTargetMultipleCsprojs</c> hole that left users stuck on the
/// dashboard with no recovery path.
///
/// <para>
/// Cross-tenant defence: <see cref="GetAsync"/> and
/// <see cref="SetScanProjectsAsync"/> always filter by the caller's
/// <c>orgId</c>. <see cref="GetAsync"/> returns <c>null</c> when the repo
/// belongs to a different tenant; the endpoint maps that to 404 (§3.5).
/// </para>
/// </summary>
public interface IRepoPreflightService
{
    /// <summary>
    /// Discovers candidates and computes the current preflight status.
    /// Returns <c>null</c> if the repo doesn't exist, is soft-deleted, or
    /// belongs to a different org.
    /// </summary>
    Task<PreflightResult?> GetAsync(long repoId, long orgId, CancellationToken ct);

    /// <summary>
    /// Persists the user's curated selection. The caller passes
    /// repo-relative paths exactly as the dashboard rendered them (they
    /// must match an entry the discovery returned). The empty list clears
    /// the selection and returns the repo to auto-detect.
    /// </summary>
    Task<SetScanProjectsOutcome> SetScanProjectsAsync(
        long repoId,
        long orgId,
        IReadOnlyList<string> projects,
        CancellationToken ct);
}

/// <summary>
/// Wire-shape returned by <see cref="IRepoPreflightService.GetAsync"/>.
/// </summary>
/// <param name="Status">One of <see cref="PreflightStatus"/>.</param>
/// <param name="AutoDetected">When <see cref="Status"/> is
/// <c>"ready"</c> AND no user selection is saved, this carries what the
/// auto-detection would pick. <c>null</c> when the user has a saved
/// selection (the saved selection wins, no auto-detect happens).</param>
/// <param name="ScanProjects">The currently saved selection. <c>null</c> /
/// empty means auto-detect.</param>
/// <param name="Candidates">Every candidate the discovery returned, in
/// stable order (yaml first, then sln alpha, then csproj alpha). Always
/// populated so the dashboard can offer "change target" even when status
/// is already <c>ready</c>.</param>
/// <param name="Truncated">True when the discovery hit GitHub's
/// per-tree limit or our per-category cap.</param>
/// <param name="Reason">Human-readable explanation, primarily for
/// <c>needs_config</c> and <c>no_dotnet_project</c>. Null on
/// <c>ready</c>.</param>
public sealed record PreflightResult(
    string Status,
    PreflightAutoDetected? AutoDetected,
    IReadOnlyList<string>? ScanProjects,
    IReadOnlyList<PreflightCandidate> Candidates,
    bool Truncated,
    string? Reason);

/// <summary>
/// Auto-detected target description (only present when no saved selection
/// exists and the discovery yielded an unambiguous default).
/// </summary>
/// <param name="Kind">One of <see cref="CandidateKind"/>.</param>
/// <param name="Path">Repo-relative POSIX path of the picked file.</param>
public sealed record PreflightAutoDetected(string Kind, string Path);

/// <summary>
/// A single candidate the user can select. The dashboard renders the list
/// in the order the service returned it (yaml first, sln alpha, csproj
/// alpha) and uses <see cref="Kind"/> to decide which UI affordances
/// apply (e.g. "select multiple" only enabled when picking csprojs).
/// </summary>
/// <param name="Kind">One of <see cref="CandidateKind"/>.</param>
/// <param name="Path">Repo-relative POSIX path the dashboard echoes back
/// in the PUT body.</param>
public sealed record PreflightCandidate(string Kind, string Path);

/// <summary>
/// Stable string constants for <see cref="PreflightResult.Status"/>. The
/// frontend branches on these literals.
/// </summary>
public static class PreflightStatus
{
    /// <summary>Repo is scannable as-is (saved selection exists OR
    /// auto-detect picked a single target).</summary>
    public const string Ready = "ready";

    /// <summary>Discovery found candidates but auto-detect couldn't pick
    /// one — user must choose.</summary>
    public const string NeedsConfig = "needs_config";

    /// <summary>No <c>.sln</c>, <c>.csproj</c>, or <c>lintty.yml</c>
    /// found — not a .NET repo.</summary>
    public const string NoDotnetProject = "no_dotnet_project";
}

/// <summary>
/// Stable string constants for <see cref="PreflightCandidate.Kind"/> and
/// <see cref="PreflightAutoDetected.Kind"/>. Frontend matches on these.
/// </summary>
public static class CandidateKind
{
    public const string Sln = "sln";
    public const string Csproj = "csproj";
    public const string Yaml = "yaml";
}

/// <summary>
/// Outcome of <see cref="IRepoPreflightService.SetScanProjectsAsync"/>.
/// </summary>
public sealed record SetScanProjectsOutcome(
    string Status,
    string? Message)
{
    public static SetScanProjectsOutcome Ok() => new("ok", null);
    public static SetScanProjectsOutcome Invalid(string message) => new("invalid", message);
    public static SetScanProjectsOutcome NotFound() => new("not_found", null);
}
