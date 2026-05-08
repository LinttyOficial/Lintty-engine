using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lintty.WebInspector.Github;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector.Repos;

/// <summary>
/// EF-backed implementation of <see cref="IRepoService"/>. ADR 0007 §3.2 +
/// Apêndice E §E.8 — manual add path. PR 3 of Sprint 3.
///
/// **Canonicalisation** (single source of truth for the partial unique index
/// <c>uq_repos_org_url_active</c>): we lowercase the host, strip a trailing
/// <c>.git</c>, and remove any trailing slash. Without it, paste variations
/// (<c>HTTPS://Github.com/Owner/Repo/</c>) would slip past the unique check
/// and create duplicates that the dashboard cannot deduplicate after the fact.
/// <see cref="UrlValidator"/> already handles the <c>.git</c> strip and host
/// lowercase, so we just consume its <see cref="GitHubRepoCoordinates.NormalizedUrl"/>.
///
/// **Idempotency:** when a row already exists for <c>(org_id, canonical_url)</c>
/// with <c>deleted_at IS NULL</c>, we return it instead of erroring. The
/// dashboard supports double-clicks, navigation-back-then-resubmit, and
/// browser auto-resubmit on flaky networks; treating those as 409 would be
/// hostile UX.
/// </summary>
public sealed class RepoService : IRepoService
{
    private readonly LinttyDbContext _db;
    private readonly IGitHubMetadataClient _github;
    private readonly IGitHubOrgsClient _orgsClient;
    private readonly ILogger<RepoService> _log;

    public RepoService(
        LinttyDbContext db,
        IGitHubMetadataClient github,
        IGitHubOrgsClient orgsClient,
        ILogger<RepoService> log)
    {
        _db = db;
        _github = github;
        _orgsClient = orgsClient;
        _log = log;
    }

    public async Task<AddRepoResult> AddManualAsync(
        long orgId,
        long userId,
        string? githubUrl,
        CancellationToken ct)
    {
        var coords = UrlValidator.TryParse(githubUrl, out var parseError);
        if (coords is null)
        {
            return AddRepoResult.Error(
                "invalid_github_url",
                parseError ?? "github_url is invalid.");
        }

        // NormalizedUrl is "https://github.com/{owner}/{repo}" — host lowercase,
        // no .git suffix, no trailing slash. This is the canonical form stored
        // in the column.
        var canonicalUrl = coords.NormalizedUrl.ToString();

        // Idempotency check first — saves a GitHub round-trip on a re-submit
        // (clicking "Add" twice should not waste a metadata call).
        var existing = await _db.Repos
            .AsNoTracking()
            .Include(r => r.AddedByUser)
            .Where(r => r.OrgId == orgId
                     && r.GithubUrl == canonicalUrl
                     && r.DeletedAt == null)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            _log.LogInformation(
                "Repo manual-add idempotent hit: org_id={OrgId} repo_id={RepoId} url={Url}",
                orgId, existing.Id, canonicalUrl);
            return AddRepoResult.AlreadyExists(ToSummary(existing));
        }

