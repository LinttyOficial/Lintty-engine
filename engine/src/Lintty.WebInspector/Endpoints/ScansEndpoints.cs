using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lintty.WebInspector.Artifacts;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Scans;

namespace Lintty.WebInspector.Endpoints;

/// <summary>
/// Wiring for the org-bound scan lifecycle. ADR 0007 §3.7 + §3.8 — Sprint 3
/// PR 4. Routes:
/// <list type="bullet">
///   <item><description><c>POST /api/scans</c> — trigger a new scan.</description></item>
///   <item><description><c>GET  /api/scans/{public_id}</c> — poll status.</description></item>
///   <item><description><c>GET  /api/scans/{public_id}/laudo.pdf</c> — download PDF.</description></item>
///   <item><description><c>GET  /api/scans/{public_id}/report.json</c> — download JSON.</description></item>
/// </list>
/// All routes require a logged-in cookie. Anonymous traffic uses the V0
/// <c>/api/jobs/*</c> path, untouched. Cross-tenant lookups always return
/// 404 (§3.5 — never 403, to avoid resource enumeration).
///
/// <para>
/// The history sibling (<c>GET /api/repos/{id}/scans</c>) lives on
/// <see cref="ReposEndpoints"/> because it nests under the repo namespace.
/// </para>
/// </summary>
public static class ScansEndpoints
{
    public static void MapScans(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/scans").WithTags("Scans");

        group.MapPost("/", TriggerScan)
            .WithName("TriggerScan")
            .WithSummary("Trigger one or more scans against a repo in the active org")
            .WithDescription(
                "Validates that the repo belongs to the caller's active org and is not " +
                "soft-deleted, snapshots the current canon version onto every new row " +
                "(§3.7 determinism invariant), and persists each requested target as its " +
                "own scan with status=queued. Returns `{ publicIds: string[] }` in trigger " +
                "order. The worker picks rows up asynchronously; clients poll " +
                "`GET /api/scans/{public_id}` for each one. Sprint 3 PR S2.")
            .Accepts<TriggerScanRequest>("application/json")
            .Produces<BatchTriggerResponse>(StatusCodes.Status201Created, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json");

        group.MapGet("/{publicId:guid}", GetScan)
            .WithName("GetScan")
            .WithSummary("Poll a scan by its public id")
            .WithDescription(
                "Returns 200 with the scan's current state if the caller's active org owns it. " +
                "Cross-tenant access returns 404 by design (§3.5).")
            .Produces<ScanResponse>(StatusCodes.Status200OK, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json");

        group.MapGet("/{publicId:guid}/laudo.pdf", GetPdf)
            .WithName("GetScanPdf")
            .WithSummary("Download the audit PDF for a completed scan")
            .WithDescription(
                "Returns 200 streaming `application/pdf`. 409 if the scan is still " +
                "queued/running, 404 if cross-tenant or unknown, 404 (defensive) if the " +
                "scan completed but the artifact is missing on the store.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, "application/json");

        group.MapGet("/{publicId:guid}/report.json", GetJson)
            .WithName("GetScanReportJson")
            .WithSummary("Download the engine JSON report for a completed scan")
            .WithDescription(
                "Returns 200 streaming `application/json` — the exact bytes whose SHA-256 " +
                "is recorded as `hash_content` on the row and in the PDF footer. Same " +
                "404/409 rules as `laudo.pdf`.")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, "application/json");
    }

    // ── POST /api/scans ─────────────────────────────────────────────────────
    private static async Task<IResult> TriggerScan(
        HttpContext ctx,
        TriggerScanRequest? body,
        IScanService scanService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;
        var (orgId, userId) = (who.OrgId!.Value, who.UserId!.Value);

        if (body is null || body.RepoId <= 0)
        {
            return BadRequest("invalid_payload", "repoId is required and must be > 0.");
        }

        var result = await scanService.TriggerAsync(
            orgId, userId, body.RepoId, body.Ref, body.Targets, ct).ConfigureAwait(false);

        return result.Outcome switch
        {
            TriggerScanOutcome.Created
                => Results.Json(
                    new BatchTriggerResponse
                    {
                        PublicIds = result.Scans.Select(s => s.PublicId.ToString("D")).ToArray(),
                    },
                    statusCode: StatusCodes.Status201Created),
            TriggerScanOutcome.RepoNotFound
                => NotFound("Repo not found."),
            TriggerScanOutcome.Error
                => BadRequest(result.ErrorCode!, result.ErrorMessage!),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ── GET /api/scans/{public_id} ──────────────────────────────────────────
    private static async Task<IResult> GetScan(
        Guid publicId,
        HttpContext ctx,
        IScanService scanService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;

        var scan = await scanService.GetByPublicIdAsync(who.OrgId!.Value, publicId, ct).ConfigureAwait(false);
        if (scan is null) return NotFound("Scan not found.");
        return Results.Json(ToResponse(scan), statusCode: StatusCodes.Status200OK);
    }

    // ── GET /api/scans/{public_id}/laudo.pdf ────────────────────────────────
    private static Task<IResult> GetPdf(
        Guid publicId,
        HttpContext ctx,
        IScanService scanService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
        => DownloadArtifactAsync(publicId, ArtifactKind.LaudoPdf, ctx, scanService, tenant, db, ct);

    // ── GET /api/scans/{public_id}/report.json ──────────────────────────────
    private static Task<IResult> GetJson(
        Guid publicId,
        HttpContext ctx,
        IScanService scanService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
        => DownloadArtifactAsync(publicId, ArtifactKind.ReportJson, ctx, scanService, tenant, db, ct);

    private static async Task<IResult> DownloadArtifactAsync(
        Guid publicId,
        ArtifactKind kind,
        HttpContext ctx,
        IScanService scanService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;

        var result = await scanService.OpenArtifactAsync(who.OrgId!.Value, publicId, kind, ct).ConfigureAwait(false);
        return result.Outcome switch
        {
            OpenScanArtifactOutcome.NotFound => NotFound("Scan not found."),
            OpenScanArtifactOutcome.Missing => NotFound("Artifact missing."),
            OpenScanArtifactOutcome.NotCompleted => Conflict("scan_not_completed", "Scan is not completed yet."),
            OpenScanArtifactOutcome.Found
                // Range processing is off — laudos are < 1 MB; the V0 jobs
                // path doesn't support range either and we keep the
                // surfaces consistent.
                => Results.Stream(result.Stream!, result.ContentType!, result.FileDownloadName!,
                    enableRangeProcessing: false),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ── Caller resolution (mirrors ReposEndpoints.ResolveCallerAsync) ──────

    /// <summary>
    /// Resolves the calling user + active org or returns the appropriate
    /// 401/403 envelope. Same fallback (no <c>org_id</c> claim → first
    /// membership) <see cref="ReposEndpoints"/> uses, so a fresh user lands
    /// on the same default org regardless of which endpoint they hit first.
    /// Duplicated by design — both endpoint files are minimal-API static
    /// classes and PR 3 chose this pattern explicitly. If a third file
    /// needs it, extract.
    /// </summary>
    private static async Task<CallerLookup> ResolveCallerAsync(
        HttpContext ctx,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
            return CallerLookup.Fail(Unauthorized());

        var idClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return CallerLookup.Fail(Unauthorized());

        if (tenant.OrgId is long orgId)
            return CallerLookup.Ok(orgId, userId);

        var first = await db.OrgMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Id)
            .Select(m => (long?)m.OrgId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (!first.HasValue)
        {
            return CallerLookup.Fail(Results.Json(
                new ErrorResponse { Error = "no_org", Message = "User has no organization." },
                statusCode: StatusCodes.Status403Forbidden));
        }
        return CallerLookup.Ok(first.Value, userId);
    }

    private readonly struct CallerLookup
    {
        public IResult? Failure { get; }
        public long? OrgId { get; }
        public long? UserId { get; }

        private CallerLookup(IResult? failure, long? orgId, long? userId)
        {
            Failure = failure; OrgId = orgId; UserId = userId;
        }

        public static CallerLookup Ok(long orgId, long userId) => new(null, orgId, userId);
        public static CallerLookup Fail(IResult result) => new(result, null, null);
    }

    // ── Response helpers ───────────────────────────────────────────────────

    internal static ScanResponse ToResponse(ScanSummary s) => new()
    {
        PublicId = s.PublicId.ToString("D"),
        Status = s.Status,
        QueuedAt = s.QueuedAt.ToString("o", CultureInfo.InvariantCulture),
        StartedAt = s.StartedAt?.ToString("o", CultureInfo.InvariantCulture),
        CompletedAt = s.CompletedAt?.ToString("o", CultureInfo.InvariantCulture),
        Ref = s.Ref,
        CanonVersion = s.CanonVersion,
        HashContent = s.HashContent,
        Error = s.Error,
        TriggeredByUserId = s.TriggeredByUserId,
        Target = s.Target,
        Repo = new ScanRepoResponse
        {
            Id = s.Repo.Id,
            GithubUrl = s.Repo.GithubUrl,
        },
    };

    private static IResult Unauthorized()
        => Results.Json(
            new ErrorResponse { Error = "unauthorized", Message = "Authentication required." },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult NotFound(string message)
        => Results.Json(
            new ErrorResponse { Error = "not_found", Message = message },
            statusCode: StatusCodes.Status404NotFound);

    private static IResult BadRequest(string code, string message)
        => Results.Json(
            new ErrorResponse { Error = code, Message = message },
            statusCode: StatusCodes.Status400BadRequest);

    private static IResult Conflict(string code, string message)
        => Results.Json(
            new ErrorResponse { Error = code, Message = message },
            statusCode: StatusCodes.Status409Conflict);
}

// ── DTOs ───────────────────────────────────────────────────────────────────

/// <summary>Request body for <c>POST /api/scans</c>.</summary>
public sealed class TriggerScanRequest
{
    /// <summary>Numeric id of a repo previously registered via <c>/api/repos</c>.</summary>
    [JsonPropertyName("repoId")] public long RepoId { get; set; }

    /// <summary>Optional branch / tag / sha. <c>null</c> means "let the worker
    /// resolve HEAD against the repo's default branch", same as the V0
    /// anonymous flow when <c>ref</c> is omitted.</summary>
    [JsonPropertyName("ref")] public string? Ref { get; set; }

    /// <summary>
    /// Optional per-request override of the targets to expand into scans.
    /// Each entry must be a repo-relative <c>.sln</c> or <c>.csproj</c>
    /// path. Sprint 3 PR S2: <c>null</c> or empty falls back to the repo's
    /// saved <c>scan_projects</c>; if that is also empty, the trigger
    /// produces a single auto-detect scan.
    /// </summary>
    [JsonPropertyName("targets")] public string[]? Targets { get; set; }
}

/// <summary>
/// Response for <c>POST /api/scans</c>. Sprint 3 PR S2 — always returns a
/// list of public ids in trigger order, length 1..N. Clients poll each
/// id independently; the dashboard renders one row per id.
/// </summary>
public sealed class BatchTriggerResponse
{
    [JsonPropertyName("publicIds")] public string[] PublicIds { get; set; } = System.Array.Empty<string>();
}

/// <summary>Wire shape for every scan response (trigger, poll, history list).</summary>
public sealed class ScanResponse
{
    [JsonPropertyName("publicId")] public string PublicId { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("queuedAt")] public string QueuedAt { get; set; } = string.Empty;
    [JsonPropertyName("startedAt")] public string? StartedAt { get; set; }
    [JsonPropertyName("completedAt")] public string? CompletedAt { get; set; }
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("canonVersion")] public string CanonVersion { get; set; } = string.Empty;
    [JsonPropertyName("hashContent")] public string? HashContent { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("triggeredByUserId")] public long TriggeredByUserId { get; set; }
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("repo")] public ScanRepoResponse Repo { get; set; } = new();
}

public sealed class ScanRepoResponse
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("githubUrl")] public string GithubUrl { get; set; } = string.Empty;
}
