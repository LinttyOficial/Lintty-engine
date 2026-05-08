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

    /// <summary>
    /// Imports a repo from a GitHub org connection. ADR 0007 Apêndice E
    /// §E.8 — the org-import sibling of <see cref="AddManualAsync"/>.
    /// Distinct from <see cref="AddManualAsync"/> in three ways:
    /// <list type="bullet">
    ///   <item><description>Uses an authenticated GitHub call
    ///         (<see cref="Github.IGitHubOrgsClient.GetRepoMetadataAsync"/>)
    ///         so private repos are visible to the metadata fetch.</description></item>
    ///   <item><description>Persists all four GitHub-import fields
    ///         (<c>github_repo_id</c>, <c>github_org_login</c>,
    ///         <c>default_branch</c>, <c>is_private</c>) — including the
    ///         <c>is_private=true</c> flag the worker reads in
    ///         <c>JobWorker.RunScanAsync</c> to decide whether to inject
    ///         the user token at clone time (§E.9).</description></item>
    ///   <item><description>Idempotency keys on
    ///         <c>(org_id, github_repo_id)</c> instead of
    ///         <c>(org_id, github_url)</c> — a repo renamed at GitHub keeps
    ///         the same numeric id, so we deduplicate on the stable
    ///         identity rather than the cosmetic URL.</description></item>
    /// </list>
    /// </summary>
    /// <param name="orgId">Tenant the repo will be registered under.</param>
    /// <param name="userId">User who is importing the repo. Persisted as
    /// <c>added_by_user_id</c>; the worker reads this column to look up
    /// the OAuth token at clone time (Apêndice E §E.9).</param>
    /// <param name="accessToken">Decrypted user OAuth token used to
    /// authenticate the GitHub metadata fetch. The caller (endpoint) is
    /// responsible for resolving the token via
    /// <see cref="Auth.IGitHubUserTokenStore.GetActiveTokenAsync"/>; the
    /// service does not read the store.</param>
    /// <param name="githubOrgLogin">Org slug (e.g. <c>"acme"</c>). Caller
    /// validated that the user has the org in their <c>github_orgs</c>
    /// cache.</param>
    /// <param name="repoFullName">"owner/name" pair the GitHub listing
    /// returned. The owner part typically equals
    /// <paramref name="githubOrgLogin"/> but we accept the full pair
    /// verbatim from the frontend so a future user-account import (V1.1)
    /// re-uses this method without renaming.</param>
    /// <param name="ct">Cancellation propagated from the request pipeline.</param>
    Task<ImportRepoResult> ImportFromGithubOrgAsync(
        long orgId,
        long userId,
        string accessToken,
        string githubOrgLogin,
        string repoFullName,
        CancellationToken ct);
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
/// out). The four GitHub-import fields are populated on the org-import path
/// (PR 7) and stay <c>null</c>/<c>false</c> on manual adds (PR 3).
/// </summary>
public sealed record RepoSummary(
    long Id,
    long OrgId,
    string GithubUrl,
    bool IsPrivate,
    System.DateTime CreatedAt,
    long AddedByUserId,
    string AddedByDisplayName);

/// <summary>
/// Outcome of <see cref="IRepoService.ImportFromGithubOrgAsync"/>. Mirrors
/// <see cref="AddRepoResult"/> but with import-specific error codes
/// (<c>repo_not_found</c>, <c>insufficient_github_scopes</c>) so the
/// endpoint can return tailored 404/403 responses without case-folding
/// strings.
/// </summary>
public sealed record ImportRepoResult(
    ImportRepoOutcome Outcome,
    RepoSummary? Repo,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ImportRepoResult Created(RepoSummary repo)
        => new(ImportRepoOutcome.Created, repo, null, null);

    public static ImportRepoResult AlreadyExists(RepoSummary repo)
        => new(ImportRepoOutcome.AlreadyExists, repo, null, null);

    public static ImportRepoResult RepoNotFound()
        => new(ImportRepoOutcome.RepoNotFound, null, "repo_not_found",
            "GitHub returned 404 for that repository. It may have been deleted, renamed, or moved out of the org.");

    public static ImportRepoResult InsufficientScopes()
        => new(ImportRepoOutcome.InsufficientScopes, null, "insufficient_github_scopes",
            "Sua conexão com o GitHub não tem permissão para ler este repositório. Reconecte concedendo os escopos solicitados.");

    public static ImportRepoResult Error(string code, string message)
        => new(ImportRepoOutcome.Error, null, code, message);
}

public enum ImportRepoOutcome
{
    /// <summary>New row inserted — endpoint emits 201.</summary>
    Created = 0,

    /// <summary>Row already existed for the same (org, github_repo_id) — endpoint
    /// emits 200 with the existing row.</summary>
    AlreadyExists = 1,

    /// <summary>GitHub returned 404 for the repo — endpoint emits 404.</summary>
    RepoNotFound = 2,

    /// <summary>GitHub returned 401/403 (token revoked or scope dropped at the
    /// repo level) — endpoint emits 403.</summary>
    InsufficientScopes = 3,

    /// <summary>Validation failure (bad input). <c>ErrorCode</c> + <c>ErrorMessage</c>
    /// populated; endpoint emits 4xx.</summary>
    Error = 4,
}