        // Pre-flight: does the repo exist + is it public?
        // Manual add explicitly rejects private repos (PR 7 brings the OAuth
        // user-token path; the path here cannot authenticate to a private
        // repo and must not pretend to). Note: this uses the Validation-
        // namespace metadata shape (Exists/Forbidden flags) — distinct from
        // the Github-namespace GitHubRepoDetails used by the org-import
        // path below.
        GitHubRepoMetadata meta;
        try
        {
            meta = await _github.GetMetadataAsync(coords, token: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // GetMetadataAsync's contract says it doesn't throw on 4xx; only
            // genuine HTTP/parse failures slip out. Surface as a 400 — the
            // user can retry, and we don't want to leak HttpClient internals
            // in a 5xx.
            _log.LogWarning(ex, "GitHub metadata threw for {Url}", canonicalUrl);
            return AddRepoResult.Error(
                "repo_not_found_or_private",
                "Could not reach GitHub to verify the repository. Try again, or use Connect GitHub for private repos (coming soon).");
        }

        if (!meta.Exists)
        {
            return AddRepoResult.Error(
                "repo_not_found_or_private",
                "Repository not found. If it's private, use Connect GitHub (coming soon) instead of manual add.");
        }
        if (meta.Forbidden)
        {
            // GitHub returned 401/403 on an unauthenticated call → the repo is
            // private (or org policy blocks unauth). Either way, manual add
            // can't proceed.
            return AddRepoResult.Error(
                "repo_is_private",
                "This repository is private. Use Connect GitHub (coming soon) to authorize Lintty to read it.");
        }

        // Insert. is_private hard-coded to false — Apêndice E §E.8 says
        // manual adds leave the four GitHub-import fields null/false.
        var repo = new Repo
        {
            OrgId = orgId,
            AddedByUserId = userId,
            GithubUrl = canonicalUrl,
            IsPrivate = false,
            GithubRepoId = null,
            GithubOrgLogin = null,
            DefaultBranch = null,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Repos.Add(repo);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Race window: concurrent POSTs from the same client. The partial
            // unique index catches the second insert. Re-read and treat as
            // idempotent — same outcome the user would have got if the first
            // request had won.
            _db.ChangeTracker.Clear();
            var raced = await _db.Repos
                .AsNoTracking()
                .Include(r => r.AddedByUser)
                .Where(r => r.OrgId == orgId
                         && r.GithubUrl == canonicalUrl
                         && r.DeletedAt == null)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (raced is not null)
            {
                _log.LogInformation(
                    "Repo manual-add raced; second insert lost: org_id={OrgId} repo_id={RepoId}",
                    orgId, raced.Id);
                return AddRepoResult.AlreadyExists(ToSummary(raced));
            }
            _log.LogError(ex, "Repo manual-add insert failed: org_id={OrgId} url={Url}", orgId, canonicalUrl);
            throw;
        }

        // Re-fetch with AddedByUser hydrated so the wire response carries
        // the display name. Cheaper than tracking a separate query — repo.Id
        // is now set so this is a single PK lookup.
        var hydrated = await _db.Repos
            .AsNoTracking()
            .Include(r => r.AddedByUser)
            .FirstAsync(r => r.Id == repo.Id, ct)
            .ConfigureAwait(false);

        _log.LogInformation(
            "Repo manual-add: org_id={OrgId} repo_id={RepoId} added_by={UserId} url={Url}",
            orgId, hydrated.Id, userId, canonicalUrl);
        return AddRepoResult.Created(ToSummary(hydrated));
    }

    public async Task<IReadOnlyList<RepoSummary>> ListAsync(long orgId, CancellationToken ct)
    {
        var rows = await _db.Repos
            .AsNoTracking()
            .Include(r => r.AddedByUser)
            .Where(r => r.OrgId == orgId && r.DeletedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)            // tie-break for created-at collisions in tests
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(ToSummary).ToList();
    }

    public async Task<RepoSummary?> GetAsync(long orgId, long repoId, CancellationToken ct)
    {
        var row = await _db.Repos
            .AsNoTracking()
            .Include(r => r.AddedByUser)
            .Where(r => r.Id == repoId && r.OrgId == orgId && r.DeletedAt == null)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return row is null ? null : ToSummary(row);
    }

    public async Task<bool> SoftDeleteAsync(long orgId, long repoId, CancellationToken ct)
    {
        var row = await _db.Repos
            .Where(r => r.Id == repoId && r.OrgId == orgId && r.DeletedAt == null)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (row is null) return false;

        row.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _log.LogInformation(
            "Repo soft-delete: org_id={OrgId} repo_id={RepoId}",
            orgId, repoId);
        return true;
    }

