using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Persistence;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Sprint 2 — OAuth GitHub callback. Asserts that:
/// <list type="bullet">
///   <item><description>Without GitHub creds configured, <c>/start</c> returns 503 (not crash).</description></item>
///   <item><description>First-time login creates user + external_login + default org + owner membership.</description></item>
///   <item><description>Second login with the same provider/provider_user_id reuses the existing user (no duplicate rows).</description></item>
/// </list>
/// </summary>
public sealed class OAuthCallbackTests : WebInspectorTestBase
{
    public OAuthCallbackTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task GitHubStart_Without_Creds_Returns_503()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        // EnableGitHubOAuth is false by default → no creds → endpoint returns 503.
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var resp = await client.GetAsync("/api/auth/github/start");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task GitHubCallback_First_Login_Creates_User_External_Login_And_Default_Org()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;
        factory.FakeGitHubOAuth.Profile = new GitHubUserProfile(
            ProviderUserId: "55555",
            Login: "newuser",
            Name: "New User",
            Email: "newuser@example.com");

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        // Hit /start first to set the state cookie. Follow the redirect URL
        // pattern to extract state.
        var startResp = await client.GetAsync("/api/auth/github/start");
        Assert.Equal(HttpStatusCode.Redirect, startResp.StatusCode);
        var stateCookie = ExtractStateCookie(startResp);
        Assert.False(string.IsNullOrEmpty(stateCookie));

        var callback = await client.GetAsync(
            $"/api/auth/github/callback?code=abc&state={Uri.EscapeDataString(stateCookie!)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/dashboard.html", callback.Headers.Location?.ToString());

        // Verify rows in the DB.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == "newuser@example.com");
        var link = await db.ExternalLogins.SingleAsync(e => e.UserId == user.Id);
        Assert.Equal("github", link.Provider);
        Assert.Equal("55555", link.ProviderUserId);
        Assert.Equal("newuser", link.Username);

        var membership = await db.OrgMembers.SingleAsync(m => m.UserId == user.Id);
        Assert.Equal("owner", membership.Role);
        var org = await db.Orgs.SingleAsync(o => o.Id == membership.OrgId);
        Assert.Equal("newuser", org.Slug);
    }

    [Fact]
    public async Task GitHubCallback_Subsequent_Login_Reuses_Existing_User()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;
        factory.FakeGitHubOAuth.Profile = new GitHubUserProfile(
            ProviderUserId: "77777",
            Login: "twiceuser",
            Name: "Twice",
            Email: "twice@example.com");

        // First login.
        var c1 = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var s1 = await c1.GetAsync("/api/auth/github/start");
        var st1 = ExtractStateCookie(s1)!;
        await c1.GetAsync($"/api/auth/github/callback?code=abc&state={Uri.EscapeDataString(st1)}");

        // Second login — fresh cookie jar (different client) but same profile.
        var c2 = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var s2 = await c2.GetAsync("/api/auth/github/start");
        var st2 = ExtractStateCookie(s2)!;
        var cb2 = await c2.GetAsync($"/api/auth/github/callback?code=def&state={Uri.EscapeDataString(st2)}");
        Assert.Equal(HttpStatusCode.Redirect, cb2.StatusCode);

        // Single user, single external_login, single org, single membership.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "twice@example.com"));
        Assert.Equal(1, await db.ExternalLogins.CountAsync(e => e.ProviderUserId == "77777"));
        var userId = (await db.Users.SingleAsync(u => u.Email == "twice@example.com")).Id;
        Assert.Equal(1, await db.OrgMembers.CountAsync(m => m.UserId == userId));
    }

    private static string? ExtractStateCookie(HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var c in cookies)
        {
            const string prefix = "lintty_oauth_state=";
            var idx = c.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0) continue;
            var rest = c[(idx + prefix.Length)..];
            var sep = rest.IndexOf(';');
            return sep < 0 ? rest : rest[..sep];
        }
        return null;
    }
}
