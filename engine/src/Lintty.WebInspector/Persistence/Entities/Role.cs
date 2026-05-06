using Microsoft.AspNetCore.Identity;

namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// Application role. Identity owns the contract (<c>name</c>,
/// <c>normalized_name</c>, <c>concurrency_stamp</c>); product fields go on
/// derived classes only when needed. PK is <c>long</c> to match the
/// <see cref="User"/> bigserial convention.
///
/// Role usage in V1.0 is minimal — org-level roles
/// (<c>owner</c>/<c>admin</c>/<c>member</c>) live on <see cref="OrgMember"/>
/// as a string column, not as Identity roles. This entity exists so we have
/// a place for global roles (e.g. <c>support</c>) when we need them in V1.1+.
/// </summary>
public sealed class Role : IdentityRole<long>
{
}
