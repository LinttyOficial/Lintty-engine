using System;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Configuration;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;

namespace Lintty.WebInspector.Endpoints;

/// <summary>
/// Wiring for the elevated GitHub OAuth <c>connect</c> flow. ADR 0007
/// Apêndice E §E.5/§E.6 / Sprint 3 PR 6.
///
/// **Distinct from the login flow** in <see cref="AuthEndpoints"/>:
/// <list type="bullet">
///   <item><description>Login asks <c>read:user user:email</c> and discards the token after fetching the profile.</description></item>
///   <item><description>Connect asks <c>repo</c> + <c>read:org</c> (elevated) and persists the token encrypted at rest in <c>github_user_tokens</c> via <see cref="IGitHubUserTokenStore"/>.</description></item>
/// </list>
/// Routes:
/// <list type="bullet">
///   <item><description><c>GET    /api/auth/github/connect/start</c> — redirect to GitHub authorize with elevated scopes.</description></item>
///   <item><description><c>GET    /api/auth/github/connect/callback</c> — exchange code, validate scopes + identity, persist token.</description></item>
///   <item><description><c>GET    /api/auth/github/connect</c> — status: returns <c>connected</c> + scopes + grant timestamps.</description></item>
///   <item><description><c>DELETE /api/auth/github/connect</c> — soft-revoke locally; response includes the upstream URL the user can hit on github.com if they want to fully revoke.</description></item>
/// </list>
/// All routes require an authenticated cookie (<c>401</c> when anonymous).
/// The state cookie is named <c>lintty_oauth_connect_state</c> — different
/// from the login flow's <c>lintty_oauth_state</c> — so the two CSRF tokens
/// cannot collide if a user starts both flows in two browser tabs.
/// </summary>
public static class AuthGithubConnectEndpoints
{
    public const string ConnectStateCookie = "lintty_oauth_connect_state";
    private const string ConnectCookiePath = "/api/auth/github/connect";

    /// <summary>
    /// URL the user goes to on github.com to fully revoke any OAuth grant
    /// they've given to this Lintty installation. Surfaced in the DELETE
    /// response so the privacy console can render "Revoked locally — to
    /// fully revoke on GitHub, click here".
    /// </summary>
    public const string UpstreamRevokeUrl = "https://github.com/settings/applications";

    /// <summary>
    /// Required scopes for the connect flow. The user can deselect scopes on
    /// GitHub's authorize prompt, so the callback validates the granted
    /// scopes contain ALL of these and rejects with <c>insufficient_scopes</c>
    /// otherwise (Apêndice E §E.4 / §E.5).
    /// </summary>
    private static readonly string[] RequiredScopes = { "repo", "read:org" };

