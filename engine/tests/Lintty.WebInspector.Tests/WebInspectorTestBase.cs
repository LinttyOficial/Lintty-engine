using System.Threading.Tasks;
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
}
