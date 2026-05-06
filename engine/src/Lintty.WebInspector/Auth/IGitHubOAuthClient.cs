using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// Thin wrapper around the GitHub OAuth + REST API surface that the auth
/// callback needs. Extracted as an interface so tests can substitute a stub
/// (real OAuth flow is unscriptable in xUnit). ADR 0007 §3.6.
/// </summary>
public interface IGitHubOAuthClient
{
    /// <summary>
    /// Exchanges an OAuth <c>code</c> for an access token via
    /// <c>POST https://github.com/login/oauth/access_token</c>.
    /// </summary>
    Task<string> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct);

    /// <summary>
    /// Fetches the authenticated user's profile + verified primary email.
    /// Combines <c>GET /user</c> + <c>GET /user/emails</c> so the caller has
    /// everything needed to upsert a row in <c>users</c> +
    /// <c>external_logins</c>.
    /// </summary>
    Task<GitHubUserProfile> GetUserAsync(string accessToken, CancellationToken ct);
}

/// <summary>
/// Subset of the GitHub user record + primary email that the auth callback
/// needs. <see cref="ProviderUserId"/> is the stable numeric id (string-typed
/// per <c>external_logins.provider_user_id</c> contract).
/// </summary>
public sealed record GitHubUserProfile(
    string ProviderUserId,
    string Login,
    string? Name,
    string Email);
