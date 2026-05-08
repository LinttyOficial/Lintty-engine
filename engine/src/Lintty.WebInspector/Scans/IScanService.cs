using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Artifacts;

namespace Lintty.WebInspector.Scans;

/// <summary>
/// Service surface for the org-bound scan lifecycle. ADR 0007 §3.7 + §3.8
/// (storage abstraction). Sprint 3 PR 4.
///
/// <para>
/// Every method takes the active <c>orgId</c> explicitly. Callers (the
/// <c>/api/scans/*</c> + <c>/api/repos/{id}/scans</c> endpoints) are
/// responsible for resolving the tenant via <c>ITenantContext</c> + the
/// first-membership fallback used by <c>ReposEndpoints</c>. The service
/// does not read <c>HttpContext.Items</c>.
/// </para>
///
/// <para>
/// <b>404 vs 403.</b> Cross-tenant lookups (cookie of org A asking for a
/// scan/repo of org B) surface as <c>null</c>, never an exception — the
/// endpoint translates that to HTTP 404 per §3.5 (404 doesn't confirm
/// existence; 403 would be a resource-enumeration vector).
/// </para>
/// </summary>
public interface IScanService
{
    /// <summary>
    /// Triggers one or more scans against <paramref name="repoId"/>. Sprint
    /// 3 PR S2 — each requested target produces its own <c>scans</c> row,
    /// each row carries its own <see cref="Lintty.WebInspector.Persistence.Entities.Scan.Target"/>,
    /// and each row produces its own PDF.
    ///
    /// <para>
    /// <b>Effective target list (in precedence order):</b>
    /// <list type="number">
    ///   <item><description><paramref name="targets"/> non-null and
    ///         non-empty → per-request override; the saved
    ///         <c>repos.scan_projects</c> is ignored for this call.</description></item>
    ///   <item><description>Otherwise, if the repo's saved
    ///         <c>scan_projects</c> is non-empty → expand each entry into
    ///         its own scan row.</description></item>
    ///   <item><description>Otherwise → 1 scan row with <c>Target = null</c>
    ///         (engine auto-detect).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Validates repo ownership (<paramref name="orgId"/> must own the
    /// repo, the repo must not be soft-deleted) and target shape (each
    /// non-null entry must end in <c>.sln</c> or <c>.csproj</c>; yaml
    /// entries are rejected). All rows are inserted in a single
    /// transaction; <c>canon_version</c> is snapshotted once for the
    /// whole batch (§3.7). On success the result carries every new
    /// public id in trigger order.
    /// </para>
    /// </summary>
    /// <param name="orgId">Tenant the scan is anchored to.</param>
    /// <param name="userId">User who triggered the scan; persisted as
    /// <c>triggered_by_user_id</c> on every row.</param>
    /// <param name="repoId">Numeric id of the repo from <c>repos</c>.
    /// Must belong to <paramref name="orgId"/>.</param>
    /// <param name="gitRef">Optional ref override (branch, tag, sha)
    /// applied to every row in the batch. When <c>null</c>, the worker
    /// lets <c>git clone --depth 1</c> resolve HEAD against the repo's
    /// default branch — same fallback as the V0 anonymous flow.</param>
    /// <param name="targets">Optional per-request override of the repo's
    /// saved <c>scan_projects</c>. <c>null</c> or empty falls back to the
    /// saved value (or to a single auto-detect scan if neither is set).</param>
    /// <param name="ct">Cancellation propagated from the request pipeline.</param>
    Task<TriggerScanResult> TriggerAsync(
        long orgId,
        long userId,
        long repoId,
        string? gitRef,
        IReadOnlyList<string>? targets,
        CancellationToken ct);

    /// <summary>
    /// Returns a single scan if it belongs to <paramref name="orgId"/>.
    /// Cross-tenant or non-existent ids surface as <c>null</c>.
    /// </summary>
    Task<ScanSummary?> GetByPublicIdAsync(
        long orgId,
        Guid publicId,
        CancellationToken ct);

    /// <summary>
    /// Lists scans for <paramref name="repoId"/> ordered by
    /// <c>queued_at DESC</c>. Returns an empty list when the repo doesn't
    /// belong to <paramref name="orgId"/> (callers should pre-validate the
    /// repo via <c>IRepoService.GetAsync</c> if they need to distinguish
    /// "no scans" from "wrong tenant"). No pagination V0 — Sprint 5 ADR
    /// adds cursor-based.
    /// </summary>
    Task<IReadOnlyList<ScanSummary>> ListByRepoAsync(
        long orgId,
        long repoId,
        CancellationToken ct);

    /// <summary>
    /// Opens a scan artifact for read. Tenant + status checks happen
    /// before the storage call so the 404 / 409 branches stay branchless
    /// at the endpoint layer.
    /// </summary>
    /// <returns>
    /// A <see cref="OpenScanArtifactResult"/> discriminating between four
    /// outcomes: <c>NotFound</c> (cross-tenant or unknown id),
    /// <c>NotCompleted</c> (status ≠ completed → 409), <c>Missing</c>
    /// (status = completed but the store has nothing — defensive 404), or
    /// <c>Found</c> (stream + content type). Same result-type pattern as
    /// <c>AddRepoResult</c>.
    /// </returns>
    Task<OpenScanArtifactResult> OpenArtifactAsync(
        long orgId,
        Guid publicId,
        ArtifactKind kind,
        CancellationToken ct);
}