    public async Task<ImportRepoResult> ImportFromGithubOrgAsync(
        long orgId,
        long userId,
        string accessToken,
        string githubOrgLogin,
        string repoFullName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repoFullName) || !repoFullName.Contains('/', StringComparison.Ordinal))
        {
            return ImportRepoResult.Error(
                "invalid_repo_full_name",
                "repoFullName must be 'owner/name'.");
        }

        // Authenticated metadata fetch. Apêndice E §E.8 says private repos
        // surface here as Private=true; the import is the only path that
        // sets that flag, so we never inherit a stale value.
        GitHubRepoDetails? meta;
        try
        {
            meta = await _orgsClient.GetRepoMetadataAsync(accessToken, repoFullName, ct).ConfigureAwait(false);
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized
                                          || ex.StatusCode == HttpStatusCode.Forbidden)
        {
            // Token revoked between connect and import, or the user
            // dropped a scope on a re-grant. Surface as 403 with a code
            // the frontend can match.
            _log.LogWarning(ex,
                "GitHub repo import rejected: insufficient scopes user_id={UserId} repo={Repo}",
                userId, repoFullName);
            return ImportRepoResult.InsufficientScopes();
        }

        if (meta is null)
        {
            return ImportRepoResult.RepoNotFound();
        }

        // Idempotency by (org_id, github_repo_id). A repo renamed at GitHub
        // changes its full_name + clone_url but keeps its numeric id, so we
        // dedupe on the stable identity. The unique index on
        // (org_id, github_repo_id) WHERE github_repo_id IS NOT NULL backs
        // this — we still re-read on conflict for the race-loser to return
        // a sensible payload.
        var existing = await _db.Repos
            .AsNoTracking()
            .Include(r => r.AddedByUser)
            .Where(r => r.OrgId == orgId
                     && r.GithubRepoId == meta.Id
                     && r.DeletedAt == null)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            _log.LogInformation(
                "Repo import idempotent hit: org_id={OrgId} repo_id={RepoId} github_repo_id={GhId}",
                orgId, existing.Id, meta.Id);
            return ImportRepoResult.AlreadyExists(ToSummary(existing));
        }

        // Canonicalise the clone URL through UrlValidator so the stored
        // github_url shape is identical to the manual-add path. UrlValidator
        // strips .git and trailing slash; we trust the GitHub-provided
        // host but lowercase via TryParse anyway. If GitHub somehow handed
        // us a non-github.com host (impossible for /repos/ output), fall
        // back to an Error so we don't persist a row we can't clone.
        var coords = UrlValidator.TryParse(meta.CloneUrl, out var parseError);
        if (coords is null)
        {
            _log.LogWarning(
                "GitHub returned an unparseable clone_url for repo={Repo}: {Url}",
                repoFullName, meta.CloneUrl);
            return ImportRepoResult.Error(
                "invalid_clone_url",
                parseError ?? "GitHub returned an unparseable clone URL.");
        }
        var canonicalUrl = coords.NormalizedUrl.ToString();

        // Owner login for repos.github_org_login: prefer what GitHub
        // returns in full_name (post-rename it reflects the new owner),
        // not the request-time argument. The argument was just used for
        // the org-membership check at the endpoint layer.
        var ownerLogin = meta.FullName.Split('/', 2)[0];

        var repo = new Repo
        {
            OrgId = orgId,
            AddedByUserId = userId,
            GithubUrl = canonicalUrl,
            GithubRepoId = meta.Id,
            GithubOrgLogin = ownerLogin,
            DefaultBranch = meta.DefaultBranch,
            IsPrivate = meta.IsPrivate,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Repos.Add(repo);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Concurrent imports of the same repo by two members of the
            // same Lintty-org. Either uq_repos_github_id (org_id, github_repo_id)
            // or uq_repos_org_url_active (org_id, github_url) catches it.
            _db.ChangeTracker.Clear();
            var raced = await _db.Repos
                .AsNoTracking()
                .Include(r => r.AddedByUser)
                .Where(r => r.OrgId == orgId
                         && r.DeletedAt == null
                         && (r.GithubRepoId == meta.Id || r.GithubUrl == canonicalUrl))
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (raced is not null)
            {
                _log.LogInformation(
                    "Repo import raced; second insert lost: org_id={OrgId} repo_id={RepoId} github_repo_id={GhId}",
                    orgId, raced.Id, meta.Id);
                return ImportRepoResult.AlreadyExists(ToSummary(raced));
            }
            _log.LogError(ex,
                "Repo import insert failed: org_id={OrgId} github_repo_id={GhId}",
                orgId, meta.Id);
            throw;
        }

        // Hydrate AddedByUser for the response payload — same pattern as
        // AddManualAsync. A single PK lookup is cheaper than tracking a
        // separate query.
        var hydrated = await _db.Repos
            .AsNoTracking()
            .Include(r => r.AddedByUser)
            .FirstAsync(r => r.Id == repo.Id, ct)
            .ConfigureAwait(false);

        _log.LogInformation(
            "Repo import: org_id={OrgId} repo_id={RepoId} added_by={UserId} github_repo_id={GhId} private={Private} default_branch={Branch}",
            orgId, hydrated.Id, userId, meta.Id, meta.IsPrivate, meta.DefaultBranch);
        return ImportRepoResult.Created(ToSummary(hydrated));
    }

    private static RepoSummary ToSummary(Repo r)
    {
        var addedByName = r.AddedByUser?.DisplayName ?? string.Empty;
        return new RepoSummary(
            Id: r.Id,
            OrgId: r.OrgId,
            GithubUrl: r.GithubUrl,
            IsPrivate: r.IsPrivate,
            CreatedAt: DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
            AddedByUserId: r.AddedByUserId,
            AddedByDisplayName: addedByName);
    }
}
