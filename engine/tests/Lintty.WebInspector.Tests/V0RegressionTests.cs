using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Sprint 2 — V0 anonymous flow regression. Sprint 2 introduced
/// auth + tenant middleware on top of the V0 endpoints; this class asserts
/// that none of those additions changed the anonymous path observable
/// behavior. The cross-determinism gate
/// (<see cref="WorkerIntegrationTests"/>) is the heavyweight equivalent of
/// these checks; the tests here are fast contract assertions that catch
/// regressions before the worker even runs.
/// </summary>
public sealed class V0RegressionTests : WebInspectorTestBase
{
    public V0RegressionTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task PostJobs_Anonymous_Returns_202_With_No_Auth_Cookie()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/lintty-demo/the-saint",
        });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

        // The POST itself must not set lintty_auth — anonymous flow stays anonymous.
        if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var c in cookies)
                Assert.False(c.StartsWith("lintty_auth="),
                    "Anonymous POST /api/jobs unexpectedly issued an auth cookie.");
        }
    }

    [Fact]
    public async Task GetJob_By_PathToken_Works_Without_Auth()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var post = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/lintty-demo/the-saint",
        });
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        using var doc = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        var jobId = doc.RootElement.GetProperty("job_id").GetString();
        Assert.False(string.IsNullOrEmpty(jobId));

        // GET with the same client (no cookie issued anyway, but explicit anyway).
        var get = await client.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var statusDoc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal("queued", statusDoc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Healthz_Still_Anonymous()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
