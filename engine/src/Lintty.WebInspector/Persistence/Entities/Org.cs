using System;
using System.Collections.Generic;

namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// Tenant boundary. ADR 0007 §3.2 — every domain row that belongs to a logged-in
/// user belongs to an org. Anonymous scans (the V0 <c>/inspect</c> flow) keep
/// <c>org_id IS NULL</c> on <see cref="Lintty.WebInspector.Jobs.Job"/>.
///
/// Slug is the URL-safe, lowercase, unique handle used in routes
/// (<c>/orgs/{slug}/...</c> in Sprint 3). Display name is the human-readable
/// label.
/// </summary>
public sealed class Org
{
    public long Id { get; set; }

    /// <summary>URL slug, lowercase, unique. Generated from name on signup.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long OwnerId { get; set; }
    public User? Owner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrgMember> Members { get; set; } = new List<OrgMember>();
}
