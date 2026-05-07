using System;

namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// A GitHub organization a <see cref="User"/> has authorized us to see during
/// the Connect GitHub flow. Apêndice E §E.7 — the binding is
/// <b>user-level, not Lintty-org-level</b>: each user has their own GitHub
/// token and their own visibility into GitHub orgs. Two members of the same
/// Lintty <see cref="Org"/> may see disjoint sets of GitHub orgs (or none).
///
/// PR 1 lands the schema only. The connect callback that upserts rows here,
/// and the <c>GET /api/github/orgs</c> read path, land in PR 6.
///
/// Cache TTL on <see cref="ConnectedAt"/>: §E.7 default is 1h — if the cached
/// row is stale, <c>IGitHubOrgsClient.ListOrgsAsync</c> refreshes from
/// <c>GET /user/orgs</c> and updates this row in place.
///
/// <c>UNIQUE (user_id, github_org_id)</c> — the same user cannot have two rows
/// for the same GitHub org. A re-connect updates the existing row (refreshes
/// avatar, bumps <see cref="ConnectedAt"/>) instead of inserting.
/// </summary>
public sealed class GithubOrg
{
    public long Id { get; set; }

    public long UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Numeric GitHub org id (from <c>GET /user/orgs</c> →
    /// <c>id</c>). Stable across rename — preferred over login for joins.</summary>
    public long GithubOrgId { get; set; }

    /// <summary>Org login slug (e.g. <c>"acme-corp"</c>). Cosmetic; may go
    /// stale if the org renames at GitHub. Re-fetched on cache refresh.</summary>
    public string GithubOrgLogin { get; set; } = string.Empty;

    /// <summary>Avatar URL from GitHub (org logo). Optional.</summary>
    public string? GithubOrgAvatarUrl { get; set; }

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
}
