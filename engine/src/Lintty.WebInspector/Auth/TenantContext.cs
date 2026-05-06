using Microsoft.AspNetCore.Http;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// Default <see cref="ITenantContext"/> backed by <c>HttpContext.Items</c>.
/// Resolves the org id and user id at construction time; subsequent property
/// reads are pure. Scoped per-request so concurrent requests never see each
/// other's tenant scope.
///
/// The keys (<see cref="OrgIdItemKey"/>, <see cref="UserIdItemKey"/>) are
/// written by <see cref="TenantContextMiddleware"/>. Anonymous requests have
/// neither key set, which surfaces here as <c>null</c>.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public const string OrgIdItemKey = "lintty.tenant.org_id";
    public const string UserIdItemKey = "lintty.tenant.user_id";

    public long? OrgId { get; }
    public long? UserId { get; }

    public TenantContext(IHttpContextAccessor accessor)
    {
        var ctx = accessor.HttpContext;
        if (ctx is null) return;

        if (ctx.Items.TryGetValue(OrgIdItemKey, out var orgObj) && orgObj is long org)
            OrgId = org;
        if (ctx.Items.TryGetValue(UserIdItemKey, out var userObj) && userObj is long user)
            UserId = user;
    }
}
