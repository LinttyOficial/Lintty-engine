using System;
using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Auth;

namespace Lintty.WebInspector.Tests.Fakes;

/// <summary>
/// Test substitute for <see cref="IGitHubOAuthClient"/>. Returns a canned
/// <see cref="GitHubUserProfile"/> instead of hitting <c>github.com</c>.
/// Tests configure <see cref="Profile"/> before invoking the callback.
///
/// **Connect-flow extensions (ADR 0007 Apêndice E §E.5/§E.6, Sprint 3 PR 6):**
/// <see cref="GrantedScopes"/> + <see cref="AccessTokenForGrant"/> let the
/// connect tests script "user accepted all scopes" vs "user deselected
/// read:org" without spinning up a real OAuth dance. The legacy
/// <see cref="ExchangeCodeForTokenAsync"/> path returns
/// <see cref="AccessTokenForGrant"/> verbatim — the login tests don't care
/// about scopes so they keep working.
/// </summary>
public sealed class FakeGitHubOAuthClient : IGitHubOAuthClient
{
    public GitHubUserProfile Profile { get; set; } =
        new("12345", "octocat", "The Octocat", "octocat@example.com");

    /// <summary>
    /// Token bytes the fake hands back from both legacy and connect-flow
    /// exchanges. Tests can swap this to assert downstream encryption /
    /// masking behavior on a known plaintext.
    /// </summary>
    public string AccessTokenForGrant { get; set; } = "fake-access-token";

    /// <summary>
    /// Scopes the fake stamps on <see cref="ExchangeCodeForTokenWithScopesAsync"/>
    /// responses. Defaults to the connect-flow happy path
    /// (<c>repo</c> + <c>read:org</c>); insufficient-scopes tests flip this
    /// to e.g. <c>["read:user"]</c> to drive the 400 path.
    /// </summary>
    public string[] GrantedScopes { get; set; } = { "repo", "read:org" };

    public Task<string> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct)
        => Task.FromResult(AccessTokenForGrant);

    public Task<GitHubTokenGrant> ExchangeCodeForTokenWithScopesAsync(
        string code, string redirectUri, CancellationToken ct)
        => Task.FromResult(new GitHubTokenGrant(AccessTokenForGrant, GrantedScopes));

    public Task<GitHubUserProfile> GetUserAsync(string accessToken, CancellationToken ct)
        => Task.FromResult(Profile);
}
