using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// Base class for every WebInspector test that needs a live Postgres. Pins
/// the collection (<see cref="PostgresCollection.Name"/>) so the shared
/// <see cref="PostgresFixture"/> container is reused across the assembly,
/// and provides <see cref="CreateFactory"/> + <see cref="ResetAsync"/>
/// helpers so test methods don't have to re-do plumbing.
///
/// Tests that previously just `new WebInspectorFactory()` now do:
///
///   <code>
///   public sealed class MyTests : WebInspectorTestBase
///   {
///       public MyTests(PostgresFixture pg) : base(pg) { }
///
///       [Fact]
///       public async Task Foo() {
///           await ResetAsync();
///           await using var factory = CreateFactory();
///           ...
///       }
///   }
///   </code>
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class WebInspectorTestBase
{
    private readonly PostgresFixture _pg;

    protected WebInspectorTestBase(PostgresFixture pg)
    {
        _pg = pg;
    }

    /// <summary>
    /// Truncates the domain tables in the shared Postgres so the next test
    /// starts from a clean slate. Idempotent; safe to call from any test.
    /// </summary>
    protected Task ResetAsync() => _pg.ResetAsync();

    /// <summary>
    /// Creates a fresh <see cref="WebInspectorFactory"/> bound to the shared
    /// Postgres. Each call returns a new factory (and therefore a new
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> host) — disposing it
    /// also cleans up the per-factory artifact scratch root.
    /// </summary>
    protected WebInspectorFactory CreateFactory() => new(_pg.ConnectionString);

    /// <summary>
    /// Convenience for the dashboard test suites: spin up a <see cref="HttpClient"/>
    /// that signs up a fresh user (creating its default org) and keeps the
    /// <c>lintty_auth</c> cookie around for follow-up calls. Returns the
    /// authenticated client + the org id so cross-tenant tests can mix two
    /// signups on the same factory and assert on the second org's data.
    ///
    /// Email collision is the caller's problem — pass distinct emails per
    /// signup within a single <see cref="ResetAsync"/> window.
    /// </summary>
    protected static async Task<(HttpClient Client, long OrgId, long UserId)> SignUpAndGetAuthedClientAsync(
        WebInspectorFactory factory,
        string email,
        string orgName,
        string password = "Strong-Password-1!",
        string? displayName = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        var resp = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            displayName = displayName ?? "Test User",
            orgName,
        });
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var orgId = doc.RootElement.GetProperty("currentOrg").GetProperty("orgId").GetInt64();
        var userId = doc.RootElement.GetProperty("user").GetProperty("id").GetInt64();
        return (client, orgId, userId);
    }
}
