using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Sprint 2 — <see cref="Lintty.WebInspector.Auth.TenantContextMiddleware"/>.
/// Anonymous requests stay anonymous; authenticated cookies anchor a user to
/// their default org. Cross-org access returns 404 (per §3.5: 404, not 403,
/// to avoid resource enumeration). The dashboard endpoints that exercise the
/// 404 path land in Sprint 3 — here we assert the V0 contract is preserved
/// and the middleware itself does not reject anonymous traffic.
/// </summary>
public sealed class TenantContextMiddlewareTests : WebInspectorTestBase
{
    public TenantContextMiddlewareTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task Anonymous_Hitting_PostJobs_Still_Returns_202()
    {
        // The middleware must NOT block anonymous requests. POST /api/jobs
        // remains the V0 anonymous flow.
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/lintty-demo/the-saint",
        });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }

    [Fact]
    public async Task Authenticated_User_Reads_Me_With_Org_Context_Populated()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "tenant@example.com",
            password = "Strong-Password-1!",
            displayName = "Tenant User",
            orgName = "Tenant Org",
        });

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("currentOrg", out var current));
        Assert.NotEqual(JsonValueKind.Null, current.ValueKind);
        Assert.Equal("tenant-org", current.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Logout_Clears_Cookie_And_Subsequent_Me_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = "logout@example.com",
            password = "Strong-Password-1!",
            orgName = "LO Co",
        });

        var meBefore = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meBefore.StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var meAfter = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }
}
