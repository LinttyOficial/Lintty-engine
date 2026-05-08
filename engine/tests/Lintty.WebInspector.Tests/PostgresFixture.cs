using System.Threading.Tasks;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Lintty.WebInspector.Persistence;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// xUnit collection fixture that boots a single Postgres 16 container shared
/// by every test in the assembly. ADR 0007 Sprint 1 swapped SQLite for
/// Postgres; Sprint 2 moved schema management from raw SQL bootstrap to EF
/// Core migrations. The fixture now applies <c>InitialIdentityAndTenant</c>
/// once at container start, then truncates all domain tables between tests.
///
///   * Tests are deterministic — schema starts identical for every assembly run.
///   * Tests are parallelizable safely — the WebInspector test collection
///     serializes its own access to the container via TRUNCATE between tests
///     (see <see cref="ResetAsync"/>); concurrent collections in the same
///     assembly would conflict, so we keep them in one named collection
///     (<see cref="PostgresCollection"/>).
///   * No external Docker Compose stack required to run tests; only the
///     Docker daemon itself.
///
/// The container starts once (~3-5s on a cold pull) and persists for the
/// entire test run.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("lintty_test")
        .WithUsername("lintty")
        .WithPassword("lintty_test")
        // tmpfs makes table truncation between tests fast and avoids host I/O
        // entirely. Postgres durability inside an ephemeral container is moot.
        .WithTmpfsMount("/var/lib/postgresql/data")
        .Build();

    /// <summary>Connection string usable by <c>WebInspectorFactory</c>.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        ConnectionString = _container.GetConnectionString();
        await ApplyMigrationsAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Applies pending EF migrations to the test container. Sprint 2 moved
    /// schema management to EF, so the fixture stops looking for an embedded
    /// SQL bootstrap and instead asks <see cref="LinttyDbContext"/> to migrate.
    /// </summary>
    private async Task ApplyMigrationsAsync()
    {
        var options = new DbContextOptionsBuilder<LinttyDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new LinttyDbContext(options);
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Wipes domain tables so successive tests start from a clean slate
    /// without paying the cost of restarting the container. CASCADE handles
    /// FKs (org_members → orgs, etc.). The Identity tables are reset too so
    /// the Auth tests can re-seed users every <c>[Fact]</c>.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await conn.ExecuteAsync(@"
            DO $$
            DECLARE
                tbl text;
            BEGIN
                FOR tbl IN
                    SELECT tablename FROM pg_tables
                    WHERE schemaname = 'public'
                      AND tablename IN (
                        'jobs', 'rate_limits',
                        -- Sprint 3 (PR 1) — multi-tenant scan ownership +
                        -- Apêndice E (OAuth user-token elevation). Listed
                        -- here even though some tables are not exercised by
                        -- PR 3 yet, so PR 4–7 inherit a clean reset path.
                        'scans', 'repos', 'github_orgs', 'github_user_tokens',
                        'org_members', 'external_logins', 'orgs',
                        'user_tokens', 'user_logins', 'user_claims', 'user_roles',
                        'role_claims', 'users', 'roles'
                      )
                LOOP
                    EXECUTE 'TRUNCATE TABLE ' || quote_ident(tbl) || ' RESTART IDENTITY CASCADE';
                END LOOP;
            END $$;
        ").ConfigureAwait(false);
    }
}

/// <summary>
/// xUnit collection definition that pins every test class needing Postgres
/// into a single collection. This serializes class-level execution so the
/// shared container's schema/data state is consistent — at the cost of
/// losing class-level parallelism, which the V0 suite never relied on
/// (filesystem fixtures and the engine subprocess already serialize the
/// integration tests).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
