using System;

namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// A GitHub repository registered to a Lintty <see cref="Org"/>. Sprint 3 of
/// ADR 0007 — only the schema lands in PR 1; the <c>RepoService</c> and the
/// <c>POST /api/repos</c> endpoint follow in PR 3.
///
/// Two ways a repo gets here:
/// <list type="number">
/// <item><b>Manual add</b> (<c>POST /api/repos { github_url }</c>) — the user
/// types a URL. Only <see cref="GithubUrl"/> is populated. The four GitHub
/// metadata fields (<see cref="GithubRepoId"/>, <see cref="GithubOrgLogin"/>,
/// <see cref="DefaultBranch"/>, <see cref="IsPrivate"/>) stay <c>null</c>/false.
/// Per Apêndice E §E.8, requesting a private scan via this path fails fast.
/// </item>
/// <item><b>Org connect import</b> (<c>POST /api/orgs/{slug}/repos/import</c>,
/// PR 3) — the user picks a repo from the GitHub org listing returned by
/// <c>IGitHubOrgsClient</c>. All four metadata fields are populated.</item>
/// </list>
///
/// **Soft delete** via <see cref="DeletedAt"/>: V1.0 keeps the row so historic
/// scans (which FK to <c>repos</c>) survive the deletion. The partial unique
/// index <c>uq_repos_org_url_active</c> filters <c>deleted_at IS NULL</c> so
/// re-adding a deleted repo is allowed.
///
/// **Restrict on <see cref="AddedByUserId"/>**: deleting a user who added a
/// repo is blocked. Decision per Apêndice E #5 — preserves the
/// separation-of-identity invariant in §E.7 (token clones are issued under
/// <c>added_by_user_id</c>; nullifying that user would orphan private clones).
/// </summary>
public sealed class Repo
{
    public long Id { get; set; }

    public long OrgId { get; set; }
    public Org? Org { get; set; }

    /// <summary>User who added this repo. Used as the GitHub-token owner for
    /// private clones — see Apêndice E §E.9. <b>ON DELETE RESTRICT</b>: cannot
    /// drop a user that owns repo entries; transfer them first.</summary>
    public long AddedByUserId { get; set; }
    public User? AddedByUser { get; set; }

    /// <summary>Normalized GitHub URL (e.g. <c>https://github.com/owner/repo</c>,
    /// no <c>.git</c>, lowercased host). Source of truth for cloning.</summary>
    public string GithubUrl { get; set; } = string.Empty;

    /// <summary>Numeric GitHub repo id (from <c>GET /repos/{owner}/{repo}</c>).
    /// Populated only when the repo is imported via the Org connect flow.
    /// Manual adds leave this <c>null</c>.</summary>
    public long? GithubRepoId { get; set; }

    /// <summary>GitHub org/owner login (e.g. <c>"acme-corp"</c>). Populated
    /// only on Org connect import.</summary>
    public string? GithubOrgLogin { get; set; }

    /// <summary>Cached default branch name (e.g. <c>"main"</c>). Used when the
    /// scan request omits <c>ref</c>. Populated only on Org connect import.
    /// Manual adds rely on the engine's git default at clone time.</summary>
    public string? DefaultBranch { get; set; }

    /// <summary>True if the repo requires authenticated clone. The
    /// <c>GitCliClient</c> consults this before injecting an
    /// <c>x-access-token:</c> URL — see Apêndice E §E.9.</summary>
    public bool IsPrivate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete timestamp. <c>null</c> means active. Historic
    /// scans referencing this repo remain queryable.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// User-curated scan target paths (Sprint 3 PR S1). Each entry is a
    /// repo-relative POSIX path (forward slashes, no leading <c>./</c>) to a
    /// <c>.sln</c>, <c>.csproj</c>, or <c>lintty.yml</c>.
    /// <para>
    /// Semantics consumed by <see cref="Lintty.WebInspector.Jobs.JobWorker"/>:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>null</c> or empty → auto-detect (current
    ///         behaviour: <see cref="Lintty.Engine.Core.Workspace.TargetResolver"/>
    ///         picks the .sln, lintty.yml, or single .csproj).</description></item>
    ///   <item><description>Single <c>.sln</c> entry → analyze that solution.</description></item>
    ///   <item><description>Single <c>.csproj</c> entry → analyze that project.</description></item>
    ///   <item><description>Single <c>lintty.yml</c> entry → use the yml's
    ///         <c>projects:</c> list.</description></item>
    ///   <item><description>2+ <c>.csproj</c> entries → the worker writes a
    ///         throwaway <c>.lintty-runtime.yml</c> in the sandbox declaring
    ///         this list, then targets that yml.</description></item>
    /// </list>
    /// Validation (combinations + path-not-in-candidates rejection) happens at
    /// the PUT endpoint via <c>IRepoPreflightService.SetScanProjectsAsync</c>;
    /// the worker trusts the column blindly because the candidate set was
    /// validated before persistence.
    /// </summary>
    public string[]? ScanProjects { get; set; }
}
