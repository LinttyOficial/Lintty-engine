using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Auth;

namespace Lintty.WebInspector.Tests.Fakes;

/// <summary>
/// Test substitute for <see cref="IGitHubOAuthClient"/>. Returns a canned
/// <see cref="GitHubUserProfile"/> instead of hitting <c>github.com</c>.
/// Tests configure <see cref="Profile"/> before invoking the callback.
/// </summary>
public sealed class FakeGitHubOAuthClient : IGitHubOAuthClient
{
    public GitHubUserProfile Profile { get; set; } =
        new("12345", "octocat", "The Octocat", "octocat@example.com");

    public Task<string> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct)
        => Task.FromResult("fake-access-token");

    public Task<GitHubUserProfile> GetUserAsync(string accessToken, CancellationToken ct)
        => Task.FromResult(Profile);
}
