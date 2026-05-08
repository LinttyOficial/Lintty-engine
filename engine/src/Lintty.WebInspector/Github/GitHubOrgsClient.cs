using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Lintty.WebInspector.Github;

/// <summary>
/// Production <see cref="IGitHubOrgsClient"/> talking to <c>api.github.com</c>.
/// Pattern lifted verbatim from
/// <see cref="Lintty.WebInspector.Auth.GitHubOAuthClient"/>:
/// <c>HttpClient</c> via <see cref="HttpClient"/>, fixed accept header
/// + UA, no retries (the dashboard polls; transient blips surface as
/// 4xx/5xx and the user gets a "tente novamente" toast).
///
/// <para>
/// <b>Pagination scope.</b> Per spec §E.7 / §E.8, V0 ships single-page
/// reads (<c>per_page=100</c>). A user with &gt;100 orgs or an org with
/// &gt;100 repos sees the truncation point logged at <c>Warning</c> so
/// V1.1 (full pagination + cache) has telemetry to tune against.
/// </para>
///
/// <para>
/// <b>Why no retries.</b> GitHub's secondary rate limit fires on
/// aggressive backoff loops; the dashboard contract says "request → if
/// fails, surface the error verbatim". Re-issuing here would mask
/// 401/403 (token issues) as transient.
/// </para>
/// </summary>
public sealed class GitHubOrgsClient : IGitHubOrgsClient
{
    public const string HttpClientName = "github-orgs";
    private const int PageSize = 100;

    private readonly HttpClient _http;
    private readonly ILogger<GitHubOrgsClient> _logger;

    public GitHubOrgsClient(HttpClient http, ILogger<GitHubOrgsClient> logger)
    {
        _http = http;
        _logger = logger;
        // GitHub rejects requests without a User-Agent. Same UA as the
        // OAuth client so log correlation across the two surfaces is
        // simple.
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("Lintty-WebInspector/1.0"))
        {
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lintty-WebInspector", "1.0"));
        }
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // Pin the API version so a future GitHub default doesn't silently
        // change the response shape (their stability policy honors this
        // header).
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        // Reasonable upper bound — list calls usually finish under a second
        // but a flaky link shouldn't tie up a request thread for minutes.
        if (_http.Timeout == System.Threading.Timeout.InfiniteTimeSpan || _http.Timeout > TimeSpan.FromSeconds(15))
            _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<IReadOnlyList<GitHubOrgSummary>> ListOrgsAsync(
        string accessToken,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentException("accessToken must not be empty", nameof(accessToken));

        var url = $"https://api.github.com/user/orgs?per_page={PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var (resp, body) = await SendAsync<List<OrgRow>>(url, accessToken, ct).ConfigureAwait(false);
        EnsureNot4xx(resp, "list orgs", url);
        WarnIfTruncated(resp, body?.Count ?? 0, "orgs", url);

        var rows = body ?? new List<OrgRow>();
        var result = new List<GitHubOrgSummary>(rows.Count);
        foreach (var r in rows)
        {
            result.Add(new GitHubOrgSummary(r.Id, r.Login ?? string.Empty, r.AvatarUrl));
        }
        return result;
    }

    public async Task<IReadOnlyList<GitHubRepoSummary>> ListReposAsync(
        string accessToken,
        string orgLogin,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentException("accessToken must not be empty", nameof(accessToken));
        if (string.IsNullOrEmpty(orgLogin))
            throw new ArgumentException("orgLogin must not be empty", nameof(orgLogin));

        // type=all so we get private + public + forks + sources in one call.
        var url = $"https://api.github.com/orgs/{Uri.EscapeDataString(orgLogin)}/repos?type=all&per_page={PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var (resp, body) = await SendAsync<List<RepoRow>>(url, accessToken, ct).ConfigureAwait(false);
        EnsureNot4xx(resp, "list repos", url);
        WarnIfTruncated(resp, body?.Count ?? 0, $"repos for org={orgLogin}", url);

        var rows = body ?? new List<RepoRow>();
        var result = new List<GitHubRepoSummary>(rows.Count);
        foreach (var r in rows)
        {
            // Some GitHub responses can return null default_branch for
            // empty repos (zero commits). We coerce to "main" — the import
            // path overrides that anyway when the user picks the repo, and
            // the listing is for display only.
            result.Add(new GitHubRepoSummary(
                Id: r.Id,
                Name: r.Name ?? string.Empty,
                FullName: r.FullName ?? string.Empty,
                IsPrivate: r.Private,
                DefaultBranch: r.DefaultBranch ?? "main",
                HtmlUrl: r.HtmlUrl ?? string.Empty));
        }
        return result;
    }

