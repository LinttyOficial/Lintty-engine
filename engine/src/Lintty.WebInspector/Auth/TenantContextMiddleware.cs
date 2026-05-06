using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Lintty.WebInspector.Persistence;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// Resolves the current tenant (org) for the authenticated user and stashes
/// it on <see cref="HttpContext.Items"/> under <see cref="TenantContext.OrgIdItemKey"/>.
///
/// Order in <see cref="Program.ConfigurePipeline"/>:
/// <list type="number">
///   <item><description><c>UseAuthentication</c></description></item>
///   <item><description><c>UseAuthorization</c></description></item>
///   <item><description><c>UseMiddleware&lt;TenantContextMiddleware&gt;</c> (this)</description></item>
///   <item><description>endpoint mapping</description></item>
/// </list>
///
/// Resolution rules (ADR 0007 §3.5):
/// <list type="bullet">
///   <item><description>Anonymous request → no items set; <c>ITenantContext.OrgId</c> stays null. The V0 anonymous flow (<c>POST /api/jobs</c>) keeps working — it just doesn't tag the job to any org.</description></item>
///   <item><description>Authenticated, claim <c>org_id</c> present (set by login or org-switcher) → use it directly.</description></item>
///   <item><description>Authenticated, no claim → look up the user's first <c>org_members</c> row by id and use that org. First-login fallback so a fresh user always lands on their default org without a follow-up call.</description></item>
/// </list>
///
/// **No HTTP shortcut here.** This middleware never returns 401/403/404 — it
/// only populates context. Endpoints decide what to do with anonymity (either
/// allow it, like <c>/api/jobs</c>, or reject it, like the auth-required
/// dashboard endpoints in Sprint 3).
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var userIdStr = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (long.TryParse(userIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            {
                ctx.Items[TenantContext.UserIdItemKey] = userId;

                var orgId = await ResolveOrgIdAsync(ctx, userId).ConfigureAwait(false);
                if (orgId.HasValue)
                {
                    ctx.Items[TenantContext.OrgIdItemKey] = orgId.Value;
                }
                else
                {
                    _logger.LogWarning(
                        "Authenticated user {UserId} has no org membership; tenant scope is anonymous-equivalent.",
                        userId);
                }
            }
        }

        await _next(ctx).ConfigureAwait(false);
    }

    private static async Task<long?> ResolveOrgIdAsync(HttpContext ctx, long userId)
    {
        // Honor an explicit claim first (set on login or after switching orgs).
        var claim = ctx.User.FindFirst("org_id");
        if (claim is not null && long.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromClaim))
        {
            return fromClaim;
        }

        // Fallback: pick the user's earliest membership. This is deterministic
        // (smallest org_member.id wins) and stable across requests.
        var db = ctx.RequestServices.GetRequiredService<LinttyDbContext>();
        var membership = await db.OrgMembers
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Id)
            .Select(m => (long?)m.OrgId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return membership;
    }
}
