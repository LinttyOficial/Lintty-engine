using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lintty.WebInspector.Artifacts;
using Lintty.WebInspector.Canon;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;

namespace Lintty.WebInspector.Scans;

/// <summary>
/// EF-backed <see cref="IScanService"/>. ADR 0007 §3.7 + §3.8 — Sprint 3 PR 4.
///
/// <para>
/// <b>Tenancy enforcement.</b> Every read filters on
/// <c>scan.OrgId == orgId</c> (and indirectly on <c>repo.OrgId</c> at trigger
/// time). The <c>OrFutureExtensionsCheck</c>-style guard from §3.5 lives in
/// <c>RepoService</c> and is mirrored here: a scan from another org surfaces
/// as <c>null</c>, never an exception.
/// </para>
///
/// <para>
/// <b>Determinism invariant (§3.7).</b> <c>canon_version</c> is taken from
/// <see cref="ICanonVersionProvider"/> at the moment of trigger, persisted
/// onto the row, and replayed verbatim by the worker. The default impl
/// returns <c>"1.0.0"</c> — same value the engine CLI hardcodes — so the
/// PR 5 cross-determinism gate
/// (<c>DashboardScan_Saint_Pdf_Equals_CliDirect</c>) holds without the engine
/// needing to grow a host-level "current canon" knob.
/// </para>
///
/// <para>
/// <b>One trigger = one scan row = one PDF (Sprint 3 PR S3).</b> The repo's
/// <c>scan_projects</c> column is consumed by the worker (which may write a
/// runtime <c>lintty.yml</c> with <c>projects:</c> for multi-csproj
/// selections); the trigger never expands it into multiple rows.
/// </para>
/// </summary>
public sealed class ScanService : IScanService
{
    private readonly LinttyDbContext _db;
    private readonly ICanonVersionProvider _canon;
    private readonly IArtifactStore _artifacts;
    private readonly ILogger<ScanService> _log;

    public ScanService(
        LinttyDbContext db,
        ICanonVersionProvider canon,
        IArtifactStore artifacts,
        ILogger<ScanService> log)
    {
        _db = db;
        _canon = canon;
        _artifacts = artifacts;
        _log = log;
    }

    public async Task<TriggerScanResult> TriggerAsync(
        long orgId,
        long userId,
        long repoId,
        string? gitRef,
        CancellationToken ct)
    {
        // Tenant + soft-delete check on the repo. §3.5 says cross-tenant
        // returns 404, not 403 — surface as RepoNotFound regardless of the
        // actual cause. Single round-trip; AsNoTracking because we only
        // read the columns we need to project into the response.
        var repo = await _db.Repos
            .AsNoTracking()
            .Where(r => r.Id == repoId
                     && r.OrgId == orgId
                     && r.DeletedAt == null)
            .Select(r => new { r.Id, r.GithubUrl })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (repo is null)
        {
            return TriggerScanResult.RepoNotFound();
        }

        // Snapshot canon at trigger time. §3.7 invariant.
        var canonVersion = _canon.GetCurrent();

        // Insert. PublicId is filled by Postgres via gen_random_uuid()
        // server-side — we read it back after SaveChanges. QueuedAt has a
        // default of "now() at time zone 'utc'" in the migration but EF
        // initializes the property to DateTime.UtcNow — let EF win so the
        // unit tests have a deterministic value to assert against.
        var queuedAt = DateTime.UtcNow;
        var scan = new Scan
        {
            OrgId = orgId,
            RepoId = repoId,
            TriggeredByUserId = userId,
            Ref = string.IsNullOrWhiteSpace(gitRef) ? null : gitRef,
            CanonVersion = canonVersion,
            Status = ScanStatus.Queued,
            QueuedAt = queuedAt,
        };
        _db.Scans.Add(scan);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Re-read PublicId from the DB-generated value. EF rehydrates it on
        // SaveChanges since the column has a default — confirmed by the
        // migration's HasDefaultValueSql("gen_random_uuid()").
        _log.LogInformation(
            "Scan triggered: org_id={OrgId} repo_id={RepoId} scan_id={ScanId} public_id={PublicId} canon={Canon} ref={Ref}",
            orgId, repoId, scan.Id, scan.PublicId, canonVersion, scan.Ref ?? "<default>");

        return TriggerScanResult.Created(ToSummary(scan, repo.Id, repo.GithubUrl));
    }

