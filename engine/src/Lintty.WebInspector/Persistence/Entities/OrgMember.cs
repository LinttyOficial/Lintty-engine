using System;

namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// Membership row linking a <see cref="User"/> to an <see cref="Org"/> with a
/// role. Unique on <c>(org_id, user_id)</c>.
///
/// Role values per ADR 0007 §3.2: <c>owner</c>, <c>admin</c>, <c>member</c>.
/// Persisted as a free-form string (lowercase) — enum semantics are enforced
/// at the application layer (see <see cref="OrgRole"/>) so a future schema
/// change (adding <c>billing</c>, etc.) doesn't require a migration.
/// </summary>
public sealed class OrgMember
{
    public long Id { get; set; }

    public long OrgId { get; set; }
    public Org? Org { get; set; }

    public long UserId { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// One of <see cref="OrgRole.Owner"/>, <see cref="OrgRole.Admin"/>,
    /// <see cref="OrgRole.Member"/>. Validated at write time;
    /// reads tolerate unknown strings (logged + treated as <c>member</c>).
    /// </summary>
    public string Role { get; set; } = OrgRole.Member;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Allowed values for <see cref="OrgMember.Role"/>. String constants instead
/// of an enum so the values stored in Postgres are stable and readable in
/// SQL queries (<c>SELECT * FROM org_members WHERE role = 'owner'</c>).
/// </summary>
public static class OrgRole
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";

    public static bool IsValid(string? role)
        => role is Owner or Admin or Member;
}