/// <summary>
/// Outcome of <see cref="IScanService.TriggerAsync"/>. Sprint 3 PR S2 —
/// always carries a list (1..N) since the trigger now expands each target
/// into its own scan row. <see cref="Scan"/> exposes the first scan in the
/// list for callers that historically only handled the 1-row case (the
/// integration tests under <c>WorkerIntegrationTests</c> still go through
/// it, since they trigger with no targets and never need more than one
/// row). Endpoint code should always read <see cref="Scans"/>.
/// </summary>
public sealed record TriggerScanResult(
    TriggerScanOutcome Outcome,
    IReadOnlyList<ScanSummary> Scans,
    string? ErrorCode,
    string? ErrorMessage)
{
    /// <summary>Convenience accessor for the first scan in <see cref="Scans"/>.
    /// Returns <c>null</c> on non-Created outcomes or empty batches.</summary>
    public ScanSummary? Scan => Scans.Count == 0 ? null : Scans[0];

    public static TriggerScanResult Created(IReadOnlyList<ScanSummary> scans)
        => new(TriggerScanOutcome.Created, scans, null, null);

    public static TriggerScanResult RepoNotFound()
        => new(TriggerScanOutcome.RepoNotFound, System.Array.Empty<ScanSummary>(), "repo_not_found", "Repo not found.");

    public static TriggerScanResult Error(string code, string message)
        => new(TriggerScanOutcome.Error, System.Array.Empty<ScanSummary>(), code, message);
}

public enum TriggerScanOutcome
{
    /// <summary>New scan inserted, status=queued — endpoint emits 201.</summary>
    Created = 0,

    /// <summary>Repo doesn't exist, soft-deleted, or belongs to a different
    /// org — endpoint emits 404 (§3.5: 404, not 403, to avoid enumeration).</summary>
    RepoNotFound = 1,

    /// <summary>Validation failure (e.g. bad <c>ref</c> format). <c>ErrorCode</c>
    /// + <c>ErrorMessage</c> populated; endpoint emits 4xx.</summary>
    Error = 2,
}

/// <summary>
/// Wire-shape of a scan row exposed to the API. Includes a denormalised
/// <see cref="Repo"/> sub-shape so the dashboard can render repo identity
/// without a second round-trip on every poll.
/// </summary>
public sealed record ScanSummary(
    Guid PublicId,
    string Status,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Ref,
    string CanonVersion,
    string? HashContent,
    string? Error,
    long TriggeredByUserId,
    string? Target,
    ScanRepoSummary Repo);

/// <summary>
/// The minimal subset of <c>repos</c> the dashboard needs alongside a scan.
/// Avoids leaking the full <c>RepoSummary</c> with its <c>addedBy</c>
/// payload — that information is already available via
/// <c>GET /api/repos/{id}</c> when the user wants it.
/// </summary>
public sealed record ScanRepoSummary(long Id, string GithubUrl);

/// <summary>
/// Tagged-union return for <see cref="IScanService.OpenArtifactAsync"/>.
/// The endpoint does not need to differentiate <see cref="OpenScanArtifactOutcome.NotFound"/>
/// from <see cref="OpenScanArtifactOutcome.Missing"/> for the user (both
/// are 404), but the distinction is kept for logging — "store empty for a
/// completed scan" is a real defect signal we want to see in dashboards.
/// </summary>
public sealed record OpenScanArtifactResult(
    OpenScanArtifactOutcome Outcome,
    Stream? Stream,
    string? ContentType,
    string? FileDownloadName)
{
    public static OpenScanArtifactResult NotFound()
        => new(OpenScanArtifactOutcome.NotFound, null, null, null);

    public static OpenScanArtifactResult NotCompleted()
        => new(OpenScanArtifactOutcome.NotCompleted, null, null, null);

    public static OpenScanArtifactResult Missing()
        => new(OpenScanArtifactOutcome.Missing, null, null, null);

    public static OpenScanArtifactResult Found(Stream stream, string contentType, string fileDownloadName)
        => new(OpenScanArtifactOutcome.Found, stream, contentType, fileDownloadName);
}

public enum OpenScanArtifactOutcome
{
    /// <summary>Scan does not exist OR belongs to a different org. 404.</summary>
    NotFound = 0,

    /// <summary>Scan exists but its <c>status</c> isn't <c>completed</c>. 409.</summary>
    NotCompleted = 1,

    /// <summary>Scan completed but the artifact is missing on the store
    /// (defensive — should not happen in normal operation). 404.</summary>
    Missing = 2,

    /// <summary>Stream is open and ready to copy to the response body.</summary>
    Found = 3,
}
