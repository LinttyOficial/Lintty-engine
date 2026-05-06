using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// Liveness probe — useful for the eventual deploy (Cloud Run / VM) and as a
/// smoke test that the host actually boots with our DI graph intact.
/// </summary>
public sealed class HealthEndpointTests : WebInspectorTestBase
{
    public HealthEndpointTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task Healthz_Returns_Ok_With_Status_Ok()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }
}
