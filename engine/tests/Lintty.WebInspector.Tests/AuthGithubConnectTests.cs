using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Endpoints;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Apêndice E §E.5/§E.6 — Sprint 3 PR 6. Covers the elevated
/// <c>connect</c> OAuth flow that is distinct from the login flow:
/// elevated scopes (<c>repo</c> + <c>read:org</c>), encrypted-at-rest
/// token storage via <see cref="IGitHubUserTokenStore"/> + <c>IDataProtector</c>,
/// status + revoke endpoints, and the masked-log invariant for authenticated
/// clones (pendência E4).
/// </summary>
public sealed class AuthGithubConnectTests : WebInspectorTestBase
{
    public AuthGithubConnectTests(PostgresFixture pg) : base(pg) { }

    // ── /connect/start ─────────────────────────────────────────────────────

    [Fact]
    public async Task Connect_Start_Without_Cookie_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;

        var anon = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var resp = await anon.GetAsync("/api/auth/github/connect/start");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Connect_Start_Without_Configured_OAuth_Returns_503()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        // EnableGitHubOAuth left at default (false) — no client_id / secret.

        var (client, _, _) = await SignUpNonFollowingAsync(
            factory, "alice@example.com", "Alice Co");

        var resp = await client.GetAsync("/api/auth/github/connect/start");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("github_oauth_not_configured", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Connect_Start_Redirects_To_Github_With_Elevated_Scopes()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;

        var (client, _, _) = await SignUpNonFollowingAsync(
            factory, "alice@example.com", "Alice Co");
        // We need AllowAutoRedirect=false to inspect the 302; but the cookie
        // jar from the helper is keyed to its own HttpClient. Re-issue a
        // separate request using HttpClientHandler-level cookie sharing.
        var startResp = await client.GetAsync("/api/auth/github/connect/start");

        Assert.Equal(HttpStatusCode.Redirect, startResp.StatusCode);
        var location = startResp.Headers.Location?.ToString() ?? string.Empty;
        Assert.StartsWith("https://github.com/login/oauth/authorize", location, StringComparison.Ordinal);

        // scope must include both elevated scopes. We don't pin the exact
        // percent-encoding (ASP.NET Core's Results.Redirect may pass the URL
        // through Uri.AbsoluteUri which round-trips %20 → space → "+", or
        // Location header parsing may decode reserved chars). What we need
        // to assert is functional: the granted scopes are repo + read:org.
        // We re-parse the query and compare on the decoded value.
        var loc = new Uri(location);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(loc.Query);
        Assert.True(query.TryGetValue("scope", out var scopeValue),
            $"Location query missing 'scope': {location}");
        var scopes = scopeValue.ToString().Split(new[] { ' ', '+' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("repo", scopes);
        Assert.Contains("read:org", scopes);
        Assert.DoesNotContain("read:user", scopes);     // login flow scope must NOT leak here
        Assert.DoesNotContain("user:email", scopes);

        // State cookie set under the connect path — different name from the
        // login state cookie (lintty_oauth_state) so the two flows can
        // coexist in adjacent tabs.
        var stateCookie = ExtractCookie(startResp, AuthGithubConnectEndpoints.ConnectStateCookie);
        Assert.False(string.IsNullOrEmpty(stateCookie));

        // Login flow's cookie must NOT be set by the connect /start.
        Assert.Null(ExtractCookie(startResp, "lintty_oauth_state"));
    }

    // ── /connect/callback ──────────────────────────────────────────────────

    [Fact]
    public async Task Connect_Callback_With_Mismatched_State_Redirects_With_Reason()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;

        var (client, _, _) = await SignUpNonFollowingAsync(
            factory, "alice@example.com", "Alice Co");

        var startResp = await client.GetAsync("/api/auth/github/connect/start");
        var stateCookie = ExtractCookie(startResp, AuthGithubConnectEndpoints.ConnectStateCookie);
        Assert.False(string.IsNullOrEmpty(stateCookie));

        // Hit the callback with a deliberately wrong state value in the query.
        // Per PR F4 contract the callback is reached via top-level browser
        // navigation, so the response must be a 302 back to the SPA with a
        // machine-readable reason — never a 400 JSON page.
        var resp = await client.GetAsync(
            $"/api/auth/github/connect/callback?code=abc&state=NOT_THE_RIGHT_STATE");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/dashboard?github_connect=error&reason=invalid_oauth_state",
            resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Connect_Callback_With_Insufficient_Scopes_Redirects_With_Reason()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;
        // Simulate user deselecting scopes on the GitHub authorize prompt.
        factory.FakeGitHubOAuth.GrantedScopes = new[] { "read:user" };
        factory.FakeGitHubOAuth.Profile = new GitHubUserProfile(
            ProviderUserId: "9001",
            Login: "scopesy",
            Name: "Scopes McGee",
            Email: "scopes@example.com");

        var (client, _, userId) = await SignUpNonFollowingAsync(
            factory,"alice@example.com", "Alice Co");

        var (cb, _) = await DriveStartAndCallbackAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        Assert.Equal("/dashboard?github_connect=error&reason=insufficient_scopes",
            cb.Headers.Location?.ToString());

        // No token persisted on this rejection path.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        Assert.Equal(0, await db.GithubUserTokens.CountAsync(t => t.UserId == userId));
    }

    [Fact]
    public async Task Connect_Callback_Happy_Path_Persists_Encrypted_Token_And_Redirects_To_Dashboard()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;
        factory.FakeGitHubOAuth.AccessTokenForGrant = "ghs_HAPPY_PATH_TOKEN_VALUE";
        factory.FakeGitHubOAuth.GrantedScopes = new[] { "repo", "read:org" };
        factory.FakeGitHubOAuth.Profile = new GitHubUserProfile(
            ProviderUserId: "55501",
            Login: "happyuser",
            Name: "Happy User",
            Email: "happy@example.com");

        var (client, _, userId) = await SignUpNonFollowingAsync(
            factory,"happy@example.com", "Happy Co");

        var (cb, _) = await DriveStartAndCallbackAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        Assert.Equal("/dashboard?github_connect=success", cb.Headers.Location?.ToString());

        // Row exists, encrypted bytes ≠ plaintext, scopes preserved.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var row = await db.GithubUserTokens.SingleAsync(t => t.UserId == userId);
        Assert.NotEmpty(row.EncryptedToken);

        var plaintextBytes = Encoding.UTF8.GetBytes("ghs_HAPPY_PATH_TOKEN_VALUE");
        Assert.False(row.EncryptedToken.SequenceEqual(plaintextBytes),
            "encrypted_token must not equal plaintext UTF-8 bytes; DataProtection.Protect should have ciphertexted them.");
        Assert.Equal(new[] { "repo", "read:org" }, row.Scopes);
        Assert.Null(row.RevokedAt);

        // external_logins row was created (signup was via email/password, no
        // prior provider link).
        var link = await db.ExternalLogins.SingleAsync(e => e.UserId == userId);
        Assert.Equal("github", link.Provider);
        Assert.Equal("55501", link.ProviderUserId);
    }

