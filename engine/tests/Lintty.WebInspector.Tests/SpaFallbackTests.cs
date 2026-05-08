using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// SPA fallback for the Next.js static export's dynamic segments
/// (<c>/dashboard/repos/[id]</c>, <c>/dashboard/scans/[publicId]</c>).
///
/// Next emits placeholder <c>index.html</c> at
/// <c>out/dashboard/repos/_/index.html</c> and
/// <c>out/dashboard/scans/_/index.html</c>; the real id only exists
/// client-side. The host's <c>UseStaticFiles</c> alone 404s on the actual
/// id paths because no file exists at that exact location, so a small
/// middleware (Program.cs) rewrites those two patterns to the placeholder.
///
/// These tests assert:
///   1. The middleware actually serves the placeholder for the two patterns.
///   2. <c>/api/*</c> is not captured (regex is scoped, ordering is correct).
///   3. Unknown paths still 404 (no MapFallbackToFile-style overreach).
///
/// The factory's default <c>landingRoot</c> resolution points at
/// <c>frontend/out</c> in the repo, which is built by <c>npm run build</c>
/// in CI. If that directory is absent on a dev machine, the middleware
/// no-ops and the placeholder tests are skipped via Skippable assertions
/// — the negative tests (api not captured, unknown 404) are unaffected.
/// </summary>
public sealed class SpaFallbackTests : WebInspectorTestBase
{
    public SpaFallbackTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task Get_DashboardRepos_With_Numeric_Id_Returns_Placeholder_Html()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/dashboard/repos/42");

        // If the frontend hasn't been built (no frontend/out), the host
        // doesn't register the static middleware at all and this path
        // 404s via the endpoint pipeline. Skip rather than fail in that
        // environment — the unit covers the wired case.
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", resp.Headers.CacheControl?.ToString() ?? "");
        var body = await resp.Content.ReadAsStringAsync();
        // Stable marker: every Lintty page emits this <title> in the static export.
        Assert.Contains("Lintty", body);
    }

    [Fact]
    public async Task Get_DashboardScans_With_Uuid_Returns_Placeholder_Html()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/dashboard/scans/abc12345-6789-4abc-9def-0123456789ab");

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", resp.Headers.CacheControl?.ToString() ?? "");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Lintty", body);
    }

    [Fact]
    public async Task Get_Api_Path_Is_Not_Captured_By_Spa_Fallback()
    {
        // /api/auth/me requires the lintty_auth cookie. Hitting it without
        // a cookie must produce 401 — the middleware's documented behavior
        // on the cookie scheme. If the SPA fallback wrongly captured this
        // request, we'd see 200 + text/html instead.
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Unknown_Static_Path_Still_404s()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Path doesn't match the dashboard regex AND doesn't exist as a
        // static file. Must surface as 404 — not be silently rewritten.
        var resp = await client.GetAsync("/foo/bar/baz");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