    public async Task<ScanSummary?> GetByPublicIdAsync(
        long orgId,
        Guid publicId,
        CancellationToken ct)
    {
        // Project repo id + url alongside the scan in a single query so the
        // wire-shape's `repo` block fills in without a follow-up fetch.
        var row = await _db.Scans
            .AsNoTracking()
            .Where(s => s.PublicId == publicId && s.OrgId == orgId)
            .Select(s => new
            {
                Scan = s,
                RepoId = s.RepoId,
                RepoUrl = s.Repo!.GithubUrl,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (row is null) return null;
        return ToSummary(row.Scan, row.RepoId, row.RepoUrl);
    }

    public async Task<IReadOnlyList<ScanSummary>> ListByRepoAsync(
        long orgId,
        long repoId,
        CancellationToken ct)
    {
        var rows = await _db.Scans
            .AsNoTracking()
            .Where(s => s.OrgId == orgId && s.RepoId == repoId)
            .OrderByDescending(s => s.QueuedAt)
            .ThenByDescending(s => s.Id)        // stable tie-break for tests with same-tick queues
            .Select(s => new
            {
                Scan = s,
                RepoId = s.RepoId,
                RepoUrl = s.Repo!.GithubUrl,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(r => ToSummary(r.Scan, r.RepoId, r.RepoUrl)).ToList();
    }

    public async Task<OpenScanArtifactResult> OpenArtifactAsync(
        long orgId,
        Guid publicId,
        ArtifactKind kind,
        CancellationToken ct)
    {
        // Read just the status — we don't need the full row for the
        // download path. Tenant filter still applies.
        var scan = await _db.Scans
            .AsNoTracking()
            .Where(s => s.PublicId == publicId && s.OrgId == orgId)
            .Select(s => new { s.Status })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (scan is null) return OpenScanArtifactResult.NotFound();
        if (!string.Equals(scan.Status, ScanStatus.Completed, StringComparison.Ordinal))
        {
            return OpenScanArtifactResult.NotCompleted();
        }

        var stream = await _artifacts.OpenAsync(publicId, kind, ct).ConfigureAwait(false);
        if (stream is null)
        {
            // Status=completed but artifact missing — should never happen
            // in normal operation; logged as a warning for retention/perf
            // dashboards. Translates to 404 at the endpoint.
            _log.LogWarning(
                "OpenArtifactAsync: artifact missing for completed scan; org_id={OrgId} public_id={PublicId} kind={Kind}",
                orgId, publicId, kind);
            return OpenScanArtifactResult.Missing();
        }

        var (contentType, fileDownloadName) = ContentMetadataFor(kind, publicId);
        return OpenScanArtifactResult.Found(stream, contentType, fileDownloadName);
    }

    private static (string ContentType, string FileDownloadName) ContentMetadataFor(
        ArtifactKind kind, Guid publicId)
    {
        return kind switch
        {
            ArtifactKind.LaudoPdf  => ("application/pdf",  $"laudo-{publicId:D}.pdf"),
            ArtifactKind.ReportJson => ("application/json", $"report-{publicId:D}.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ArtifactKind."),
        };
    }

    private static ScanSummary ToSummary(Scan s, long repoId, string repoUrl)
    {
        return new ScanSummary(
            PublicId: s.PublicId,
            Status: s.Status,
            QueuedAt: DateTime.SpecifyKind(s.QueuedAt, DateTimeKind.Utc),
            StartedAt: s.StartedAt is null ? null : DateTime.SpecifyKind(s.StartedAt.Value, DateTimeKind.Utc),
            CompletedAt: s.CompletedAt is null ? null : DateTime.SpecifyKind(s.CompletedAt.Value, DateTimeKind.Utc),
            Ref: s.Ref,
            CanonVersion: s.CanonVersion,
            HashContent: s.HashContent,
            Error: s.Error,
            TriggeredByUserId: s.TriggeredByUserId,
            Repo: new ScanRepoSummary(repoId, repoUrl));
    }
}