    [Fact]
    public async Task Connect_Callback_With_Github_Identity_Mismatch_Redirects_With_Reason()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;

        var (client, _, userId) = await SignUpNonFollowingAsync(
            factory, "mismatch@example.com", "Mismatch Co");

        // Pre-seed an external_logins row pinning user → provider_user_id 12345.
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<LinttyDbContext>();
            db.ExternalLogins.Add(new ExternalLogin
            {
                UserId = userId,
                Provider = "github",
                ProviderUserId = "12345",
                Username = "alice-original",
                LinkedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // The OAuth callback returns a DIFFERENT GitHub identity (id 99999).
        factory.FakeGitHubOAuth.Profile = new GitHubUserProfile(
            ProviderUserId: "99999",
            Login: "alice-other-account",
            Name: "Alice (other)",
            Email: "alice2@example.com");

        var (cb, _) = await DriveStartAndCallbackAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        Assert.Equal("/dashboard?github_connect=error&reason=github_identity_mismatch",
            cb.Headers.Location?.ToString());

        // No token persisted; existing external_logins untouched.
        await using var scope = factory.Services.CreateAsyncScope();
        var db2 = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        Assert.Equal(0, await db2.GithubUserTokens.CountAsync(t => t.UserId == userId));
        var link = await db2.ExternalLogins.SingleAsync(e => e.UserId == userId);
        Assert.Equal("12345", link.ProviderUserId);            // unchanged
        Assert.Equal("alice-original", link.Username);
    }

    [Fact]
    public async Task Connect_Callback_With_Github_Identity_Owned_By_Another_User_Redirects_With_Reason()
    {
        // Regression: before this guard, the connect flow would attempt an
        // INSERT into external_logins for the current Lintty user even when
        // the (provider, provider_user_id) was already claimed by a different
        // user, tripping the global unique index
        // ix_external_logins_provider_provider_user_id with a Postgres 23505
        // and surfacing as a 500 stack trace to the user. The fix detects the
        // cross-user collision up-front and 302s back to the dashboard with a
        // machine-readable reason the frontend can humanize.
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;

        // User A "owns" provider_user_id=77777.
        var (clientA, _, userAId) = await SignUpNonFollowingAsync(
            factory, "owner@example.com", "Owner Co");
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<LinttyDbContext>();
            db.ExternalLogins.Add(new ExternalLogin
            {
                UserId = userAId,
                Provider = "github",
                ProviderUserId = "77777",
                Username = "owner-handle",
                LinkedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // User B authenticates as the SAME GitHub identity (77777).
        var (clientB, _, userBId) = await SignUpNonFollowingAsync(
            factory, "intruder@example.com", "Intruder Co");
        factory.FakeGitHubOAuth.Profile = new GitHubUserProfile(
            ProviderUserId: "77777",
            Login: "owner-handle",
            Name: "Owner",
            Email: "owner@example.com");

        var (cb, _) = await DriveStartAndCallbackAsync(clientB);
        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        Assert.Equal(
            "/dashboard?github_connect=error&reason=github_already_linked_to_another_account",
            cb.Headers.Location?.ToString());

        // User A's row is untouched; user B got no link and no token.
        await using var scope = factory.Services.CreateAsyncScope();
        var db2 = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        Assert.Equal(0, await db2.GithubUserTokens.CountAsync(t => t.UserId == userBId));
        Assert.Equal(0, await db2.ExternalLogins.CountAsync(e => e.UserId == userBId));
        var ownerLink = await db2.ExternalLogins.SingleAsync(e => e.UserId == userAId);
        Assert.Equal("77777", ownerLink.ProviderUserId);
        Assert.Equal("owner-handle", ownerLink.Username);
    }

    // ── GET /connect (status) ─────────────────────────────────────────────

    [Fact]
    public async Task Get_Connect_Status_When_Disconnected_Returns_Connected_False()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;

        var (client, _, _) = await SignUpNonFollowingAsync(
            factory, "alice@example.com", "Alice Co");

        var resp = await client.GetAsync("/api/auth/github/connect");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Get_Connect_Status_When_Connected_Returns_Scopes_And_Granted_At_Without_Token()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;
        factory.FakeGitHubOAuth.AccessTokenForGrant = "ghs_PRIVATE_VALUE_DO_NOT_LEAK";
        factory.FakeGitHubOAuth.GrantedScopes = new[] { "repo", "read:org" };
        factory.FakeGitHubOAuth.Profile = new GitHubUserProfile(
            ProviderUserId: "44402", Login: "stat", Name: "Stat", Email: "stat@example.com");

        var (client, _, _) = await SignUpNonFollowingAsync(
            factory,"stat@example.com", "Stat Co");
        await DriveStartAndCallbackAsync(client);

        var resp = await client.GetAsync("/api/auth/github/connect");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var bodyText = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyText);

        Assert.True(doc.RootElement.GetProperty("connected").GetBoolean());
        var scopes = doc.RootElement.GetProperty("scopes").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "repo", "read:org" }, scopes);
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("grantedAt").GetString()));

        // Crucially: response body must not carry the plaintext token, the
        // ciphertext, or any field with the substring "token".
        Assert.DoesNotContain("ghs_PRIVATE_VALUE_DO_NOT_LEAK", bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("encryptedToken", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"token\"", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    // ── DELETE /connect ────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Connect_Marks_Revoked_And_Returns_UpstreamRevokeUrl()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;
        factory.FakeGitHubOAuth.GrantedScopes = new[] { "repo", "read:org" };
        factory.FakeGitHubOAuth.Profile = new GitHubUserProfile(
            ProviderUserId: "70001", Login: "del", Name: "Del", Email: "del@example.com");

        var (client, _, userId) = await SignUpNonFollowingAsync(
            factory,"del@example.com", "Del Co");
        await DriveStartAndCallbackAsync(client);

        var del = await client.DeleteAsync("/api/auth/github/connect");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        using (var doc = JsonDocument.Parse(await del.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("revoked").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("alreadyRevoked").GetBoolean());
            Assert.Equal(AuthGithubConnectEndpoints.UpstreamRevokeUrl,
                doc.RootElement.GetProperty("upstreamRevokeUrl").GetString());
        }

        // Status now reports disconnected.
        var status = await client.GetAsync("/api/auth/github/connect");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using (var sdoc = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
        {
            Assert.False(sdoc.RootElement.GetProperty("connected").GetBoolean());
        }

        // DB row carries revoked_at set.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var row = await db.GithubUserTokens.SingleAsync(t => t.UserId == userId);
        Assert.NotNull(row.RevokedAt);
    }

    [Fact]
    public async Task Delete_Connect_Idempotent_When_Already_Revoked()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.EnableGitHubOAuth = true;

        var (client, _, _) = await SignUpNonFollowingAsync(
            factory, "alice@example.com", "Alice Co");

        // No connect ever performed → first DELETE returns alreadyRevoked=true.
        var first = await client.DeleteAsync("/api/auth/github/connect");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var doc = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.False(doc.RootElement.GetProperty("revoked").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("alreadyRevoked").GetBoolean());
            Assert.Equal(AuthGithubConnectEndpoints.UpstreamRevokeUrl,
                doc.RootElement.GetProperty("upstreamRevokeUrl").GetString());
        }

        // Second DELETE — same idempotent shape.
        var second = await client.DeleteAsync("/api/auth/github/connect");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using (var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync()))
        {
            Assert.False(doc.RootElement.GetProperty("revoked").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("alreadyRevoked").GetBoolean());
        }
    }

    // ── GitCliClient log masking (pendência E4) ────────────────────────────

    [Fact]
    public void GitCliClient_Masks_Token_In_CloneUrlForLog()
    {
        // This is a unit test on the mask helper directly, not a process
        // spawn. Spawning git in an xUnit run is flaky on CI and we already
        // exercise the live path via WorkerIntegrationTests; what we need to
        // pin down for E4 is that the log-formatting helper never emits the
        // literal token bytes.
        var coords = UrlValidator.TryParse("https://github.com/owner/repo", out var err)
            ?? throw new Xunit.Sdk.XunitException(err ?? "URL parse failed");

        const string secret = "ghs_LITERAL_PRIVATE_TOKEN_VALUE_xyz123";

        var safe = coords.CloneUrlForLog(secret);
        Assert.Contains("***", safe, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, safe, StringComparison.Ordinal);
        Assert.StartsWith("https://x-access-token:***@github.com/", safe, StringComparison.Ordinal);

        // Anonymous clone has no mask placeholder either.
        var anon = coords.CloneUrlForLog(null);
        Assert.DoesNotContain("***", anon, StringComparison.Ordinal);
        Assert.DoesNotContain("x-access-token", anon, StringComparison.Ordinal);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives the full /start → /callback dance against the supplied
    /// authenticated, non-following client and returns the callback response
    /// + the state value used. <see cref="SignUpNonFollowingAsync"/> already
    /// configured AllowAutoRedirect=false so /start returns the raw 302 with
    /// Set-Cookie intact.
    /// </summary>
    private static async Task<(HttpResponseMessage Callback, string State)> DriveStartAndCallbackAsync(
        HttpClient client)
    {
        var startResp = await client.GetAsync("/api/auth/github/connect/start");
        var state = ExtractCookie(startResp, AuthGithubConnectEndpoints.ConnectStateCookie)
            ?? throw new InvalidOperationException("Connect /start did not set the state cookie.");

        var callback = await client.GetAsync(
            $"/api/auth/github/connect/callback?code=abc&state={Uri.EscapeDataString(state)}");
        return (callback, state);
    }

    /// <summary>
    /// Variant of <see cref="WebInspectorTestBase.SignUpAndGetAuthedClientAsync"/>
    /// that uses <c>AllowAutoRedirect=false</c>. The connect flow returns 302
    /// to <c>github.com</c> on /start; following that would either hit the
    /// real internet or surface as a hostile connection error inside
    /// <c>WebApplicationFactory</c>. Non-following lets the test inspect the
    /// Location header and Set-Cookie directly.
    /// </summary>
    private static async Task<(HttpClient Client, long OrgId, long UserId)> SignUpNonFollowingAsync(
        WebInspectorFactory factory,
        string email,
        string orgName,
        string password = "Strong-Password-1!")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var resp = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            displayName = "Test User",
            orgName,
        });
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var orgId = doc.RootElement.GetProperty("currentOrg").GetProperty("orgId").GetInt64();
        var userId = doc.RootElement.GetProperty("user").GetProperty("id").GetInt64();
        return (client, orgId, userId);
    }

    private static string? ExtractCookie(HttpResponseMessage resp, string name)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        var prefix = name + "=";
        foreach (var c in cookies)
        {
            var idx = c.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0) continue;
            var rest = c[(idx + prefix.Length)..];
            var sep = rest.IndexOf(';');
            return sep < 0 ? rest : rest[..sep];
        }
        return null;
    }
}
