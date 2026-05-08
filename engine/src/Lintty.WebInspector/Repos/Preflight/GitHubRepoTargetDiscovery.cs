using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Lintty.WebInspector.Repos.Preflight;

/// <summary>
/// Production <see cref="IRepoTargetDiscovery"/> backed by the GitHub Tree
/// API. Same HttpClient pattern as
/// <see cref="Lintty.WebInspector.Github.GitHubOrgsClient"/>: pinned UA,
/// pinned API version, no automatic retries.
///
/// <para>
/// Two GitHub round-trips per discovery:
/// <list type="number">
///   <item><description><c>GET /repos/{owner}/{name}</c> when the caller
///         passes a null/empty branch — we read <c>default_branch</c>.
///         Skipped when the caller already supplied a branch (the dashboard
///         passes <c>repos.default_branch</c>, snapshotted at import).</description></item>
///   <item><description><c>GET /repos/{owner}/{name}/git/trees/{branchOrSha}?recursive=1</c>
///         to list every blob path. The Tree API accepts a branch name
///         directly (the response shape is the same as for a SHA), so we
///         skip the <c>refs/heads/{branch}</c> dance.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Truncation.</b> Two sources collapse into the single
/// <see cref="RepoTargetDiscoveryResult.Truncated"/> flag: GitHub's own
/// <c>"truncated": true</c> for &gt;100k items, and our per-category 100
/// cap. Either way the dashboard tells the user the picker may be
/// incomplete and offers a "manual entry" fallback (V1.1).
/// </para>
/// </summary>
public sealed class GitHubRepoTargetDiscovery : IRepoTargetDiscovery
{
    public const string HttpClientName = "github-tree";

    /// <summary>
    /// Per-category cap. GitHub's Tree API itself caps at ~7 MB / 100k
    /// items; this is a soft cap on our side so the picker doesn't
    /// drown the dashboard with hundreds of csprojs from a monorepo.
    /// </summary>
    public const int PerCategoryCap = 100;

    private readonly HttpClient _http;
    private readonly ILogger<GitHubRepoTargetDiscovery> _logger;

