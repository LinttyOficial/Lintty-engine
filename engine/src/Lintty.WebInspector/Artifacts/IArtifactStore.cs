using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Artifacts;

/// <summary>
/// Storage abstraction for the per-scan artifacts (laudo PDF + report JSON)
/// produced by the engine subprocess. ADR 0007 §3.8 — introduced in Sprint 3
/// PR 2 so the org-bound scan flow (PR 4 onwards) and the future cloud
/// implementation (V1.1, <c>S3ArtifactStore</c>) share a single contract.
///
/// <para>
/// <b>Scope.</b> This interface only governs the org-bound <c>scans</c> flow
/// (URL: <c>/api/scans/{public_id}/...</c>). The V0 anonymous job path keeps
/// writing under <c>JobStorage:JobsDir</c> via <see cref="Lintty.WebInspector.Jobs.JobWorker"/>
/// — that legacy layout is preserved bit-for-bit so the cross-determinism
/// gate (<c>13-web-inspector.md</c> §10) stays valid. Sprint 5 of ADR 0007
/// migrates the anonymous flow on top of this same store; until then the
/// two coexist.
/// </para>
///
/// <para>
/// <b>Why <see cref="Guid"/> instead of string.</b>
/// <see cref="Lintty.WebInspector.Persistence.Entities.Scan.PublicId"/> is a
/// native UUID v4 in Postgres. Strong typing here prevents accidental hex /
/// dashed mismatches across the call chain; any string formatting is a
/// concern of the implementation, not the contract.
/// </para>
///
/// <para>
/// <b>Why <see cref="Stream"/> on <see cref="OpenAsync"/>.</b> Laudo PDFs are
/// hundreds of kilobytes — streaming straight into the HTTP response (via
/// <c>Results.Stream(...)</c> in PR 4) avoids an in-memory buffer per
/// download. Returning <c>byte[]</c> would defeat that.
/// </para>
///
/// <para>
/// <b>Why <see cref="ReserveAsync"/> returns a path instead of writing.</b>
/// The engine CLI subprocess writes the PDF to disk itself (it receives the
/// path via <c>--pdf</c>). The runner asks the store to <i>reserve</i> a
/// destination, hands the path to the CLI, and the CLI writes there
/// directly. This keeps the cross-determinism gate intact: nothing flows
/// through our pipeline in memory, so we can't accidentally re-encode the
/// PDF and break byte-equality with the local CLI output.
/// </para>
/// </summary>
public interface IArtifactStore
{
    /// <summary>
    /// Returns the absolute filesystem path where the runner should write
    /// the artifact for this scan. Implementations guarantee uniqueness for
    /// (<paramref name="publicId"/>, <paramref name="kind"/>) and ensure the
    /// parent directory exists. The file itself is NOT created — the caller
    /// (engine subprocess) is responsible for writing.
    /// </summary>
    Task<string> ReserveAsync(Guid publicId, ArtifactKind kind, CancellationToken ct);

    /// <summary>
    /// Opens an existing artifact for read. Returns <c>null</c> when the
    /// artifact does not exist; callers translate that to HTTP 404. Never
    /// throws <see cref="FileNotFoundException"/> — the null contract keeps
    /// the 404 path branchless and avoids the exception cost on a hot path.
    /// </summary>
    Task<Stream?> OpenAsync(Guid publicId, ArtifactKind kind, CancellationToken ct);

    /// <summary>
    /// Returns true when the artifact is present on the underlying store.
    /// Used by polling endpoints / health checks that need to know whether
    /// the worker has finished writing without paying the cost of opening
    /// a stream.
    /// </summary>
    Task<bool> ExistsAsync(Guid publicId, ArtifactKind kind, CancellationToken ct);

    /// <summary>
    /// Deletes <i>every</i> artifact belonging to the given scan. Idempotent
    /// — calling twice on the same id is a no-op for the second call. Used
    /// by retention cleanup (Sprint 5 of ADR 0007) and by any hard-delete
    /// admin path that needs to expunge a scan from the store.
    /// </summary>
    Task DeleteAsync(Guid publicId, CancellationToken ct);
}

/// <summary>
/// Discriminator for the two artifact kinds an engine run produces:
/// the laudo PDF (the user-facing report) and the JSON report (the
/// canonical machine-readable engine output, hash-equal to the CLI's
/// stdout).
/// </summary>
public enum ArtifactKind
{
    /// <summary>The laudo PDF — what the user downloads / reads.</summary>
    LaudoPdf,

    /// <summary>The engine JSON report — input to <c>hash_content</c>
    /// and the cross-determinism gate.</summary>
    ReportJson,
}
