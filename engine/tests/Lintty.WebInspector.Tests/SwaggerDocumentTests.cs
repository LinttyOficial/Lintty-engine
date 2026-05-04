using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// Smoke tests for the OpenAPI document. Not exhaustive — they only assert
/// that Swagger is wired up, the document name and version match the spec
/// (<c>v0</c>, "Lintty Web Inspector API"), and every documented route from
/// <c>docs/13-web-inspector.md</c> §5 is present in the <c>paths</c> dict.
///
/// Anything richer (per-operation parameter shape, response schemas) is
/// already covered by the contract tests; this is just a guard against
/// accidentally breaking the Swashbuckle wiring or renaming the doc.
/// </summary>
public sealed class SwaggerDocumentTests
{
    private const string SwaggerJsonPath = "/swagger/v0/swagger.json";

    [Fact]
    public async Task Swagger_Json_Has_Expected_Info_Title_And_Version()
    {
        await using var factory = new WebInspectorFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(SwaggerJsonPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Swashbuckle 6.x uses "application/json; charset=utf-8".
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var info = doc.RootElement.GetProperty("info");
        Assert.Equal("Lintty Web Inspector API", info.GetProperty("title").GetString());
        Assert.Equal("v0", info.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Swagger_Json_Lists_All_Documented_Paths()
    {
        await using var factory = new WebInspectorFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(SwaggerJsonPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        // Collect all top-level path keys.
        var pathKeys = paths.EnumerateObject().Select(p => p.Name).ToHashSet();

        // §5 of the spec — every one of these must show up in the OpenAPI doc.
        // The exact key for the POST is "/api/jobs" (the leading "/" route on
        // the group). Swashbuckle normalises trailing slashes for us.
        string[] expected =
        {
            "/healthz",
            "/api/jobs",
            "/api/jobs/{jobId}",
            "/api/jobs/{jobId}/laudo.pdf",
            "/api/jobs/{jobId}/report.json",
        };

        foreach (var key in expected)
        {
            Assert.True(pathKeys.Contains(key),
                $"OpenAPI document is missing path '{key}'. Got: [{string.Join(", ", pathKeys)}]");
        }

        Assert.True(pathKeys.Count >= 5,
            $"Expected at least 5 paths in the OpenAPI doc, got {pathKeys.Count}.");
    }
}
