using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Repos;

/// <summary>
/// Service surface for the org-bound repo CRUD. ADR 0007 §3.2 (manual add) +
/// Apêndice E §E.8 (manual vs Org-import compat). Sprint 3 / PR 3 ships only
/// the manual-add path; the Org-import sibling (<c>AddFromGitHubOrgAsync</c>)
/// arrives in PR 7 alongside the OAuth user-token elevation flow.
///
/// Every method takes the active <c>orgId</c> explicitly. Callers (endpoints)
/// are responsible for resolving the tenant via <see cref="Auth.ITenantContext"/>
/// + first-org fallback. The service does not read <c>HttpContext.Items</c>.
/// </summary>
public interface IRepoService
{
    /// <summary>
    /// Manual add: validates + canonicalises the URL, hits GitHub for a public
    /// existence check, persists the row. Idempotent — re-posting the same
    /// canonical URL within the same active org returns the existing row
    /// (<see cref="RepoOperationOutcome.AlreadyExists"/>).
    /// </summary>
    /// <param name="orgId">Tenant the repo will be registered under.</param>
    /// <param name="userId">User who is adding the repo. Persisted as
    /// <c>added_by_user_id</c>; in PR 6 this is the GitHub-token owner used
    /// for private clone (Apêndice E §E.9). For manual add it is just an
    /// audit anchor.</param>
    /// <param name="githubUrl">Raw URL the user pasted. The service runs it
    /// through <see cref="Validation.UrlValidator"/> + lowercases the host +
    /// strips any trailing slash before storage.</param>
    /// <param name="ct">Cancellation propagated from the request pipeline.</param>
    Task<AddRepoResult> AddManualAsync(long orgId, long userId, string? githubUrl, CancellationToken ct);

    /// <summary>
    /// Lists active (not soft-deleted) repos in the org, newest first.
    /// </summary>
    Task<IReadOnlyList<RepoSummary>> ListAsync(long orgId, CancellationToken ct);

    /// <summary>
    /// Returns the active repo if it belongs to <paramref name="orgId"/>.
    /// Cross-tenant or soft-deleted rows surface as <c>null</c> (the endpoint
    /// turns that into a 404 per §3.5).
    /// </summary>
    Task<RepoSummary?> GetAsync(long orgId, long repoId, CancellationToken ct);

    /// <summary>
    /// Soft-deletes the repo (sets <c>deleted_at</c>). Does NOT cascade to
    /// scans — historic laudos remain queryable per §E.8 / §3.2. Returns
    /// <c>false</c> if the repo doesn't exist, is already soft-deleted, or
    /// belongs to a different org.
    /// </summary>
    Task<bool> SoftDeleteAsync(long orgId, long repoId, CancellationToken ct);
}

/// <summary>
/// Outcome of <see cref="IRepoService.AddManualAsync"/>. The endpoint maps
/// <see cref="ErrorCode"/> to an HTTP status; <see cref="Outcome"/> drives
/// the 201 vs 200 (idempotent) decision.
/// </summary>
public sealed record AddRepoResult(
    RepoOperationOutcome Outcome,
    RepoSummary? Repo,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static AddRepoResult Created(RepoSummary repo)
        => new(RepoOperationOutcome.Created, repo, null, null);

    public static AddRepoResult AlreadyExists(RepoSummary repo)
        => new(RepoOperationOutcome.AlreadyExists, repo, null, null);

    public static AddRepoResult Error(string code, string message)
        => new(RepoOperationOutcome.Error, null, code, message);
}

public enum RepoOperationOutcome
{
    /// <summary>New row inserted — endpoint emits 201.</summary>
    Created = 0,

    /// <summary>Row already existed for the same (org, canonical url) — endpoint
    /// emits 200 with the existing row. Idempotent re-submit.</summary>
    AlreadyExists = 1,

    /// <summary>Validation or upstream failure. <c>ErrorCode</c> + <c>ErrorMessage</c>
    /// populated; endpoint emits 4xx.</summary>
    Error = 2,
}

/// <summary>
/// Wire-shape of a repo row exposed to the API. Mirrors the columns of
/// <c>repos</c> minus the soft-delete timestamp (only active rows ever leak
/// out) and the four GitHub-import fields (always null for manual adds in
/// PR 3 — PR 7 will populate them on the org-import path).
/// </summary>
public sealed record RepoSummary(
    long Id,
    long OrgId,
    string GithubUrl,
    bool IsPrivate,
    System.DateTime CreatedAt,
    long AddedByUserId,
    string AddedByDisplayName);
