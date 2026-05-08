using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector.Repos.Preflight;

/// <summary>
/// EF-backed <see cref="IRepoPreflightService"/>. Reads <c>repos</c> for
/// owner/branch metadata + saved <c>scan_projects</c>, delegates the
/// remote walk to <see cref="IRepoTargetDiscovery"/>, and computes status
/// by combining both.
///
/// <para>
/// <b>Why no caching.</b> V0 ships a fresh discovery on every preflight
/// call — it's one or two GitHub round-trips, the dashboard caller is the
/// rate-limited boundary, and a stale picker is hostile UX (user opens
/// the modal, refreshes their fork's branches, picker still shows last
/// week's csprojs). Per-org cache lands in V1.1 alongside webhook-driven
/// invalidation.
/// </para>
///
/// <para>
/// <b>Auto-detect tiebreaker</b> (kept in sync with
/// <see cref="Lintty.Engine.Core.Workspace.TargetResolver"/>): root-level
/// <c>lintty.yml</c> wins over root-level <c>.sln</c>. The resolver
/// itself prefers <c>.sln</c> when both exist, but that's a CLI-direct
/// fallback for legacy fixtures; on the dashboard we honor the
/// engineer's explicit declaration in lintty.yml because it's the only
/// way to scope a multi-project layout without a sln.
/// </para>
/// </summary>
public sealed class RepoPreflightService : IRepoPreflightService
{
    private readonly LinttyDbContext _db;
    private readonly IRepoTargetDiscovery _discovery;
    private readonly IGitHubUserTokenStore _tokenStore;
    private readonly ILogger<RepoPreflightService> _log;

    public RepoPreflightService(
        LinttyDbContext db,
        IRepoTargetDiscovery discovery,
        IGitHubUserTokenStore tokenStore,
        ILogger<RepoPreflightService> log)
    {
        _db = db;
        _discovery = discovery;
        _tokenStore = tokenStore;
        _log = log;
    }

    public async Task<PreflightResult?> GetAsync(long repoId, long orgId, CancellationToken ct)
    {
        // Cross-tenant defence (§3.5): one filtered SELECT, no separate
        // existence check — the null path covers both "missing" and
        // "belongs to a different org".
        var repo = await _db.Repos
            .AsNoTracking()
            .Where(r => r.Id == repoId && r.OrgId == orgId && r.DeletedAt == null)
            .Select(r => new
            {
                r.Id,
                r.GithubUrl,
                r.GithubOrgLogin,
                r.DefaultBranch,
                r.IsPrivate,
                r.AddedByUserId,
                r.ScanProjects,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (repo is null) return null;

        var (owner, name) = ResolveOwnerAndName(repo.GithubUrl, repo.GithubOrgLogin);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name))
        {
            // Defensive: GithubUrl was canonicalised at insert time so this
            // shouldn't fire in practice. Surface as no_dotnet_project so
            // the user gets a clear message rather than a 500.
            _log.LogWarning(
                "Preflight: could not parse owner/name from github_url={Url} (org_id={OrgId}, repo_id={RepoId})",
                repo.GithubUrl, orgId, repoId);
            return new PreflightResult(
                Status: PreflightStatus.NoDotnetProject,
                AutoDetected: null,
                ScanProjects: NormalizeSavedProjects(repo.ScanProjects),
                Candidates: Array.Empty<PreflightCandidate>(),
                Truncated: false,
                Reason: "Não foi possível interpretar a URL do GitHub deste repositório.");
        }

        // Token resolution mirrors JobWorker.RunScanAsync §E.9: private
        // repos clone with the added_by_user_id's OAuth token; public
        // repos can be walked unauthenticated.
        string? token = null;
        if (repo.IsPrivate)
        {
            token = await _tokenStore.GetActiveTokenAsync(repo.AddedByUserId, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                throw new RepoTargetDiscoveryException(
                    RepoTargetDiscoveryError.Forbidden,
                    "Este repositório é privado e o usuário que o adicionou não tem token GitHub ativo. Reconecte em /api/auth/github/connect/start.");
            }
        }

        RepoTargetDiscoveryResult discovered;
        try
        {
            discovered = await _discovery.DiscoverAsync(
                owner, name, repo.DefaultBranch, token, ct).ConfigureAwait(false);
        }
        catch (RepoTargetDiscoveryException) { throw; }       // surface to endpoint verbatim
        catch (Exception ex)
        {
            // Anything else (parse failure, NRE) becomes Transient so we
            // don't 500 the dashboard.
            throw new RepoTargetDiscoveryException(
                RepoTargetDiscoveryError.Transient,
                $"Discovery failed unexpectedly: {ex.Message}",
                ex);
        }

        var candidates = BuildOrderedCandidates(discovered);
        var savedProjects = NormalizeSavedProjects(repo.ScanProjects);

        if (savedProjects is { Count: > 0 })
        {
            // User has a saved selection — short-circuit, no auto-detect.
            return new PreflightResult(
                Status: PreflightStatus.Ready,
                AutoDetected: null,
                ScanProjects: savedProjects,
                Candidates: candidates,
                Truncated: discovered.Truncated,
                Reason: null);
        }

        // No saved selection — auto-detect logic.
        var rootYml = discovered.LinttyYmlFiles.FirstOrDefault(p =>
            string.Equals(p, "lintty.yml", StringComparison.OrdinalIgnoreCase));
        if (rootYml is not null)
        {
            return new PreflightResult(
                Status: PreflightStatus.Ready,
                AutoDetected: new PreflightAutoDetected(CandidateKind.Yaml, rootYml),
                ScanProjects: null,
                Candidates: candidates,
                Truncated: discovered.Truncated,
                Reason: null);
        }

        var rootSlns = discovered.SlnFiles
            .Where(p => !p.Contains('/', StringComparison.Ordinal))
            .ToList();
        if (rootSlns.Count == 1)
        {
            return new PreflightResult(
                Status: PreflightStatus.Ready,
                AutoDetected: new PreflightAutoDetected(CandidateKind.Sln, rootSlns[0]),
                ScanProjects: null,
                Candidates: candidates,
                Truncated: discovered.Truncated,
                Reason: null);
        }

        // No clean default. Either there are 2+ sln, multiple csprojs but
        // no sln, etc. Tell the user to pick.
        if (discovered.SlnFiles.Count + discovered.CsprojFiles.Count + discovered.LinttyYmlFiles.Count >= 1)
        {
            return new PreflightResult(
                Status: PreflightStatus.NeedsConfig,
                AutoDetected: null,
                ScanProjects: null,
                Candidates: candidates,
                Truncated: discovered.Truncated,
                Reason: BuildNeedsConfigReason(discovered, rootSlns.Count));
        }

        return new PreflightResult(
            Status: PreflightStatus.NoDotnetProject,
            AutoDetected: null,
            ScanProjects: null,
            Candidates: candidates,
            Truncated: discovered.Truncated,
            Reason: "Nenhum .sln, .csproj ou lintty.yml encontrado.");
    }

