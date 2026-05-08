using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// Thin wrapper around the GitHub OAuth + REST API surface that the auth
/// callback needs. Extracted as an interface so tests can substitute a stub
/// (real OAuth flow is unscriptable in xUnit). ADR 0007 §3.6 +
/// Apêndice E §E.5/§E.6 (the elevated <c>connect</c> flow).
/// </summary>
public interface IGitHubOAuthClient
{
    /// <summary>
    /// Exchanges an OAuth <c>code</c> for an access token via
    /// <c>POST https://github.com/login/oauth/access_token</c>. Used by the
    /// minimal-scope login flow which only needs the token transiently
    /// (it discards it after <see cref="GetUserAsync"/>).
    /// </summary>
    Task<string> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct);

    /// <summary>
    /// Same exchange as <see cref="ExchangeCodeForTokenAsync"/> but also
    /// returns the scopes GitHub stamped on the resulting token. Required by
    /// the <c>connect</c> flow (Apêndice E §E.5) which needs to validate that
    /// the user accepted the requested elevated scopes (<c>repo</c> +
    /// <c>read:org</c>) before persisting the token. GitHub returns a CSV
    /// in the <c>scope</c> field of the access-token response; this method
    /// parses it into a normalized array.
    /// </summary>
    Task<GitHubTokenGrant> ExchangeCodeForTokenWithScopesAsync(string code, string redirectUri, CancellationToken ct);

    /// <summary>
    /// Fetches the authenticated user's profile + verified primary email.
    /// Combines <c>GET /user</c> + <c>GET /user/emails</c> so the caller has
    /// everything needed to upsert a row in <c>users</c> +
    /// <c>external_logins</c>.
    /// </summary>
    Task<GitHubUserProfile> GetUserAsync(string accessToken, CancellationToken ct);
}

/// <summary>
/// Result of the <c>connect</c>-flow code exchange: token bytes plus the
/// scopes the user actually granted. The user can deselect scopes on the
/// GitHub authorize prompt, so the granted set may be a strict subset of
/// what the start endpoint requested — callers MUST verify before
/// persisting (Apêndice E §E.5).
/// </summary>
public sealed record GitHubTokenGrant(string AccessToken, string[] Scopes);

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
