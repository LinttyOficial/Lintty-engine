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

    /// <summary>
    /// Sane upper bound on the batch size to keep an accidental click from
    /// flooding the queue. The picker UI never shows more than ~50 csprojs
    /// per repo (the Tree API returns a flat list); this matches that
    /// expectation.
    /// </summary>
    private const int MaxTargetsPerTrigger = 50;

    public async Task<TriggerScanResult> TriggerAsync(
        long orgId,
        long userId,
        long repoId,
        string? gitRef,
        IReadOnlyList<string>? targets,
        CancellationToken ct)
    {
        // Tenant + soft-delete check on the repo. §3.5 says cross-tenant
        // returns 404, not 403 — surface as RepoNotFound regardless of the
        // actual cause. Single round-trip; AsNoTracking because we only
        // read the columns we need to project into the response (plus the
        // saved scan_projects to fall back to).
        var repo = await _db.Repos
            .AsNoTracking()
            .Where(r => r.Id == repoId
                     && r.OrgId == orgId
                     && r.DeletedAt == null)
            .Select(r => new { r.Id, r.GithubUrl, r.ScanProjects })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (repo is null)
        {
            return TriggerScanResult.RepoNotFound();
        }

        // Resolve effective target list — per-request override beats saved
        // selection beats single auto-detect.
        var effective = ResolveEffectiveTargets(targets, repo.ScanProjects);
        if (effective.Count > MaxTargetsPerTrigger)
        {
            return TriggerScanResult.Error(
                "too_many_targets",
                $"Trigger requested {effective.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} targets; the per-request maximum is {MaxTargetsPerTrigger.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        // Validate every non-null entry. yaml is rejected even if a legacy
        // row in scan_projects somehow contains it — see Repo.ScanProjects
        // doc. Empty/whitespace entries are dropped earlier in
        // ResolveEffectiveTargets so we don't see them here.
        foreach (var entry in effective)
        {
            if (entry is null) continue; // explicit auto-detect slot
            var validationError = ValidateTargetShape(entry);
            if (validationError is not null)
            {
                return TriggerScanResult.Error("invalid_scan_projects", validationError);
            }
        }

        // Snapshot canon once for the whole batch (§3.7 invariant). Every
        // row in this trigger replays the same canon even if the provider
        // is bumped between rows.
        var canonVersion = _canon.GetCurrent();
        var queuedAt = DateTime.UtcNow;
        var refValue = string.IsNullOrWhiteSpace(gitRef) ? null : gitRef;

        var rows = new List<Scan>(effective.Count);
        foreach (var entry in effective)
        {
            rows.Add(new Scan
            {
                OrgId = orgId,
                RepoId = repoId,
                TriggeredByUserId = userId,
                Ref = refValue,
                CanonVersion = canonVersion,
                Status = ScanStatus.Queued,
                QueuedAt = queuedAt,
                Target = entry,
            });
        }

        // Single SaveChanges = single transaction. EF rehydrates each row's
        // gen_random_uuid()-derived PublicId on the round-trip.
        _db.Scans.AddRange(rows);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var summaries = new List<ScanSummary>(rows.Count);
        foreach (var s in rows)
        {
            _log.LogInformation(
                "Scan triggered: org_id={OrgId} repo_id={RepoId} scan_id={ScanId} public_id={PublicId} canon={Canon} ref={Ref} target={Target}",
                orgId, repoId, s.Id, s.PublicId, canonVersion, s.Ref ?? "<default>", s.Target ?? "<auto>");
            summaries.Add(ToSummary(s, repo.Id, repo.GithubUrl));
        }

        return TriggerScanResult.Created(summaries);
    }

    /// <summary>
    /// Pick the effective per-trigger target list per the precedence rules
    /// in <see cref="IScanService.TriggerAsync"/>'s xml doc. Empty entries
    /// are dropped here so the validator below sees only real paths.
    /// Returns a list of <c>string?</c> where a single <c>null</c> entry
    /// means "1 scan, auto-detect" (kept distinct from "no scans" so the
    /// caller never accidentally inserts zero rows).
    /// </summary>
    private static IReadOnlyList<string?> ResolveEffectiveTargets(
        IReadOnlyList<string>? perRequest,
        string[]? saved)
    {
        var source = (perRequest is { Count: > 0 } pr)
            ? (IEnumerable<string>)pr
            : (saved is { Length: > 0 } s ? s : null);

        if (source is null)
        {
            // No override, no saved selection: 1 row, auto-detect.
            return new string?[] { null };
        }

        var result = new List<string?>(8);
        foreach (var raw in source)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var p = raw.Trim().Replace('\\', '/');
            if (p.StartsWith("./", System.StringComparison.Ordinal)) p = p[2..];
            result.Add(p);
        }

        // If everything was empty/whitespace fall back to auto-detect; we
        // never want to "succeed" with zero rows because the API contract
        // promises at least one publicId on Created.
        if (result.Count == 0) return new string?[] { null };
        return result;
    }

    /// <summary>
    /// Per-entry shape check. Each non-null entry must be a <c>.sln</c> or
    /// <c>.csproj</c> path. yaml entries are rejected — defensively, since
    /// the picker filters them and the saved column is validated at PUT,
    /// but a tampered DB or an old row could still slip through. Returns
    /// the human message on rejection; <c>null</c> when valid.
    /// </summary>
    private static string? ValidateTargetShape(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return "Empty target path is not allowed.";
        if (entry.StartsWith('/') || entry.Contains("../", System.StringComparison.Ordinal) || entry == "..")
            return $"Target path '{entry}' must be repo-relative.";

        var lower = entry.ToLowerInvariant();
        if (lower.EndsWith(".sln", System.StringComparison.Ordinal)) return null;
        if (lower.EndsWith(".csproj", System.StringComparison.Ordinal)) return null;

        if (lower == "lintty.yml"
            || lower.EndsWith("/lintty.yml", System.StringComparison.Ordinal)
            || lower.EndsWith(".yml", System.StringComparison.Ordinal)
            || lower.EndsWith(".yaml", System.StringComparison.Ordinal))
        {
            return $"Target '{entry}' is a yaml file; the dashboard only accepts .sln or .csproj entries (yaml without a 'projects:' list fails the resolver).";
        }

        return $"Target '{entry}' must end in .sln or .csproj.";
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
            Target: s.Target,
            Repo: new ScanRepoSummary(repoId, repoUrl));
    }
}
