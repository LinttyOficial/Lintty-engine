using AspNet.Security.OAuth.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Lintty.WebInspector.Configuration;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// Lazily binds <see cref="GitHubAuthenticationOptions"/> from
/// <see cref="IConfiguration"/> so test fixtures and env vars win over
/// <c>appsettings.json</c> without forcing reads at <c>ConfigureServices</c>
/// time. The handler is registered unconditionally with placeholder creds in
/// <c>Program</c>; this post-configure pass overwrites them with the real
/// values resolved from configuration.
///
/// When the configuration has no <c>Github:ClientId</c>/<c>ClientSecret</c>
/// set, the placeholder values stay and any actual challenge/callback would
/// fail at GitHub's end — but neither the start nor the callback in
/// <see cref="Lintty.WebInspector.Endpoints.AuthEndpoints"/> ever invokes
/// the framework handler; they short-circuit on missing creds and emit a 503.
/// </summary>
public sealed class GitHubOAuthOptionsBinder : IPostConfigureOptions<GitHubAuthenticationOptions>
{
    private readonly IConfiguration _cfg;

    public GitHubOAuthOptionsBinder(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    public void PostConfigure(string? name, GitHubAuthenticationOptions options)
    {
        var section = _cfg.GetSection(GitHubOAuthOptions.SectionName);
        var clientId = section["ClientId"];
        var clientSecret = section["ClientSecret"];
        if (!string.IsNullOrWhiteSpace(clientId)) options.ClientId = clientId!;
        if (!string.IsNullOrWhiteSpace(clientSecret)) options.ClientSecret = clientSecret!;
    }
}
