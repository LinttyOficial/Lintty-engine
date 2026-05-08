using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Repos.Preflight;

namespace Lintty.WebInspector.Endpoints;

/// <summary>
/// Sprint 3 PR S1 — preflight + scan-target curation. Two routes nested
/// under <c>/api/repos/{id}</c>:
/// <list type="bullet">
///   <item><description><c>GET /api/repos/{id}/preflight</c> — discovers
///         scan candidates and reports current status.</description></item>
///   <item><description><c>PUT /api/repos/{id}/scan-target</c> — persists
///         the user's curated selection.</description></item>
/// </list>
/// Both honor the same tenant-isolation invariant as
/// <see cref="ReposEndpoints"/>: cookie required, cross-tenant lookups
/// return 404 (§3.5), never 403.
/// </summary>
public static class ReposPreflightEndpoints
{
    public static void MapReposPreflight(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/repos").WithTags("Repos");

        group.MapGet("/{repoId:long}/preflight", GetPreflight)
            .WithName("GetRepoPreflight")
            .WithSummary("Discover scan candidates for a repo (.sln, .csproj, lintty.yml)")
            .WithDescription(
                "Walks the repo's GitHub Tree API (no clone) to enumerate candidate scan targets " +
                "and combines the result with any saved selection in `repos.scan_projects`. " +
                "Status is `ready` (saved selection or unambiguous auto-detect), `needs_config` " +
                "(candidates exist but auto-detect can't pick one), or `no_dotnet_project`. " +
                "Cross-tenant access returns 404 (§3.5).")
            .Produces<PreflightResponse>(StatusCodes.Status200OK, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway, "application/json");

        group.MapPut("/{repoId:long}/scan-target", SetScanTarget)
            .WithName("SetRepoScanTarget")
            .WithSummary("Persist the user's curated scan target paths")
            .WithDescription(
                "Validates the supplied paths against the discovered candidate set and the " +
                "allowed combinations (1 yaml, 1 sln, 1 csproj, or 2+ csprojs). Empty list " +
                "clears the saved selection and returns the repo to auto-detect. 204 on success, " +
                "400 on combination/path errors, 404 on cross-tenant.")
            .Accepts<SetScanTargetRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json");
    }

    // ── GET /api/repos/{id}/preflight ─────────────────────────────────────
    private static async Task<IResult> GetPreflight(
        long repoId,
        HttpContext ctx,
        IRepoPreflightService preflight,
        ITenantContext tenant,
        LinttyDbContext db,
        ILoggerFactory logFactory,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;

        try
        {
            var result = await preflight.GetAsync(repoId, who.OrgId!.Value, ct).ConfigureAwait(false);
            if (result is null) return NotFound();
            return Results.Json(ToResponse(result), statusCode: StatusCodes.Status200OK);
        }
        catch (RepoTargetDiscoveryException ex)
        {
            // Map discovery errors to HTTP. Same envelope as the rest of
            // the API.
            var log = logFactory.CreateLogger("RepoPreflight");
            log.LogWarning(ex,
                "Preflight discovery failed for org_id={OrgId} repo_id={RepoId} category={Category}",
                who.OrgId!.Value, repoId, ex.Error);
            return ex.Error switch
            {
                RepoTargetDiscoveryError.NotAccessible
                    => NotFound(),
                RepoTargetDiscoveryError.Forbidden
                    => Results.Json(
                        new ErrorResponse { Error = "github_forbidden", Message = ex.Message },
                        statusCode: StatusCodes.Status403Forbidden),
                RepoTargetDiscoveryError.Transient
                    => Results.Json(
                        new ErrorResponse { Error = "github_transient", Message = "Erro temporário ao consultar o GitHub. Tente novamente em instantes." },
                        statusCode: StatusCodes.Status502BadGateway),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        }
    }

    // ── PUT /api/repos/{id}/scan-target ───────────────────────────────────
    private static async Task<IResult> SetScanTarget(
        long repoId,
        SetScanTargetRequest? body,
        HttpContext ctx,
        IRepoPreflightService preflight,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;

        var projects = (IReadOnlyList<string>)(body?.Projects ?? System.Array.Empty<string>());

        try
        {
            var outcome = await preflight.SetScanProjectsAsync(repoId, who.OrgId!.Value, projects, ct).ConfigureAwait(false);
            return outcome.Status switch
            {
                "ok" => Results.NoContent(),
                "invalid" => Results.Json(
                    new ErrorResponse { Error = "invalid_scan_projects", Message = outcome.Message ?? "Invalid selection." },
                    statusCode: StatusCodes.Status400BadRequest),
                "not_found" => NotFound(),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        }
        catch (RepoTargetDiscoveryException ex)
        {
            // Same mapping as the GET path — the SetScanProjectsAsync
            // implementation calls discovery to validate the candidate set,
            // so the same upstream errors can surface.
            return ex.Error switch
            {
                RepoTargetDiscoveryError.NotAccessible
                    => NotFound(),
                RepoTargetDiscoveryError.Forbidden
                    => Results.Json(
                        new ErrorResponse { Error = "github_forbidden", Message = ex.Message },
                        statusCode: StatusCodes.Status403Forbidden),
                RepoTargetDiscoveryError.Transient
                    => Results.Json(
                        new ErrorResponse { Error = "github_transient", Message = "Erro temporário ao consultar o GitHub. Tente novamente em instantes." },
                        statusCode: StatusCodes.Status502BadGateway),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        }
    }

    // ── Caller resolution (mirrors ReposEndpoints.ResolveCallerAsync) ─────

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

    // ── Response helpers ──────────────────────────────────────────────────

    private static PreflightResponse ToResponse(PreflightResult r) => new()
    {
        Status = r.Status,
        AutoDetected = r.AutoDetected is null ? null : new PreflightAutoDetectedDto
        {
            Kind = r.AutoDetected.Kind,
            Path = r.AutoDetected.Path,
        },
        ScanProjects = r.ScanProjects?.ToArray(),
        Candidates = r.Candidates.Select(c => new PreflightCandidateDto
        {
            Kind = c.Kind,
            Path = c.Path,
        }).ToArray(),
        Truncated = r.Truncated,
        Reason = r.Reason,
    };

    private static IResult Unauthorized()
        => Results.Json(
            new ErrorResponse { Error = "unauthorized", Message = "Authentication required." },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult NotFound()
        => Results.Json(
            new ErrorResponse { Error = "not_found", Message = "Repo not found." },
            statusCode: StatusCodes.Status404NotFound);
}

// ── DTOs ───────────────────────────────────────────────────────────────────

/// <summary>Request body for <c>PUT /api/repos/{id}/scan-target</c>.</summary>
public sealed class SetScanTargetRequest
{
    /// <summary>Repo-relative POSIX paths the user picked. Empty list
    /// clears the saved selection and returns the repo to auto-detect.</summary>
    [JsonPropertyName("projects")] public string[]? Projects { get; set; }
}

/// <summary>Wire shape for <c>GET /api/repos/{id}/preflight</c>.</summary>
public sealed class PreflightResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("autoDetected")] public PreflightAutoDetectedDto? AutoDetected { get; set; }
    [JsonPropertyName("scanProjects")] public string[]? ScanProjects { get; set; }
    [JsonPropertyName("candidates")] public PreflightCandidateDto[] Candidates { get; set; } = System.Array.Empty<PreflightCandidateDto>();
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

public sealed class PreflightAutoDetectedDto
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
}

public sealed class PreflightCandidateDto
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
}