    public static void MapAuthGithubConnect(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth/github/connect").WithTags("AuthGithubConnect");

        group.MapGet("/start", ConnectStart)
            .WithName("GitHubConnectStart")
            .WithSummary("Redirect to GitHub authorize with elevated scopes (repo + read:org)")
            .Produces(StatusCodes.Status302Found)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/callback", ConnectCallback)
            .WithName("GitHubConnectCallback")
            .WithSummary("Exchange code, persist encrypted token, redirect to dashboard")
            .Produces(StatusCodes.Status302Found)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", ConnectStatus)
            .WithName("GitHubConnectStatus")
            .WithSummary("Privacy console: is GitHub currently connected, and with what scopes?")
            .Produces<ConnectStatusResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapDelete("/", ConnectDelete)
            .WithName("GitHubConnectDelete")
            .WithSummary("Soft-revoke locally; also returns upstream URL for full revocation on github.com")
            .Produces<ConnectDeleteResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);
    }

    // ── GET /api/auth/github/connect/start ─────────────────────────────────
    private static IResult ConnectStart(
        HttpContext ctx,
        IOptions<GitHubOAuthOptions> opts)
    {
        if (!TryGetUserId(ctx, out _)) return Unauthorized();

        var o = opts.Value;
        if (string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.ClientSecret))
            return Results.Json(new ErrorResponse
            {
                Error = "github_oauth_not_configured",
                Message = "GitHub Connect is not enabled on this server.",
            }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var redirectUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/auth/github/connect/callback";
        var state = Guid.NewGuid().ToString("N");

        // Distinct cookie name + path from the login flow so a user with both
        // tabs open doesn't have the two CSRF tokens stomp on each other.
        ctx.Response.Cookies.Append(ConnectStateCookie, state, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = ConnectCookiePath,
        });

        // scope = "repo read:org" — space-separated per RFC 6749 §3.3 and
        // GitHub docs. Uri.EscapeDataString turns the space into %20 and
        // the colon in read:org into %3A.
        var url = $"https://github.com/login/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(o.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(string.Join(' ', RequiredScopes))}" +
            $"&state={Uri.EscapeDataString(state)}";
        return Results.Redirect(url);
    }

    // ── GET /api/auth/github/connect/callback ──────────────────────────────
    private static async Task<IResult> ConnectCallback(
        HttpContext ctx,
        IOptions<GitHubOAuthOptions> opts,
        IGitHubOAuthClient gh,
        IGitHubUserTokenStore tokenStore,
        LinttyDbContext db,
        ILoggerFactory logFactory,
        CancellationToken ct)
    {
        var log = logFactory.CreateLogger("Auth.GitHubConnect");

        if (!TryGetUserId(ctx, out var userId)) return Unauthorized();

        var o = opts.Value;
        if (string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.ClientSecret))
            return Results.Json(new ErrorResponse
            {
                Error = "github_oauth_not_configured",
                Message = "GitHub Connect is not enabled on this server.",
            }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var code = ctx.Request.Query["code"].ToString();
        var state = ctx.Request.Query["state"].ToString();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return BadRequest("invalid_oauth_callback", "Missing code or state.");

        var expectedState = ctx.Request.Cookies[ConnectStateCookie];
        // Always clear the cookie — even on mismatch, so a stale state cannot
        // be replayed.
        ctx.Response.Cookies.Delete(ConnectStateCookie, new CookieOptions { Path = ConnectCookiePath });
        if (string.IsNullOrEmpty(expectedState) || !string.Equals(expectedState, state, StringComparison.Ordinal))
            return BadRequest("invalid_oauth_state", "State token mismatch.");

        // Exchange code → token + scopes.
        GitHubTokenGrant grant;
        GitHubUserProfile profile;
        try
        {
            var redirectUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/auth/github/connect/callback";
            grant = await gh.ExchangeCodeForTokenWithScopesAsync(code, redirectUri, ct).ConfigureAwait(false);
            profile = await gh.GetUserAsync(grant.AccessToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "GitHub connect exchange failed for user_id={UserId}", userId);
            return BadRequest("github_oauth_exchange_failed", "GitHub did not return a usable identity.");
        }

        // Validate scopes — GitHub lets the user deselect them on the
        // authorize prompt, so the granted set may be a subset of what we
        // asked for. Reject early so we never persist an under-privileged
        // token that would fail later at scan time.
        if (!HasAllRequiredScopes(grant.Scopes))
        {
            log.LogWarning(
                "GitHub connect rejected: insufficient scopes user_id={UserId} granted=[{Granted}]",
                userId, string.Join(",", grant.Scopes));
            return BadRequest("insufficient_scopes",
                "GitHub returned a token without the required permissions; please retry and accept all requested scopes.");
        }

        // Identity reconciliation. The user is logged in via a Lintty cookie;
        // they may or may not already have an external_logins row:
        //  - If absent (signed up via email/password): create the link.
        //  - If present and provider_user_id matches: keep going.
        //  - If present and provider_user_id MISMATCHES: refuse — the user is
        //    granting OAuth from a different GitHub account than the one
        //    already linked. Persisting that would silently let two GitHub
        //    identities share the same Lintty user (Apêndice E §E.7
        //    separation-of-identity invariant).
        var existingLink = await db.ExternalLogins
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Provider == "github", ct)
            .ConfigureAwait(false);
        if (existingLink is null)
        {
            db.ExternalLogins.Add(new ExternalLogin
            {
                UserId = userId,
                Provider = "github",
                ProviderUserId = profile.ProviderUserId,
                Username = profile.Login,
                LinkedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else if (!string.Equals(existingLink.ProviderUserId, profile.ProviderUserId, StringComparison.Ordinal))
        {
            log.LogWarning(
                "GitHub connect identity mismatch: user_id={UserId} expected_provider_user_id={Expected} got_provider_user_id={Got}",
                userId, existingLink.ProviderUserId, profile.ProviderUserId);
            return BadRequest("github_identity_mismatch",
                "Você concedeu acesso com uma conta GitHub diferente da já vinculada. Faça logout no GitHub e tente novamente.");
        }
        else if (existingLink.Username != profile.Login)
        {
            // Cosmetic refresh — the user renamed at GitHub since linking.
            existingLink.Username = profile.Login;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Persist the encrypted token. UPSERT semantics live in the store.
        await tokenStore.SaveAsync(userId, grant.AccessToken, grant.Scopes, ct).ConfigureAwait(false);
        log.LogInformation("GitHub connect: user_id={UserId} login={Login}", userId, profile.Login);

        // Redirect back to the dashboard. Mirrors the login flow's target so
        // the front-end has a single entry point.
        return Results.Redirect("/dashboard.html");
    }

    // ── GET /api/auth/github/connect ───────────────────────────────────────
    private static async Task<IResult> ConnectStatus(
        HttpContext ctx,
        IGitHubUserTokenStore tokenStore,
        CancellationToken ct)
    {
        if (!TryGetUserId(ctx, out var userId)) return Unauthorized();

        var snapshot = await tokenStore.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (snapshot is null || snapshot.RevokedAt is not null)
            return Results.Json(new ConnectStatusResponse { Connected = false });

        return Results.Json(new ConnectStatusResponse
        {
            Connected = true,
            Scopes = snapshot.Scopes,
            GrantedAt = DateTime.SpecifyKind(snapshot.GrantedAt, DateTimeKind.Utc)
                .ToString("o", CultureInfo.InvariantCulture),
            LastUsedAt = snapshot.LastUsedAt is DateTime lu
                ? DateTime.SpecifyKind(lu, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture)
                : null,
        });
    }

    // ── DELETE /api/auth/github/connect ────────────────────────────────────
    private static async Task<IResult> ConnectDelete(
        HttpContext ctx,
        IGitHubUserTokenStore tokenStore,
        CancellationToken ct)
    {
        if (!TryGetUserId(ctx, out var userId)) return Unauthorized();

        var revoked = await tokenStore.RevokeAsync(userId, ct).ConfigureAwait(false);
        return Results.Json(new ConnectDeleteResponse
        {
            Revoked = revoked,
            AlreadyRevoked = !revoked,
            UpstreamRevokeUrl = UpstreamRevokeUrl,
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool TryGetUserId(HttpContext ctx, out long userId)
    {
        userId = 0;
        if (ctx.User.Identity?.IsAuthenticated != true) return false;
        var idClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(idClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out userId);
    }

    private static bool HasAllRequiredScopes(string[] granted)
    {
        if (granted is null) return false;
        var grantedSet = new HashSet<string>(granted, StringComparer.Ordinal);
        foreach (var required in RequiredScopes)
        {
            if (!grantedSet.Contains(required)) return false;
        }
        return true;
    }

    private static IResult Unauthorized()
        => Results.Json(new ErrorResponse { Error = "unauthorized", Message = "Authentication required." },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult BadRequest(string code, string message)
        => Results.Json(new ErrorResponse { Error = code, Message = message },
            statusCode: StatusCodes.Status400BadRequest);
}

// ── DTOs ───────────────────────────────────────────────────────────────────

/// <summary>
/// Response for <c>GET /api/auth/github/connect</c>. Never carries the token
/// — the privacy console only needs to render "connected/disconnected" plus
/// metadata.
/// </summary>
public sealed class ConnectStatusResponse
{
    [JsonPropertyName("connected")] public bool Connected { get; set; }
    [JsonPropertyName("scopes")] public string[]? Scopes { get; set; }
    [JsonPropertyName("grantedAt")] public string? GrantedAt { get; set; }
    [JsonPropertyName("lastUsedAt")] public string? LastUsedAt { get; set; }
}

/// <summary>
/// Response for <c>DELETE /api/auth/github/connect</c>. Idempotent — a
/// second call with no active token returns <c>revoked=false,
/// alreadyRevoked=true</c> rather than 404, so the front-end can blindly
/// hit the endpoint on logout/cleanup paths.
/// </summary>
public sealed class ConnectDeleteResponse
{
    [JsonPropertyName("revoked")] public bool Revoked { get; set; }
    [JsonPropertyName("alreadyRevoked")] public bool AlreadyRevoked { get; set; }
    [JsonPropertyName("upstreamRevokeUrl")] public string UpstreamRevokeUrl { get; set; } = string.Empty;
}
