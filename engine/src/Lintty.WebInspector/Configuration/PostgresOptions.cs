namespace Lintty.WebInspector.Configuration;

/// <summary>
/// Postgres connection configuration for the Web Inspector. Bound to the
/// <c>Postgres</c> section of <c>appsettings.json</c>; can also be supplied
/// via the env var <c>LINTTY_POSTGRES__CONNECTIONSTRING</c> (ASP.NET Core's
/// double-underscore convention for hierarchical keys).
///
/// ADR 0007 Sprint 1 moved the backing store from SQLite to Postgres. The
/// connection string is the only required field; pool sizing, timeouts and
/// SSL mode all default to Npgsql's defaults, which are sane for a single-
/// instance worker.
/// </summary>
public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    /// <summary>
    /// Standard Npgsql connection string. Example for local dev (matches the
    /// docker-compose at <c>engine/docker-compose.yml</c>):
    /// <c>Host=localhost;Port=5434;Database=lintty_dev;Username=lintty;Password=lintty_dev</c>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
