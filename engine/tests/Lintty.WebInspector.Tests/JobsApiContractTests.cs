using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Lintty.WebInspector.Jobs;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// Contract tests for <c>POST /api/jobs</c> and <c>GET /api/jobs/{id}</c>.
/// The worker is disabled in the factory so jobs stay in <c>queued</c> long
/// enough for assertions. Network is replaced by <see cref="Fakes.FakeGitHubMetadataClient"/>.
/// </summary>
public sealed class JobsApiContractTests : WebInspectorTestBase
{
    public JobsApiContractTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task Post_Jobs_With_Bad_Host_Returns_400()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://gitlab.com/some/project",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("invalid_url", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_Jobs_With_Empty_Body_Returns_400()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync<object?>("/api/jobs", null);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_Jobs_Happy_Path_Returns_202_With_Valid_Ulid()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/lintty-demo/the-saint",
        });

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var jobId = doc.RootElement.GetProperty("job_id").GetString();
        Assert.False(string.IsNullOrEmpty(jobId));
        Assert.True(Ulid.IsValid(jobId!), $"job_id is not a valid ULID: {jobId}");
        Assert.Equal("queued", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal($"/api/jobs/{jobId}", doc.RootElement.GetProperty("poll_url").GetString());
    }

    [Fact]
    public async Task Post_Jobs_With_404_From_Github_Returns_400()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHub.Exists = false;
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/owner/missing",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_Jobs_With_Oversize_Repo_Returns_400()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        // 600 MB > 500 MB cap.
        factory.FakeGitHub.SizeKilobytes = 600L * 1024L;
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/owner/giant",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("repo_too_large", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_Job_With_Unknown_Id_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/jobs/01HKRZQ8M3X9ABCDEFGHJKMNPQ");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Job_With_Malformed_Id_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/jobs/not-a-ulid");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Rate_Limit_Triggers_429_On_Fourth_Post_From_Same_Ip()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Default cap is 3 per IP per day. The 4th must be rejected.
        for (var i = 0; i < 3; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/jobs", new
            {
                github_url = "https://github.com/lintty-demo/the-saint",
            });
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/lintty-demo/the-saint",
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"),
            "429 response must carry Retry-After per HTTP semantics.");
    }
}