    public async Task<SetScanProjectsOutcome> SetScanProjectsAsync(
        long repoId,
        long orgId,
        IReadOnlyList<string> projects,
        CancellationToken ct)
    {
        if (projects is null) projects = Array.Empty<string>();

        // Normalize input: trim, force forward slashes, drop leading "./".
        var normalized = NormalizeIncomingPaths(projects);

        // Empty selection clears the saved choice — auto-detect resumes.
        if (normalized.Count == 0)
        {
            var clearedRows = await _db.Repos
                .Where(r => r.Id == repoId && r.OrgId == orgId && r.DeletedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ScanProjects, (string[]?)null), ct)
                .ConfigureAwait(false);
            return clearedRows == 0 ? SetScanProjectsOutcome.NotFound() : SetScanProjectsOutcome.Ok();
        }

        // Combination validation BEFORE we hit the GitHub Tree API. Cheap
        // path-based checks first so a grossly invalid payload fails fast.
        var combinationCheck = ValidateCombination(normalized);
        if (combinationCheck is not null)
        {
            return SetScanProjectsOutcome.Invalid(combinationCheck);
        }

        // Path-against-candidates check: defends against tampering and
        // path traversal. If the dashboard rendered the picker with these
        // candidates, a different value here means the user crafted the
        // PUT manually OR the picker is stale (the truncation flag) — we
        // refuse either way and let the frontend re-fetch preflight.
        var preflight = await GetAsync(repoId, orgId, ct).ConfigureAwait(false);
        if (preflight is null) return SetScanProjectsOutcome.NotFound();

        var candidatePaths = new HashSet<string>(
            preflight.Candidates.Select(c => c.Path),
            StringComparer.Ordinal);
        var unknown = normalized.FirstOrDefault(p => !candidatePaths.Contains(p));
        if (unknown is not null)
        {
            return SetScanProjectsOutcome.Invalid(
                $"Path '{unknown}' is not in the discovered candidate set. Refresh the preflight and pick again.");
        }

