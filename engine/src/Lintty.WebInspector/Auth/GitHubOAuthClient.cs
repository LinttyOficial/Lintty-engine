using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lintty.WebInspector.Configuration;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// Production <see cref="IGitHubOAuthClient"/> talking to <c>github.com</c>
/// and <c>api.github.com</c>. Tests substitute this with a fake.
/// </summary>
public sealed class GitHubOAuthClient : IGitHubOAuthClient
{
    public const string HttpClientName = "github-oauth";

    private readonly HttpClient _http;
    private readonly GitHubOAuthOptions _opts;
    private readonly ILogger<GitHubOAuthClient> _logger;

    public GitHubOAuthClient(HttpClient http, IOptions<GitHubOAuthOptions> opts, ILogger<GitHubOAuthClient> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Lintty-WebInspector/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct)
    {
        var grant = await ExchangeCodeForTokenWithScopesAsync(code, redirectUri, ct).ConfigureAwait(false);
        return grant.AccessToken;
    }

    public async Task<GitHubTokenGrant> ExchangeCodeForTokenWithScopesAsync(
        string code, string redirectUri, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ClientId) || string.IsNullOrWhiteSpace(_opts.ClientSecret))
            throw new InvalidOperationException("GitHub OAuth credentials are not configured.");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(form),
        };
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty token response from GitHub.");
        if (string.IsNullOrEmpty(payload.AccessToken))
            throw new InvalidOperationException(
                $"GitHub OAuth code exchange failed: error={payload.Error} description={payload.ErrorDescription}");

        return new GitHubTokenGrant(payload.AccessToken, ParseScopes(payload.Scope));
    }

    /// <summary>
    /// Parses the CSV scope string GitHub returns in the access-token
    /// response (e.g. <c>"repo,read:org"</c>) into a normalized array.
    /// Empty / null input → empty array. Whitespace around commas is
    /// tolerated.
    /// </summary>
    internal static string[] ParseScopes(string? scopeCsv)
    {
        if (string.IsNullOrWhiteSpace(scopeCsv)) return Array.Empty<string>();
        var parts = scopeCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts;
    }

    public async Task<GitHubUserProfile> GetUserAsync(string accessToken, CancellationToken ct)
    {
        var user = await GetJsonAsync<UserResponse>("https://api.github.com/user", accessToken, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty user response from GitHub.");

        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            // GitHub users with private primary email need /user/emails.
            var emails = await GetJsonAsync<List<EmailResponse>>("https://api.github.com/user/emails", accessToken, ct)
                .ConfigureAwait(false) ?? new List<EmailResponse>();
            foreach (var e in emails)
            {
                if (e.Primary && e.Verified) { email = e.Address; break; }
            }
            if (string.IsNullOrWhiteSpace(email))
            {
                foreach (var e in emails)
                {
                    if (e.Verified) { email = e.Address; break; }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("GitHub returned no usable email; cannot link account.");

        return new GitHubUserProfile(
            ProviderUserId: user.Id.ToString(CultureInfo.InvariantCulture),
            Login: user.Login,
            Name: user.Name,
            Email: email!);
    }

    private async Task<T?> GetJsonAsync<T>(string url, string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return default;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }

    private sealed class UserResponse
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
    }

    private sealed class EmailResponse
    {
        [JsonPropertyName("email")] public string Address { get; set; } = string.Empty;
        [JsonPropertyName("primary")] public bool Primary { get; set; }
        [JsonPropertyName("verified")] public bool Verified { get; set; }
    }
}
