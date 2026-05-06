using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lintty.WebInspector.Persistence;

/// <summary>
/// Used by <c>dotnet ef migrations add</c> / <c>dotnet ef database update</c>
/// to instantiate <see cref="LinttyDbContext"/> at design time without booting
/// the full ASP.NET host. Connection string comes from the env var
/// <c>LINTTY_POSTGRES__CONNECTIONSTRING</c> if set, otherwise falls back to
/// the local docker-compose default. The chosen string is irrelevant for
/// <c>migrations add</c> (no DB is touched), but must be valid for
/// <c>database update</c>.
/// </summary>
public sealed class DesignTimeLinttyDbContextFactory : IDesignTimeDbContextFactory<LinttyDbContext>
{
    public LinttyDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("LINTTY_POSTGRES__CONNECTIONSTRING")
            ?? "Host=localhost;Port=5433;Database=lintty_dev;Username=lintty;Password=lintty_dev";

        var options = new DbContextOptionsBuilder<LinttyDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new LinttyDbContext(options);
    }
}