        var rows = await _db.Repos
            .Where(r => r.Id == repoId && r.OrgId == orgId && r.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ScanProjects, normalized.ToArray()), ct)
            .ConfigureAwait(false);
        if (rows == 0) return SetScanProjectsOutcome.NotFound();

        _log.LogInformation(
            "Preflight: scan_projects updated for org_id={OrgId} repo_id={RepoId} count={Count}",
            orgId, repoId, normalized.Count);
        return SetScanProjectsOutcome.Ok();
    }

    /// <summary>
    /// Combination validity per spec (PR S1, restored in PR S3 after the
    /// PR S2 multi-PDF revert): empty (handled outside), 1 sln, 1 csproj,
    /// 1 legacy yaml, or 2+ all-csproj. Anything else is invalid because
    /// the engine resolver / runtime-yaml synthesis can't ingest it.
    /// The maximum batch size is enforced here too so a PUT can't seed
    /// a value the worker would later reject. Returns a human message
    /// when invalid, <c>null</c> when valid.
    /// </summary>
    private static string? ValidateCombination(IReadOnlyList<string> projects)
    {
        const int maxEntries = 50;
        if (projects.Count > maxEntries)
            return $"Selection is too large ({projects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} entries); pick at most {maxEntries.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";

        var yaml = 0;
        var sln = 0;
        var csproj = 0;
        var unknown = 0;
        foreach (var p in projects)
        {
            if (string.IsNullOrEmpty(p)) return "Empty path is not allowed.";
            if (p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) sln++;
            else if (p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) csproj++;
            else if (string.Equals(p, "lintty.yml", StringComparison.OrdinalIgnoreCase)
                  || p.EndsWith("/lintty.yml", StringComparison.OrdinalIgnoreCase)) yaml++;
            else unknown++;
        }
        if (unknown > 0) return "Only .sln, .csproj, or lintty.yml paths are allowed.";
        if (sln > 1) return "Pick a single .sln, not multiple.";
        if (yaml > 1) return "Pick a single lintty.yml, not multiple.";
        if (sln + yaml > 0 && csproj > 0)
            return "Cannot mix .sln/lintty.yml with .csproj. Pick a solution OR a project list.";
        if (sln + yaml + csproj == 0) return "Selection is empty.";
        return null;
    }

    /// <summary>
    /// Normalize incoming paths: trim, replace backslash with slash, drop
    /// leading <c>./</c>, reject leading <c>/</c> or absolute Windows
    /// paths. Returns a deduplicated list preserving caller order.
    /// </summary>
    private static List<string> NormalizeIncomingPaths(IReadOnlyList<string> input)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(input.Count);
        foreach (var raw in input)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var p = raw.Trim().Replace('\\', '/');
            if (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
            // Reject leading slash (would be absolute on POSIX) or
            // ".." segments. The candidate-set check below also rejects
            // these because they wouldn't appear in the discovery output.
            if (p.StartsWith('/') || p.Contains("../", StringComparison.Ordinal) || p == "..") continue;
            if (seen.Add(p)) result.Add(p);
        }
        return result;
    }

    /// <summary>
    /// Project-list normalization for the column read path: collapse
    /// null/empty to null so callers see one shape.
    /// </summary>
    private static IReadOnlyList<string>? NormalizeSavedProjects(string[]? saved)
    {
        if (saved is null || saved.Length == 0) return null;
        return saved.ToArray();
    }

    /// <summary>
    /// Build the ordered candidate list the dashboard renders. Sprint 3
    /// PR S2 — yaml is intentionally <b>excluded</b> from the picker
    /// because a <c>lintty.yml</c> without a <c>projects:</c> declaration
    /// fails the engine resolver, and the dashboard cannot tell from a
    /// tree walk whether a yaml has a usable <c>projects:</c> block.
    /// Discovery still surfaces yaml files so the auto-detect status
    /// branch (root <c>lintty.yml</c> wins) works; users just can't pick
    /// them explicitly. Order: sln (alpha), then csproj (alpha) —
    /// already alpha-sorted inside categories by
    /// <see cref="GitHubRepoTargetDiscovery"/>.
    /// </summary>
    private static IReadOnlyList<PreflightCandidate> BuildOrderedCandidates(RepoTargetDiscoveryResult d)
    {
        var list = new List<PreflightCandidate>(d.SlnFiles.Count + d.CsprojFiles.Count);
        foreach (var p in d.SlnFiles) list.Add(new PreflightCandidate(CandidateKind.Sln, p));
        foreach (var p in d.CsprojFiles) list.Add(new PreflightCandidate(CandidateKind.Csproj, p));
        return list;
    }

    private static string BuildNeedsConfigReason(RepoTargetDiscoveryResult d, int rootSlnCount)
    {
        if (rootSlnCount > 1)
            return "Vários arquivos .sln na raiz. Escolha qual analisar.";
        if (d.CsprojFiles.Count > 0 && d.SlnFiles.Count == 0)
            return "Múltiplos .csproj encontrados e nenhum .sln na raiz. Escolha qual(is) analisar.";
        if (d.SlnFiles.Count > 0)
            return "Arquivos .sln encontrados, mas nenhum na raiz. Escolha qual analisar.";
        return "Não foi possível auto-detectar o alvo. Escolha um candidato.";
    }

    /// <summary>
    /// Best-effort owner/name extraction. Prefers the cached
    /// <c>github_org_login</c> for the owner — that field comes straight
    /// from GitHub on import, while <c>github_url</c> path parsing has to
    /// handle a few historical shapes.
    /// </summary>
    private static (string Owner, string Name) ResolveOwnerAndName(
        string githubUrl,
        string? cachedOwner)
    {
        // Try UrlValidator first — it's the canonical parser.
        var coords = UrlValidator.TryParse(githubUrl, out _);
        if (coords is not null)
        {
            return (cachedOwner ?? coords.Owner, coords.Repo);
        }
        // Defensive: extract from raw text. Should be unreachable because
        // RepoService canonicalises at insert.
        var idx = githubUrl.LastIndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return (string.Empty, string.Empty);
        var rest = githubUrl[(idx + "github.com/".Length)..].TrimEnd('/');
        var parts = rest.Split('/', 2);
        if (parts.Length < 2) return (string.Empty, string.Empty);
        return (cachedOwner ?? parts[0], parts[1].Replace(".git", string.Empty));
    }
}
