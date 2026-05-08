using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Github;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;

namespace Lintty.WebInspector.Endpoints;

/// <summary>
/// Wiring for <c>/api/github/*</c>. ADR 0007 Apêndice E §E.7 / §E.8 —
/// Sprint 3 PR 7. The endpoints expose what GitHub knows about the
/// authenticated user's orgs + repos, so the dashboard can render the
/// "connect an org → pick a repo to import" flow.
///
/// <para>
/// <b>Why a separate file from <see cref="AuthGithubConnectEndpoints"/>.</b>
/// The connect endpoints own the OAuth lifecycle (start, callback, status,
/// revoke). These endpoints own the data layer on top of an already-active
/// connection. Mixing them in one file would conflate two CSRF surfaces
/// and two cookie scopes; splitting keeps the auth boundaries crisp.
/// </para>
///
/// <para>
/// <b>Why no cache here.</b> §E.7 sketches a 1h TTL on <c>github_orgs</c>
/// rows; V0 ships without it (every <c>GET /api/github/orgs</c> hits
/// <c>api.github.com</c>) because the cache would need an invalidation
/// handle the dashboard doesn't have yet. The UPSERT in ListOrgs keeps
/// the table fresh as a side effect, so when V1.1 adds a TTL the rows
/// already exist.
/// </para>
///
/// Routes:
/// <list type="bullet">
///   <item><description><c>GET /api/github/orgs</c> — list user's orgs from
///         GitHub + UPSERT into <c>github_orgs</c>.</description></item>
///   <item><description><c>GET /api/github/orgs/{login}/repos</c> — list
///         all repos (public + private) under <c>{login}</c>; requires
///         the user to be a member of that org per the connected token.</description></item>
/// </list>
///
/// All routes require an authenticated cookie (401) AND an active GitHub
/// token (403 <c>github_not_connected</c>).
/// </summary>
public static class GithubEndpoints
{
    public static void MapGithub(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/github").WithTags("Github");

        group.MapGet("/orgs", ListOrgs)
            .WithName("GithubListOrgs")
            .WithSummary("List the GitHub organizations the connected user has access to")
            .WithDescription(
                "Calls `GET /user/orgs` on GitHub with the user's connected token, returns the orgs " +
                "as `[{ id, login, avatarUrl }]`, and UPSERTs them into the `github_orgs` cache. " +
                "Requires the user to have completed the Connect GitHub flow first; otherwise " +
                "responds 403 `github_not_connected` with a hint pointing at /api/auth/github/connect/start.")
            .Produces<GithubOrgResponse[]>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        group.MapGet("/orgs/{login}/repos", ListReposForOrg)
            .WithName("GithubListReposForOrg")
            .WithSummary("List repos (public + private) under {login} visible to the connected user")
            .WithDescription(
                "Calls `GET /orgs/{login}/repos?type=all` on GitHub with the user's connected token. " +
                "Refuses with 404 `org_not_in_user_orgs` when the user has no row in `github_orgs` for " +
                "the requested login (defends against pinging an arbitrary org). V0 returns up to 100 " +
                "repos per call; pagination is V1.1.")
            .Produces<GithubRepoResponse[]>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }

