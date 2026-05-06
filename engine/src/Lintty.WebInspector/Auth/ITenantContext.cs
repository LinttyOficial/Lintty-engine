namespace Lintty.WebInspector.Auth;

/// <summary>
/// Per-request tenant scope. Populated by <see cref="TenantContextMiddleware"/>
/// after authentication. Anonymous requests keep <see cref="OrgId"/> = null;
/// authenticated requests get the org id of the user's "current" org (first
/// membership for V1.0; org-switcher API arrives V1.1).
///
/// Repositories that filter by org should always go through this — never read
/// <c>HttpContext.Items</c> directly outside of tests.
/// </summary>
public interface ITenantContext
{
    long? OrgId { get; }
    long? UserId { get; }
    bool IsAuthenticated => UserId.HasValue;
}
