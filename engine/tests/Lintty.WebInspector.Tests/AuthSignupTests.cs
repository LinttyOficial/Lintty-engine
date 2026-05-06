using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Sprint 2 — <c>POST /api/auth/signup</c>. Covers happy path,
/// duplicate email, weak password (Identity defaults), missing fields, and
/// slug collision.
/// </summary>
public sealed class AuthSignupTests : WebInspectorTestBase
{
    public AuthSignupTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task Signup_Happy_Path_Returns_201_With_User_And_Default_Org()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "alice@example.com",
            password = "Strong-Password-1!",
            displayName = "Alice",
            orgName = "Acme Corp",
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("alice@example.com", doc.RootElement.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Alice", doc.RootElement.GetProperty("user").GetProperty("displayName").GetString());
        var slug = doc.RootElement.GetProperty("currentOrg").GetProperty("slug").GetString();
        Assert.Equal("acme-corp", slug);
        Assert.Equal("owner", doc.RootElement.GetProperty("currentOrg").GetProperty("role").GetString());

        // Cookie issued.
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"), c => c.StartsWith("lintty_auth="));
    }

    [Fact]
    public async Task Signup_With_Duplicate_Email_Returns_409()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "bob@example.com",
            password = "Strong-Password-1!",
            displayName = "Bob",
            orgName = "Bob Inc",
        });

        var second = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "bob@example.com",
            password = "Different-Password-2!",
            displayName = "Bob 2",
            orgName = "Other Inc",
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("email_taken", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Signup_With_Weak_Password_Returns_400_With_Field_Errors()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "carol@example.com",
            password = "weak",
            displayName = "Carol",
            orgName = "Carol Co",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("error").GetString());
        // At least one error mapped to 'password'.
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("password", out _),
            "expected errors.password to be present for weak password");
    }

    [Fact]
    public async Task Signup_With_Missing_Email_Returns_400()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = (string?)null,
            password = "Strong-Password-1!",
            orgName = "Some Org",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Signup_With_Slug_Collision_Resolves_With_Suffix()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "dan1@example.com",
            password = "Strong-Password-1!",
            orgName = "Same Name",
        });
        var second = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "dan2@example.com",
            password = "Strong-Password-1!",
            orgName = "Same Name",
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var slug = doc.RootElement.GetProperty("currentOrg").GetProperty("slug").GetString();
        Assert.Equal("same-name-2", slug);
    }
}