    public GitHubRepoTargetDiscovery(HttpClient http, ILogger<GitHubRepoTargetDiscovery> logger)
    {
        _http = http;
        _logger = logger;

        // Same UA family as the orgs client so log correlation across the
        // two GitHub surfaces is simple.
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("Lintty-WebInspector/1.0"))
        {
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lintty-WebInspector", "1.0"));
        }
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_http.Timeout == System.Threading.Timeout.InfiniteTimeSpan || _http.Timeout > TimeSpan.FromSeconds(15))
            _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<RepoTargetDiscoveryResult> DiscoverAsync(
        string ownerLogin,
        string repoName,
        string? branch,
        string? userToken,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ownerLogin)) throw new ArgumentException("ownerLogin is required.", nameof(ownerLogin));
        if (string.IsNullOrEmpty(repoName)) throw new ArgumentException("repoName is required.", nameof(repoName));

        var ownerEsc = Uri.EscapeDataString(ownerLogin);
        var repoEsc = Uri.EscapeDataString(repoName);

        // Resolve the branch if the caller didn't supply one. We could also
        // resolve via /branches/{branch} but /repos/{owner}/{repo} returns
        // default_branch in the same payload and is what the import path
        // already uses, so cache reuse on the network layer is better.
        var resolvedBranch = branch;
        if (string.IsNullOrWhiteSpace(resolvedBranch))
        {
            var repoUrl = $"https://api.github.com/repos/{ownerEsc}/{repoEsc}";
            var repoMeta = await SendJsonAsync<RepoMetaRow>(repoUrl, userToken, ct).ConfigureAwait(false);
            resolvedBranch = string.IsNullOrEmpty(repoMeta?.DefaultBranch) ? "main" : repoMeta!.DefaultBranch;
        }

        // Tree API accepts a branch name directly. ?recursive=1 returns the
        // full tree in one call; truncation surfaces in the response body.
        var branchEsc = Uri.EscapeDataString(resolvedBranch!);
        var treeUrl = $"https://api.github.com/repos/{ownerEsc}/{repoEsc}/git/trees/{branchEsc}?recursive=1";
        var tree = await SendJsonAsync<TreeRow>(treeUrl, userToken, ct).ConfigureAwait(false);
        if (tree is null)
        {
            // 200 but null body — defensive; treat as transient.
            throw new RepoTargetDiscoveryException(
                RepoTargetDiscoveryError.Transient,
                $"GitHub returned an empty tree for {ownerLogin}/{repoName}@{resolvedBranch}.");
        }

        var slns = new List<string>();
        var csprojs = new List<string>();
        var ymls = new List<string>();
        var capHit = false;

        foreach (var node in tree.Tree ?? Array.Empty<TreeNode>())
        {
            // Skip non-blob entries (commits, trees). Blobs are files.
            if (!string.Equals(node.Type, "blob", StringComparison.Ordinal)) continue;
            var path = node.Path;
            if (string.IsNullOrEmpty(path)) continue;

            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                if (slns.Count >= PerCategoryCap) { capHit = true; continue; }
                slns.Add(path);
            }
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                if (csprojs.Count >= PerCategoryCap) { capHit = true; continue; }
                csprojs.Add(path);
            }
            else if (string.Equals(path, "lintty.yml", StringComparison.OrdinalIgnoreCase)
                  || path.EndsWith("/lintty.yml", StringComparison.OrdinalIgnoreCase))
            {
                if (ymls.Count >= PerCategoryCap) { capHit = true; continue; }
                ymls.Add(path);
            }
        }

        // Stable alphabetic ordering inside each category — the preflight
        // service sorts the merged candidate list deterministically and
        // tests rely on the order.
        slns.Sort(StringComparer.Ordinal);
        csprojs.Sort(StringComparer.Ordinal);
        ymls.Sort(StringComparer.Ordinal);

        var truncated = (tree.Truncated ?? false) || capHit;
        if (truncated)
        {
            _logger.LogWarning(
                "Tree discovery hit truncation for {Owner}/{Repo}@{Branch}: github_truncated={GhT}, cap_hit={CapHit}, slns={Slns}, csprojs={Csprojs}, ymls={Ymls}",
                ownerLogin, repoName, resolvedBranch, tree.Truncated ?? false, capHit, slns.Count, csprojs.Count, ymls.Count);
        }

        return new RepoTargetDiscoveryResult(
            SlnFiles: slns,
            CsprojFiles: csprojs,
            LinttyYmlFiles: ymls,
            Truncated: truncated);
    }

    private async Task<T?> SendJsonAsync<T>(string url, string? token, CancellationToken ct)
        where T : class
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RepoTargetDiscoveryException(
                RepoTargetDiscoveryError.Transient,
                $"GitHub request failed for {url}: {ex.Message}",
                ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Distinguish HttpClient timeout from a caller-driven cancel.
            throw new RepoTargetDiscoveryException(
                RepoTargetDiscoveryError.Transient,
                $"GitHub request timed out for {url}.",
                ex);
        }

        try
        {
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                throw new RepoTargetDiscoveryException(
                    RepoTargetDiscoveryError.NotAccessible,
                    $"GitHub returned 404 for {url}.");
            }
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // 403 without a token reads as "rate-limited or private";
                // with a token it reads as "scope dropped or token revoked".
                // Either maps to the Forbidden category — endpoint surfaces 403.
                throw new RepoTargetDiscoveryException(
                    RepoTargetDiscoveryError.Forbidden,
                    string.IsNullOrEmpty(token)
                        ? $"GitHub returned {(int)resp.StatusCode} for {url} (private repo or rate-limited; reconnect required for private)."
                        : $"GitHub returned {(int)resp.StatusCode} for {url} (token revoked or insufficient scopes).");
            }
            if ((int)resp.StatusCode is >= 400 and < 500)
            {
                throw new RepoTargetDiscoveryException(
                    RepoTargetDiscoveryError.NotAccessible,
                    $"GitHub returned {(int)resp.StatusCode} for {url}.");
            }
            if (!resp.IsSuccessStatusCode)
            {
                throw new RepoTargetDiscoveryException(
                    RepoTargetDiscoveryError.Transient,
                    $"GitHub returned {((int)resp.StatusCode).ToString(CultureInfo.InvariantCulture)} for {url}.");
            }

            return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            resp.Dispose();
        }
    }

    // ── Wire DTOs ──────────────────────────────────────────────────────────

#pragma warning disable CA1812 // instantiated by System.Text.Json
    private sealed class RepoMetaRow
    {
        [JsonPropertyName("default_branch")] public string? DefaultBranch { get; set; }
    }

    private sealed class TreeRow
    {
        [JsonPropertyName("sha")] public string? Sha { get; set; }
        [JsonPropertyName("truncated")] public bool? Truncated { get; set; }
        [JsonPropertyName("tree")] public TreeNode[]? Tree { get; set; }
    }

    private sealed class TreeNode
    {
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
#pragma warning restore CA1812
}
