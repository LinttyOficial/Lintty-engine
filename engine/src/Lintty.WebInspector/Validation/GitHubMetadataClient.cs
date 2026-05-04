using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Lintty.WebInspector.Validation;

/// <summary>
/// Real <see cref="IGitHubMetadataClient"/> backed by an HttpClient. We send
/// <c>User-Agent: lintty-web-inspector</c> per spec §6 (GitHub rejects
/// requests without a UA). Timeouts are short — this is a pre-flight check,
/// not a long-poll.
/// </summary>
public sealed class GitHubMetadataClient : IGitHubMetadataClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GitHubMetadataClient> _logger;

    public const string HttpClientName = "github-metadata";

    public GitHubMetadataClient(HttpClient http, ILogger<GitHubMetadataClient> logger)
    {
        _http = http;
        _logger = logger;
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("lintty-web-inspector"))
        {
            // Fallback for environments where TryParseAdd is fussy.
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("lintty-web-inspector", "0.1"));
        }
        if (_http.Timeout == System.Threading.Timeout.InfiniteTimeSpan || _http.Timeout > TimeSpan.FromSeconds(15))
            _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<GitHubRepoMetadata> GetMetadataAsync(GitHubRepoCoordinates coords, string? token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, coords.ApiUrl);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return new GitHubRepoMetadata(Exists: false, Forbidden: false, SizeKilobytes: 0, DefaultBranch: null);
            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Unauthorized)
                return new GitHubRepoMetadata(Exists: true, Forbidden: true, SizeKilobytes: 0, DefaultBranch: null);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub metadata returned {Status} for {Url}", resp.StatusCode, coords.ApiUrl);
                return new GitHubRepoMetadata(Exists: false, Forbidden: false, SizeKilobytes: 0, DefaultBranch: null);
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            // GitHub reports size in kilobytes (rounded). default_branch is the working ref.
            var sizeKb = doc.RootElement.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                ? sizeProp.GetInt64()
                : 0L;
            var branch = doc.RootElement.TryGetProperty("default_branch", out var branchProp) && branchProp.ValueKind == JsonValueKind.String
                ? branchProp.GetString()
                : null;
            return new GitHubRepoMetadata(Exists: true, Forbidden: false, SizeKilobytes: sizeKb, DefaultBranch: branch);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "GitHub metadata request failed for {Url}", coords.ApiUrl);
            // Network blip is not the user's fault — let the request through but warn.
            return new GitHubRepoMetadata(Exists: true, Forbidden: false, SizeKilobytes: 0, DefaultBranch: null);
        }
    }
}
