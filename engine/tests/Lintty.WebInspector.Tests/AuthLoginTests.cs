using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Sprint 2 — <c>POST /api/auth/login</c>. Generic 401 for both
/// "user not found" and "wrong password" so an attacker can't enumerate
/// emails. Cookie issued on success and accepted by <c>/api/auth/me</c>.
/// </summary>
public sealed class AuthLoginTests : WebInspectorTestBase
{
    public AuthLoginTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task Login_Happy_Path_Returns_200_And_Cookie_Authenticates_Me()
    {
        await ResetAsync();
        await using var factory = CreateFactory();

        // Signup happens on its own client; we then use a fresh cookie jar
        // to ensure the post-login cookie (not the signup one) is what
        // authenticates the /me call.
        var signupClient = factory.CreateClient();
        await signupClient.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "erin@example.com",
            password = "Strong-Password-1!",
            displayName = "Erin",
            orgName = "Erin Co",
        });

        var loginClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
        var login = await loginClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = "erin@example.com",
            password = "Strong-Password-1!",
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("lintty_auth="));

        // /me returns the same user when called on the same cookie-handling client.
        var me = await loginClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal("erin@example.com", doc.RootElement.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Returns_401_Generic()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "frank@example.com",
            password = "Strong-Password-1!",
            orgName = "Frank Co",
        });

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "frank@example.com",
            password = "wrong-password",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Login_With_Unknown_User_Returns_401_Same_Shape()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "ghost@example.com",
            password = "Strong-Password-1!",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Me_Without_Cookie_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
