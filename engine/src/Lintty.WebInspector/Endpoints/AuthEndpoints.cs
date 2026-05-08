using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Configuration;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;

namespace Lintty.WebInspector.Endpoints;

/// <summary>
/// Wiring for <c>/api/auth/*</c>. ADR 0007 Sprint 2.
///
/// Routes:
/// <list type="bullet">
///   <item><description><c>POST /api/auth/signup</c> — create user + default org + owner membership.</description></item>
///   <item><description><c>POST /api/auth/login</c> — password sign-in.</description></item>
///   <item><description><c>POST /api/auth/logout</c> — clear cookie.</description></item>
///   <item><description><c>GET  /api/auth/github/start</c> — redirect to GitHub.</description></item>
///   <item><description><c>GET  /api/auth/github/callback</c> — exchange code, upsert user + external_login + default org.</description></item>
///   <item><description><c>GET  /api/auth/me</c> — current user + memberships.</description></item>
/// </list>
///
/// **V0 anonymous flow is unaffected.** No middleware here forces auth on
/// <c>/api/jobs/*</c>; tenant tagging happens via <see cref="TenantContextMiddleware"/>
/// only when a cookie is present.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/signup", Signup)
            .WithName("Signup")
            .WithSummary("Create a user, a default org, and an owner membership")
            .Produces<MeResponse>(StatusCodes.Status201Created)
            .Produces<ValidationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Password sign-in")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .WithSummary("Clear the auth cookie")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", Me)
            .WithName("Me")
            .WithSummary("Current user + org memberships")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/github/start", GitHubStart)
            .WithName("GitHubStart")
            .WithSummary("Redirect to GitHub OAuth authorize endpoint")
            .Produces(StatusCodes.Status302Found)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/github/callback", GitHubCallback)
            .WithName("GitHubCallback")
            .WithSummary("OAuth callback — exchange code, upsert user, set cookie")
            .Produces(StatusCodes.Status302Found)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);
    }

    // ── Signup ─────────────────────────────────────────────────────────────
    private static async Task<IResult> Signup(
        HttpContext ctx,
        SignupRequest? body,
        UserManager<User> userMgr,
        SignInManager<User> signInMgr,
        LinttyDbContext db,
        ILoggerFactory logFactory,
        CancellationToken ct)
    {
        var log = logFactory.CreateLogger("Auth.Signup");
        var errors = ValidateSignup(body);
        if (errors.Count > 0)
            return Results.Json(new ValidationErrorResponse { Error = "validation_failed", Errors = errors },
                statusCode: StatusCodes.Status400BadRequest);

        var b = body!;

        if (await userMgr.FindByEmailAsync(b.Email!).ConfigureAwait(false) is not null)
            return Conflict("email_taken", "An account with that email already exists.");

        var user = new User
        {
            UserName = b.Email,
            Email = b.Email,
            DisplayName = string.IsNullOrWhiteSpace(b.DisplayName) ? LocalPart(b.Email!) : b.DisplayName!,
            CreatedAt = DateTime.UtcNow,
        };
        var createResult = await userMgr.CreateAsync(user, b.Password!).ConfigureAwait(false);
        if (!createResult.Succeeded)
        {
            return Results.Json(new ValidationErrorResponse
            {
                Error = "validation_failed",
                Errors = IdentityErrorsToFieldMap(createResult),
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        var slug = await EnsureUniqueSlugAsync(db, SlugGenerator.Slugify(b.OrgName), ct).ConfigureAwait(false);
        var org = new Org
        {
            Slug = slug,
            Name = b.OrgName!,
            OwnerId = user.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Orgs.Add(org);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        db.OrgMembers.Add(new OrgMember
        {
            OrgId = org.Id,
            UserId = user.Id,
            Role = OrgRole.Owner,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        log.LogInformation("Signup: user_id={UserId} email={Email} org_id={OrgId} slug={Slug}",
            user.Id, user.Email, org.Id, org.Slug);

        await signInMgr.SignInAsync(user, isPersistent: true).ConfigureAwait(false);

        var resp = await BuildMeResponseAsync(db, user.Id, ct).ConfigureAwait(false);
        return Results.Json(resp, statusCode: StatusCodes.Status201Created);
    }

    // ── Login ──────────────────────────────────────────────────────────────
    private static async Task<IResult> Login(
        LoginRequest? body,
        UserManager<User> userMgr,
        SignInManager<User> signInMgr,
        LinttyDbContext db,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
            return Unauthorized();

        var user = await userMgr.FindByEmailAsync(body.Email).ConfigureAwait(false);
        if (user is null) return Unauthorized();

        var result = await signInMgr.CheckPasswordSignInAsync(user, body.Password, lockoutOnFailure: false)
            .ConfigureAwait(false);
        if (!result.Succeeded) return Unauthorized();

        await signInMgr.SignInAsync(user, isPersistent: true).ConfigureAwait(false);

        var resp = await BuildMeResponseAsync(db, user.Id, ct).ConfigureAwait(false);
        return Results.Json(resp, statusCode: StatusCodes.Status200OK);
    }

    // ── Logout ─────────────────────────────────────────────────────────────
    private static async Task<IResult> Logout(SignInManager<User> signInMgr)
    {
        await signInMgr.SignOutAsync().ConfigureAwait(false);
        return Results.NoContent();
    }

    // ── /me ────────────────────────────────────────────────────────────────
    private static async Task<IResult> Me(
        HttpContext ctx,
        LinttyDbContext db,
        CancellationToken ct)
    {
        if (ctx.User.Identity?.IsAuthenticated != true) return Unauthorized();

        var idClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return Unauthorized();

        var resp = await BuildMeResponseAsync(db, userId, ct).ConfigureAwait(false);
        return resp is null ? Unauthorized() : Results.Json(resp);
    }

    // ── GitHub OAuth start ─────────────────────────────────────────────────
    private static IResult GitHubStart(
        HttpContext ctx,
        IOptions<GitHubOAuthOptions> opts)
    {
        var o = opts.Value;
        if (string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.ClientSecret))
            return Results.Json(new ErrorResponse
            {
                Error = "github_oauth_not_configured",
                Message = "GitHub login is not enabled on this server. Use email + password instead.",
            }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var redirectUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/auth/github/callback";
        var state = Guid.NewGuid().ToString("N");
        // Persist state in a short-lived signed cookie so the callback can verify CSRF.
        ctx.Response.Cookies.Append("lintty_oauth_state", state, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/api/auth/github",
        });

        var url = $"https://github.com/login/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(o.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString("read:user user:email")}" +
            $"&state={Uri.EscapeDataString(state)}";
        return Results.Redirect(url);
    }

    // ── GitHub OAuth callback ──────────────────────────────────────────────
    private static async Task<IResult> GitHubCallback(
        HttpContext ctx,
        IOptions<GitHubOAuthOptions> opts,
        IGitHubOAuthClient gh,
        UserManager<User> userMgr,
        SignInManager<User> signInMgr,
        LinttyDbContext db,
        ILoggerFactory logFactory,
        CancellationToken ct)
    {
        var log = logFactory.CreateLogger("Auth.GitHubCallback");
        var o = opts.Value;
        if (string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.ClientSecret))
            return Results.Json(new ErrorResponse
            {
                Error = "github_oauth_not_configured",
                Message = "GitHub login is not enabled on this server.",
            }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var code = ctx.Request.Query["code"].ToString();
        var state = ctx.Request.Query["state"].ToString();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return BadRequest("invalid_oauth_callback", "Missing code or state.");

        var expectedState = ctx.Request.Cookies["lintty_oauth_state"];
        if (string.IsNullOrEmpty(expectedState) || !string.Equals(expectedState, state, StringComparison.Ordinal))
            return BadRequest("invalid_oauth_state", "State token mismatch.");
        ctx.Response.Cookies.Delete("lintty_oauth_state", new CookieOptions { Path = "/api/auth/github" });

        string accessToken;
        GitHubUserProfile profile;
        try
        {
            var redirectUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/auth/github/callback";
            accessToken = await gh.ExchangeCodeForTokenAsync(code, redirectUri, ct).ConfigureAwait(false);
            profile = await gh.GetUserAsync(accessToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "GitHub OAuth exchange failed");
            return BadRequest("github_oauth_exchange_failed", "GitHub did not return a usable identity.");
        }

        // Existing link?
        var existingLink = await db.ExternalLogins
            .FirstOrDefaultAsync(e => e.Provider == "github" && e.ProviderUserId == profile.ProviderUserId, ct)
            .ConfigureAwait(false);

        User user;
        if (existingLink is not null)
        {
            user = await userMgr.FindByIdAsync(existingLink.UserId.ToString(CultureInfo.InvariantCulture))
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Dangling external_login row.");
            // Refresh username if it drifted (cheap; lets the UI render @login correctly).
            if (existingLink.Username != profile.Login)
            {
                existingLink.Username = profile.Login;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        else
        {
            // Reuse a user with the same email if present (manual + OAuth merge).
            user = await userMgr.FindByEmailAsync(profile.Email).ConfigureAwait(false)
                ?? await CreateUserFromGitHubAsync(userMgr, profile).ConfigureAwait(false);

            db.ExternalLogins.Add(new ExternalLogin
            {
                UserId = user.Id,
                Provider = "github",
                ProviderUserId = profile.ProviderUserId,
                Username = profile.Login,
                LinkedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // First login → ensure the user has at least one org. Default slug
            // is the GitHub login (collisions resolved by appending -2, ...).
            await EnsureDefaultOrgAsync(db, user, profile.Login, ct).ConfigureAwait(false);
        }

        await signInMgr.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        log.LogInformation("GitHub OAuth login: user_id={UserId} login={Login}", user.Id, profile.Login);

        return Results.Redirect("/dashboard");
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static async Task<User> CreateUserFromGitHubAsync(UserManager<User> userMgr, GitHubUserProfile profile)
    {
        var user = new User
        {
            UserName = profile.Email,
            Email = profile.Email,
            EmailConfirmed = true, // GitHub already verified it
            DisplayName = string.IsNullOrWhiteSpace(profile.Name) ? profile.Login : profile.Name!,
            CreatedAt = DateTime.UtcNow,
        };
        var result = await userMgr.CreateAsync(user).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create user from GitHub identity: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        return user;
    }

    private static async Task EnsureDefaultOrgAsync(LinttyDbContext db, User user, string githubLogin, CancellationToken ct)
    {
        var hasOrg = await db.OrgMembers.AnyAsync(m => m.UserId == user.Id, ct).ConfigureAwait(false);
        if (hasOrg) return;

        var slug = await EnsureUniqueSlugAsync(db, SlugGenerator.Slugify(githubLogin), ct).ConfigureAwait(false);
        var org = new Org
        {
            Slug = slug,
            Name = githubLogin,
            OwnerId = user.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Orgs.Add(org);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        db.OrgMembers.Add(new OrgMember
        {
            OrgId = org.Id,
            UserId = user.Id,
            Role = OrgRole.Owner,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string> EnsureUniqueSlugAsync(LinttyDbContext db, string seed, CancellationToken ct)
    {
        var slug = seed;
        var n = 2;
        while (await db.Orgs.AnyAsync(o => o.Slug == slug, ct).ConfigureAwait(false))
        {
            slug = $"{seed}-{n.ToString(CultureInfo.InvariantCulture)}";
            n++;
            if (n > 1000)
                throw new InvalidOperationException("Could not allocate a unique org slug after 1000 attempts.");
        }
        return slug;
    }

    private static async Task<MeResponse?> BuildMeResponseAsync(LinttyDbContext db, long userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null) return null;

        var memberships = await db.OrgMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Id)
            .Join(db.Orgs.AsNoTracking(), m => m.OrgId, o => o.Id, (m, o) => new MembershipDto
            {
                OrgId = o.Id,
                Slug = o.Slug,
                Name = o.Name,
                Role = m.Role,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var current = memberships.Count == 0 ? null : memberships[0];

        return new MeResponse
        {
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
            },
            CurrentOrg = current,
            Memberships = memberships,
        };
    }

    private static Dictionary<string, List<string>> ValidateSignup(SignupRequest? body)
    {
        var errors = new Dictionary<string, List<string>>();
        if (body is null)
        {
            errors["body"] = new List<string> { "Request body is required." };
            return errors;
        }
        if (string.IsNullOrWhiteSpace(body.Email))
            errors["email"] = new List<string> { "Email is required." };
        else if (!body.Email.Contains('@', StringComparison.Ordinal))
            errors["email"] = new List<string> { "Email is not valid." };
        if (string.IsNullOrWhiteSpace(body.Password))
            errors["password"] = new List<string> { "Password is required." };
        if (string.IsNullOrWhiteSpace(body.OrgName))
            errors["orgName"] = new List<string> { "Organization name is required." };
        return errors;
    }

    private static Dictionary<string, List<string>> IdentityErrorsToFieldMap(IdentityResult result)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var err in result.Errors)
        {
            var field = err.Code switch
            {
                "PasswordTooShort" or "PasswordRequiresDigit" or "PasswordRequiresLower"
                    or "PasswordRequiresUpper" or "PasswordRequiresNonAlphanumeric"
                    or "PasswordRequiresUniqueChars" => "password",
                "InvalidEmail" or "DuplicateEmail" or "DuplicateUserName" => "email",
                _ => "form",
            };
            if (!map.TryGetValue(field, out var list))
            {
                list = new List<string>();
                map[field] = list;
            }
            list.Add(err.Description);
        }
        return map;
    }

    private static string LocalPart(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? email : email[..at];
    }

    private static IResult Conflict(string code, string message)
        => Results.Json(new ErrorResponse { Error = code, Message = message }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Unauthorized()
        => Results.Json(new ErrorResponse { Error = "unauthorized", Message = "Authentication required." },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult BadRequest(string code, string message)
        => Results.Json(new ErrorResponse { Error = code, Message = message }, statusCode: StatusCodes.Status400BadRequest);
}

// ── DTOs ───────────────────────────────────────────────────────────────────

public sealed class SignupRequest
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("orgName")] public string? OrgName { get; set; }
}

public sealed class LoginRequest
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
}

public sealed class MeResponse
{
    [JsonPropertyName("user")] public UserDto User { get; set; } = new();
    [JsonPropertyName("currentOrg")] public MembershipDto? CurrentOrg { get; set; }
    [JsonPropertyName("memberships")] public List<MembershipDto> Memberships { get; set; } = new();
}

public sealed class UserDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
}

public sealed class MembershipDto
{
    [JsonPropertyName("orgId")] public long OrgId { get; set; }
    [JsonPropertyName("slug")] public string Slug { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
}

public sealed class ValidationErrorResponse
{
    [JsonPropertyName("error")] public string Error { get; set; } = string.Empty;
    [JsonPropertyName("errors")] public Dictionary<string, List<string>> Errors { get; set; } = new();
}
