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
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Repos;
using Lintty.WebInspector.Scans;

namespace Lintty.WebInspector.Endpoints;

/// <summary>
/// Wiring for <c>/api/repos/*</c>. ADR 0007 Sprint 3 / PR 3 — manual add only.
/// The Org-import sibling (<c>POST /api/orgs/{slug}/repos/import</c>) lands in
/// PR 7 with the OAuth user-token elevation flow.
///
/// Routes:
/// <list type="bullet">
///   <item><description><c>POST /api/repos</c> — manual add by URL.</description></item>
///   <item><description><c>GET  /api/repos</c> — list active repos in the active org.</description></item>
///   <item><description><c>GET  /api/repos/{id}</c> — fetch a single repo (404 on cross-tenant per §3.5).</description></item>
///   <item><description><c>DELETE /api/repos/{id}</c> — soft-delete; scans survive (§E.8).</description></item>
/// </list>
///
/// Every route requires a logged-in cookie. Anonymous traffic (the V0
/// <c>/api/jobs</c> path) is untouched. Tenant scope is resolved via
/// <see cref="ITenantContext"/>, with a first-membership fallback for users
/// whose cookie was issued before any <c>org_id</c> claim existed.
/// </summary>
public static class ReposEndpoints
{
    public static void MapRepos(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/repos").WithTags("Repos");

        group.MapPost("/", AddRepo)
            .WithName("AddRepo")
            .WithSummary("Add a public GitHub repo to the active org by URL")
            .WithDescription(
                "Manually registers a public GitHub repository under the caller's active org. " +
                "The URL is canonicalised (lowercased host, no trailing `.git`, no trailing slash) " +
                "before storage. Re-posting the same canonical URL while the previous row is still " +
                "active is idempotent — the existing row is returned with `200 OK`. Private repos " +
                "are rejected here; the Connect GitHub flow (PR 7) is the only path for those.")
            .Accepts<AddRepoRequest>("application/json")
            .Produces<RepoResponse>(StatusCodes.Status201Created, "application/json")
            .Produces<RepoResponse>(StatusCodes.Status200OK, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, "application/json");

        group.MapGet("/", ListRepos)
            .WithName("ListRepos")
            .WithSummary("List active repos in the active org")
            .Produces<RepoResponse[]>(StatusCodes.Status200OK, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json");

        group.MapGet("/{repoId:long}", GetRepo)
            .WithName("GetRepo")
            .WithSummary("Fetch a single repo by id, scoped to the active org")
            .WithDescription(
                "Returns 200 if the repo belongs to the caller's active org and is not soft-deleted. " +
                "Cross-tenant access (cookie of org A asking for a repo of org B) returns 404 by design " +
                "(ADR 0007 §3.5 — 404, not 403, to avoid resource enumeration).")
            .Produces<RepoResponse>(StatusCodes.Status200OK, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json");

        group.MapGet("/{repoId:long}/scans", ListRepoScans)
            .WithName("ListRepoScans")
            .WithSummary("List the scan history of a repo in the active org, newest first")
            .WithDescription(
                "Returns the scans queued/running/completed/failed for the repo, ordered " +
                "by `queued_at DESC`. ADR 0007 §3.7 — public id is the URL-facing identifier; " +
                "internal numeric ids never leak. Cross-tenant access (cookie of org A asking " +
                "for a repo of org B) returns 404 by design (§3.5).")
            .Produces<ScanResponse[]>(StatusCodes.Status200OK, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json");

        group.MapDelete("/{repoId:long}", DeleteRepo)
            .WithName("DeleteRepo")
            .WithSummary("Soft-delete a repo from the active org")
            .WithDescription(
                "Sets `deleted_at` on the repo. Historic scans referencing it remain queryable " +
                "(Apêndice E §E.8). Cross-tenant deletes return 404, never 403, per §3.5.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, "application/json");
    }

    // ── POST /api/repos ────────────────────────────────────────────────────
    private static async Task<IResult> AddRepo(
        HttpContext ctx,
        AddRepoRequest? body,
        IRepoService repoService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;
        var (orgId, userId) = (who.OrgId!.Value, who.UserId!.Value);

        var url = body?.GithubUrl;
        var result = await repoService.AddManualAsync(orgId, userId, url, ct).ConfigureAwait(false);

        return result.Outcome switch
        {
            RepoOperationOutcome.Created
                => Results.Json(ToResponse(result.Repo!), statusCode: StatusCodes.Status201Created),
            RepoOperationOutcome.AlreadyExists
                => Results.Json(ToResponse(result.Repo!), statusCode: StatusCodes.Status200OK),
            RepoOperationOutcome.Error
                => BadRequest(result.ErrorCode!, result.ErrorMessage!),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ── GET /api/repos ─────────────────────────────────────────────────────
    private static async Task<IResult> ListRepos(
        HttpContext ctx,
        IRepoService repoService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;

        var rows = await repoService.ListAsync(who.OrgId!.Value, ct).ConfigureAwait(false);
        return Results.Json(rows.Select(ToResponse).ToList(), statusCode: StatusCodes.Status200OK);
    }

    // ── GET /api/repos/{id} ────────────────────────────────────────────────
    private static async Task<IResult> GetRepo(
        long repoId,
        HttpContext ctx,
        IRepoService repoService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;

        var row = await repoService.GetAsync(who.OrgId!.Value, repoId, ct).ConfigureAwait(false);
        if (row is null) return NotFound();
        return Results.Json(ToResponse(row), statusCode: StatusCodes.Status200OK);
    }

    // ── GET /api/repos/{id}/scans ──────────────────────────────────────────
    private static async Task<IResult> ListRepoScans(
        long repoId,
        HttpContext ctx,
        IRepoService repoService,
        IScanService scanService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;

        // Mirror the GET-by-id contract: validate repo ownership FIRST so a
        // cross-tenant lookup returns 404 (§3.5) rather than an empty list.
        // The empty-list semantics (repo exists, no scans yet) only kicks in
        // when the repo legitimately belongs to the caller.
        var repo = await repoService.GetAsync(who.OrgId!.Value, repoId, ct).ConfigureAwait(false);
        if (repo is null) return NotFound();

        var rows = await scanService.ListByRepoAsync(who.OrgId!.Value, repoId, ct).ConfigureAwait(false);
        return Results.Json(
            rows.Select(ScansEndpoints.ToResponse).ToList(),
            statusCode: StatusCodes.Status200OK);
    }

    // ── DELETE /api/repos/{id} ─────────────────────────────────────────────
    private static async Task<IResult> DeleteRepo(
        long repoId,
        HttpContext ctx,
        IRepoService repoService,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        var who = await ResolveCallerAsync(ctx, tenant, db, ct).ConfigureAwait(false);
        if (who.Failure is not null) return who.Failure;

        var ok = await repoService.SoftDeleteAsync(who.OrgId!.Value, repoId, ct).ConfigureAwait(false);
        return ok ? Results.NoContent() : NotFound();
    }

    // ── Caller resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Resolves the calling user + active org or returns the appropriate 401/403
    /// envelope. The fallback path (no <c>org_id</c> claim → first membership)
    /// mirrors <c>BuildMeResponseAsync</c> in <see cref="AuthEndpoints"/> so a
    /// fresh user lands on the same default org regardless of which endpoint
    /// they hit first.
    /// </summary>
    private static async Task<CallerLookup> ResolveCallerAsync(
        HttpContext ctx,
        ITenantContext tenant,
        LinttyDbContext db,
        CancellationToken ct)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
            return CallerLookup.Fail(Unauthorized());

        // The middleware writes UserId to HttpContext.Items, but we re-read
        // the claim directly so this works even if a future test bypasses
        // the middleware. Same parsing rules.
        var idClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return CallerLookup.Fail(Unauthorized());

        if (tenant.OrgId is long orgId)
            return CallerLookup.Ok(orgId, userId);

        // Fallback: first membership wins. Same query as
        // BuildMeResponseAsync's "current = memberships[0]". Defensive 403 if
        // the user really has zero orgs (signup guarantees one; this would
        // mean a manual DB tweak or a partial signup).
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

    private static RepoResponse ToResponse(Repos.RepoSummary r) => new()
    {
        Id = r.Id,
        GithubUrl = r.GithubUrl,
        IsPrivate = r.IsPrivate,
        CreatedAt = r.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
        AddedBy = new RepoAddedByDto
        {
            Id = r.AddedByUserId,
            DisplayName = r.AddedByDisplayName,
        },
    };

    private static IResult Unauthorized()
        => Results.Json(
            new ErrorResponse { Error = "unauthorized", Message = "Authentication required." },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult NotFound()
        => Results.Json(
            new ErrorResponse { Error = "not_found", Message = "Repo not found." },
            statusCode: StatusCodes.Status404NotFound);

    private static IResult BadRequest(string code, string message)
        => Results.Json(
            new ErrorResponse { Error = code, Message = message },
            statusCode: StatusCodes.Status400BadRequest);
}

// ── DTOs ───────────────────────────────────────────────────────────────────

/// <summary>Request body for <c>POST /api/repos</c>.</summary>
/// <example>
/// { "githubUrl": "https://github.com/owner/repo" }
/// </example>
public sealed class AddRepoRequest
{
    /// <summary>HTTPS URL of a public GitHub repository.</summary>
    [JsonPropertyName("githubUrl")] public string? GithubUrl { get; set; }
}

/// <summary>Wire shape returned by every successful repo endpoint.</summary>
public sealed class RepoResponse
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("githubUrl")] public string GithubUrl { get; set; } = string.Empty;
    [JsonPropertyName("isPrivate")] public bool IsPrivate { get; set; }
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = string.Empty;
    [JsonPropertyName("addedBy")] public RepoAddedByDto AddedBy { get; set; } = new();
}

public sealed class RepoAddedByDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
}