    // ── GET /api/github/orgs ───────────────────────────────────────────────
    private static async Task<IResult> ListOrgs(
        HttpContext ctx,
        IGitHubUserTokenStore tokenStore,
        IGitHubOrgsClient orgsClient,
        LinttyDbContext db,
        ILoggerFactory logFactory,
        CancellationToken ct)
    {
        var log = logFactory.CreateLogger("Github.Orgs");

        if (!TryGetUserId(ctx, out var userId)) return Unauthorized();

        var token = await tokenStore.GetActiveTokenAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token)) return GithubNotConnected();

        IReadOnlyList<GitHubOrgSummary> orgs;
        try
        {
            orgs = await orgsClient.ListOrgsAsync(token, ct).ConfigureAwait(false);
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized
                                          || ex.StatusCode == HttpStatusCode.Forbidden)
        {
            // Token revoked at GitHub between our store-read and the API
            // call. Surface the same shape as "no token" so the frontend
            // funnels both into "reconnect".
            log.LogWarning(ex,
                "GitHub /user/orgs returned {Status} for user_id={UserId}; treating as not-connected.",
                ex.StatusCode, userId);
            return GithubNotConnected();
        }

        // UPSERT into github_orgs. Spec §E.7: row is per-user (PK is the
        // synthetic id), uniqueness is (user_id, github_org_id). A re-call
        // updates login + avatar in place — both can drift between calls
        // (org rename, avatar change).
        await UpsertOrgsAsync(db, userId, orgs, ct).ConfigureAwait(false);

        var response = orgs
            .Select(o => new GithubOrgResponse
            {
                Id = o.Id,
                Login = o.Login,
                AvatarUrl = o.AvatarUrl,
            })
            .ToArray();
        return Results.Json(response, statusCode: StatusCodes.Status200OK);
    }

    // ── GET /api/github/orgs/{login}/repos ─────────────────────────────────
    private static async Task<IResult> ListReposForOrg(
        string login,
        HttpContext ctx,
        IGitHubUserTokenStore tokenStore,
        IGitHubOrgsClient orgsClient,
        LinttyDbContext db,
        ILoggerFactory logFactory,
        CancellationToken ct)
    {
        var log = logFactory.CreateLogger("Github.Repos");

        if (!TryGetUserId(ctx, out var userId)) return Unauthorized();

        var token = await tokenStore.GetActiveTokenAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token)) return GithubNotConnected();

        // §E.7 access check: the user can only ask for orgs that are in
        // their cached github_orgs. The cache is populated by ListOrgs so
        // a fresh login that hasn't called /api/github/orgs yet must do so
        // first — which is what the dashboard already does (orgs picker
        // populates before the repos picker shows up). Defends against
        // /api/github/orgs/microsoft/repos when the user isn't in
        // Microsoft.
        var hasOrg = await db.GithubOrgs
            .AsNoTracking()
            .AnyAsync(o => o.UserId == userId && o.GithubOrgLogin == login, ct)
            .ConfigureAwait(false);
        if (!hasOrg)
        {
            return Results.Json(
                new ErrorResponse
                {
                    Error = "org_not_in_user_orgs",
                    Message = "Você não tem acesso a essa organização ou ainda não atualizou a lista. Chame GET /api/github/orgs primeiro.",
                },
                statusCode: StatusCodes.Status404NotFound);
        }

        IReadOnlyList<GitHubRepoSummary> repos;
        try
        {
            repos = await orgsClient.ListReposAsync(token, login, ct).ConfigureAwait(false);
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized
                                          || ex.StatusCode == HttpStatusCode.Forbidden)
        {
            log.LogWarning(ex,
                "GitHub /orgs/{Login}/repos returned {Status} for user_id={UserId}; treating as not-connected.",
                login, ex.StatusCode, userId);
            return GithubNotConnected();
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Org existed in our cache but GitHub now says 404 — org
            // deleted or user removed. Mirror the cache-miss response so
            // the frontend handles it the same way.
            log.LogWarning(ex,
                "GitHub /orgs/{Login}/repos returned 404 for user_id={UserId}; org may have been deleted.",
                login, userId);
            return Results.Json(
                new ErrorResponse
                {
                    Error = "org_not_in_user_orgs",
                    Message = "Essa organização não existe mais ou você foi removido dela. Atualize a lista de orgs.",
                },
                statusCode: StatusCodes.Status404NotFound);
        }

        var response = repos
            .Select(r => new GithubRepoResponse
            {
                Id = r.Id,
                Name = r.Name,
                FullName = r.FullName,
                Private = r.IsPrivate,
                DefaultBranch = r.DefaultBranch,
                HtmlUrl = r.HtmlUrl,
            })
            .ToArray();
        return Results.Json(response, statusCode: StatusCodes.Status200OK);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static async Task UpsertOrgsAsync(
        LinttyDbContext db,
        long userId,
        IReadOnlyList<GitHubOrgSummary> orgs,
        CancellationToken ct)
    {
        if (orgs.Count == 0) return;

        // Read all existing rows for this user in one round-trip; then
        // patch in memory and SaveChanges once. Tens of orgs at most —
        // well under what would justify a Dapper bulk UPSERT.
        var existing = await db.GithubOrgs
            .Where(o => o.UserId == userId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var byId = existing.ToDictionary(o => o.GithubOrgId);

        foreach (var summary in orgs)
        {
            if (byId.TryGetValue(summary.Id, out var row))
            {
                // Refresh cosmetic fields. Login can change on org rename;
                // avatar URL changes when the admin uploads a new logo.
                if (!string.Equals(row.GithubOrgLogin, summary.Login, StringComparison.Ordinal))
                    row.GithubOrgLogin = summary.Login;
                if (!string.Equals(row.GithubOrgAvatarUrl, summary.AvatarUrl, StringComparison.Ordinal))
                    row.GithubOrgAvatarUrl = summary.AvatarUrl;
            }
            else
            {
                db.GithubOrgs.Add(new GithubOrg
                {
                    UserId = userId,
                    GithubOrgId = summary.Id,
                    GithubOrgLogin = summary.Login,
                    GithubOrgAvatarUrl = summary.AvatarUrl,
                    ConnectedAt = DateTime.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static bool TryGetUserId(HttpContext ctx, out long userId)
    {
        userId = 0;
        if (ctx.User.Identity?.IsAuthenticated != true) return false;
        var idClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(idClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out userId);
    }

    private static IResult Unauthorized()
        => Results.Json(
            new ErrorResponse { Error = "unauthorized", Message = "Authentication required." },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult GithubNotConnected()
        => Results.Json(
            new ErrorResponse
            {
                Error = "github_not_connected",
                Message = "Conecte sua conta GitHub primeiro: /api/auth/github/connect/start",
            },
            statusCode: StatusCodes.Status403Forbidden);
}

// ── DTOs ───────────────────────────────────────────────────────────────────

/// <summary>Response shape for <c>GET /api/github/orgs</c>.</summary>
public sealed class GithubOrgResponse
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
    [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; set; }
}

/// <summary>Response shape for <c>GET /api/github/orgs/{login}/repos</c>.</summary>
public sealed class GithubRepoResponse
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("fullName")] public string FullName { get; set; } = string.Empty;
    [JsonPropertyName("private")] public bool Private { get; set; }
    [JsonPropertyName("defaultBranch")] public string DefaultBranch { get; set; } = string.Empty;
    [JsonPropertyName("htmlUrl")] public string HtmlUrl { get; set; } = string.Empty;
}