    public async Task<GitHubRepoDetails?> GetRepoMetadataAsync(
        string accessToken,
        string repoFullName,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentException("accessToken must not be empty", nameof(accessToken));
        if (string.IsNullOrEmpty(repoFullName) || !repoFullName.Contains('/'))
            throw new ArgumentException("repoFullName must be 'owner/name'", nameof(repoFullName));

        // Don't escape the slash — GitHub's path is two segments owner/name.
        // Escape each segment individually so weird names with periods etc.
        // round-trip safely.
        var slashIdx = repoFullName.IndexOf('/');
        var owner = Uri.EscapeDataString(repoFullName[..slashIdx]);
        var name = Uri.EscapeDataString(repoFullName[(slashIdx + 1)..]);
        var url = $"https://api.github.com/repos/{owner}/{name}";

        using var req = BuildRequest(HttpMethod.Get, url, accessToken);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if ((int)resp.StatusCode is >= 400 and < 500)
        {
            throw new GitHubApiException(resp.StatusCode,
                $"GitHub returned {(int)resp.StatusCode} for {url}");
        }
        resp.EnsureSuccessStatusCode();

        var row = await resp.Content.ReadFromJsonAsync<RepoRow>(cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub returned an empty repo body.");

        // CloneUrl preference: GitHub returns multiple variants; the HTTPS
        // clone URL is what `git clone` against api.github.com expects and
        // is what we'll feed UrlValidator. Falling back to the html_url is
        // a defensive last resort — every public/private repo we'll see
        // has clone_url populated.
        var cloneUrl = row.CloneUrl ?? row.HtmlUrl ?? throw new InvalidOperationException(
            "GitHub repo metadata missing both clone_url and html_url.");

        return new GitHubRepoDetails(
            Id: row.Id,
            Name: row.Name ?? string.Empty,
            FullName: row.FullName ?? string.Empty,
            IsPrivate: row.Private,
            DefaultBranch: row.DefaultBranch ?? "main",
            CloneUrl: cloneUrl);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<(HttpResponseMessage Response, T? Body)> SendAsync<T>(
        string url, string accessToken, CancellationToken ct)
        where T : class
    {
        using var req = BuildRequest(HttpMethod.Get, url, accessToken);
        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        // Caller decides whether to throw based on the status code.
        T? body = null;
        if (resp.IsSuccessStatusCode)
        {
            body = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
        }
        return (resp, body);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string accessToken)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return req;
    }

    private static void EnsureNot4xx(HttpResponseMessage resp, string opLabel, string url)
    {
        if (resp.IsSuccessStatusCode) return;
        if ((int)resp.StatusCode is >= 400 and < 500)
        {
            throw new GitHubApiException(resp.StatusCode,
                $"GitHub returned {(int)resp.StatusCode} on {opLabel} ({url})");
        }
        // 5xx — let the caller see HttpRequestException via EnsureSuccessStatusCode.
        resp.EnsureSuccessStatusCode();
    }

    private void WarnIfTruncated(HttpResponseMessage resp, int rowCount, string subject, string url)
    {
        if (rowCount < PageSize) return;
        // Single-page V0: if we returned exactly PageSize rows, GitHub
        // probably has more. Emit a warning so the next page can be paged
        // in V1.1 without changing the contract.
        if (resp.Headers.TryGetValues("Link", out var linkHeaders))
        {
            foreach (var link in linkHeaders)
            {
                if (link.Contains("rel=\"next\"", StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "GitHub {Subject} listing truncated at {PageSize}; pagination not yet implemented (V1.1 / Apêndice E §E.13 E6). url={Url}",
                        subject, PageSize, url);
                    return;
                }
            }
        }
        // No Link header → exactly 100 rows fits one page; nothing to warn about.
    }

    // ── Wire DTOs ──────────────────────────────────────────────────────────

#pragma warning disable CA1812 // instantiated by System.Text.Json
    private sealed class OrgRow
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("login")] public string? Login { get; set; }
        [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
    }

    private sealed class RepoRow
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("full_name")] public string? FullName { get; set; }
        [JsonPropertyName("private")] public bool Private { get; set; }
        [JsonPropertyName("default_branch")] public string? DefaultBranch { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("clone_url")] public string? CloneUrl { get; set; }
    }
#pragma warning restore CA1812
}
