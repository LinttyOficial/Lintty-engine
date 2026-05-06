using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Lintty.WebInspector.Configuration;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Postgres-backed implementation of <see cref="IJobStore"/>. Replaces the
/// SQLite store as part of ADR 0007 Sprint 1 (clean cut: no dual write, no
/// fallback). Hand-written SQL via Dapper instead of EF Core because:
///
///   * The surface is small (8 methods) and stable — the cost of EF query
///     translation isn't justified.
///   * <see cref="ClaimNextQueuedAsync"/> needs a single-statement
///     <c>UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING *</c>
///     to be race-safe across multiple worker instances. Expressing that in
///     EF requires <c>FromSqlRaw</c> anyway, so we skip the layer.
///   * Sprint 2 (Identity) will introduce <c>LinttyDbContext</c> on the same
///     database for the auth schema. EF and Npgsql/Dapper coexisting on one
///     Postgres connection pool is the canonical .NET pattern: EF for
///     opinionated/schema-managed tables, Dapper for hand-tuned domain queries.
/// </summary>
public sealed class PostgresJobStore : IJobStore
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresJobStore> _logger;

    public PostgresJobStore(IOptions<PostgresOptions> options, ILogger<PostgresJobStore> logger)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            throw new InvalidOperationException(
                "Postgres connection string is required. Set 'Postgres:ConnectionString' in " +
                "appsettings.json or the env var LINTTY_POSTGRES__CONNECTIONSTRING. " +
                "For local dev: docker compose -f engine/docker-compose.yml up -d postgres.");
        }
        _connectionString = opts.ConnectionString;
        _logger = logger;
    }

    /// <summary>
    /// No-op since ADR 0007 Sprint 2: schema (jobs, rate_limits + Identity +
    /// tenant tables) is now owned by the EF Core migration
    /// <c>InitialIdentityAndTenant</c>. The host calls
    /// <see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync"/>
    /// at startup (dev) or operators run <c>dotnet ef database update</c>
    /// (prod) before booting the app. Method kept on the interface so the
    /// contract surface stays stable across the cut and any other store impl
    /// (in-memory tests, future) can still hook bootstrap there.
    /// </summary>
    public Task InitializeAsync(CancellationToken ct)
    {
        _logger.LogDebug(
            "PostgresJobStore.InitializeAsync is a no-op — schema is owned by the EF migration since ADR 0007 Sprint 2.");
        return Task.CompletedTask;
    }

    public async Task InsertJobAsync(Job job, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO jobs
                (id, github_url, ref, solution_path, status, stage, created_at)
            VALUES
                (@Id, @GithubUrl, @Ref, @SolutionPath, @Status, @Stage, @CreatedAtUtc);
        ";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            job.Id,
            job.GithubUrl,
            job.Ref,
            job.SolutionPath,
            job.Status,
            job.Stage,
            CreatedAtUtc = ToUtc(job.CreatedAt),
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<Job?> GetAsync(string jobId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        return await QuerySingleJobAsync(conn, "SELECT * FROM jobs WHERE id = @id LIMIT 1;",
            new { id = jobId }, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Job job, CancellationToken ct)
    {
        const string sql = @"
            UPDATE jobs SET
                ref             = @Ref,
                solution_path   = @SolutionPath,
                status          = @Status,
                stage           = @Stage,
                completed_at    = @CompletedAtUtc,
                expires_at      = @ExpiresAtUtc,
                error_code      = @ErrorCode,
                error_message   = @ErrorMessage,
                pdf_path        = @PdfPath,
                json_path       = @JsonPath,
                score           = @Score,
                grade           = @Grade,
                canon_version   = @CanonVersion,
                violation_count = @ViolationCount,
                hard_locks_open = @HardLocksOpen
            WHERE id = @Id;
        ";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            job.Id,
            job.Ref,
            job.SolutionPath,
            job.Status,
            job.Stage,
            CompletedAtUtc = ToUtcOrNull(job.CompletedAt),
            ExpiresAtUtc = ToUtcOrNull(job.ExpiresAt),
            job.ErrorCode,
            job.ErrorMessage,
            job.PdfPath,
            job.JsonPath,
            job.Score,
            job.Grade,
            job.CanonVersion,
            job.ViolationCount,
            job.HardLocksOpen,
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically picks the oldest queued job and flips it to <c>running</c>.
    /// Uses <c>FOR UPDATE SKIP LOCKED</c> so two workers polling concurrently
    /// never claim the same row (Postgres-specific; SQLite version relied on
    /// a serial transaction). Returns null when no queued job exists.
    /// </summary>
    public async Task<Job?> ClaimNextQueuedAsync(CancellationToken ct)
    {
        const string sql = @"
            UPDATE jobs SET
                status = 'running',
                stage  = 'cloning'
            WHERE id = (
                SELECT id FROM jobs
                WHERE status = 'queued'
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            RETURNING *;
        ";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        return await QuerySingleJobAsync(conn, sql, parameters: null, ct).ConfigureAwait(false);
    }

    public async Task<int> CountActiveAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM jobs WHERE status IN ('queued', 'running');",
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> IncrementRateLimitAsync(string ip, string day, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO rate_limits (ip, day, count) VALUES (@ip, @day, 1)
              ON CONFLICT (ip, day) DO UPDATE SET count = rate_limits.count + 1
            RETURNING count;
        ";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { ip, day },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> GetRateLimitCountAsync(string ip, string day, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        var v = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT count FROM rate_limits WHERE ip = @ip AND day = @day;",
            new { ip, day }, cancellationToken: ct)).ConfigureAwait(false);
        return v ?? 0;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    private static async Task<Job?> QuerySingleJobAsync(
        NpgsqlConnection conn, string sql, object? parameters, CancellationToken ct)
    {
        var row = await conn.QuerySingleOrDefaultAsync<JobRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    private static Job Map(JobRow r) => new()
    {
        Id = r.id,
        GithubUrl = r.github_url,
        Ref = r.@ref,
        SolutionPath = r.solution_path,
        Status = r.status,
        Stage = r.stage,
        CreatedAt = ToDateTimeUtc(r.created_at),
        CompletedAt = ToDateTimeUtcOrNull(r.completed_at),
        ExpiresAt = ToDateTimeUtcOrNull(r.expires_at),
        ErrorCode = r.error_code,
        ErrorMessage = r.error_message,
        PdfPath = r.pdf_path,
        JsonPath = r.json_path,
        Score = r.score,
        Grade = r.grade,
        CanonVersion = r.canon_version,
        ViolationCount = r.violation_count,
        HardLocksOpen = r.hard_locks_open,
    };

    /// <summary>
    /// Npgsql binds <c>timestamptz</c> to <see cref="DateTime"/> with
    /// <see cref="DateTimeKind.Utc"/>. We assert that explicitly so any drift
    /// (timezone-aware row arriving as Local) blows up at the boundary instead
    /// of leaking into the Job DTO.
    /// </summary>
    private static DateTime ToDateTimeUtc(DateTime v)
        => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.ToUniversalTime(), DateTimeKind.Utc);

    private static DateTime? ToDateTimeUtcOrNull(DateTime? v)
        => v.HasValue ? ToDateTimeUtc(v.Value) : null;

    private static DateTime ToUtc(DateTime v)
        => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();

    private static DateTime? ToUtcOrNull(DateTime? v)
        => v.HasValue ? ToUtc(v.Value) : null;

    /// <summary>
    /// Dapper row shape — columns named to match Postgres conventions
    /// (snake_case). Field-style intentionally, to match Dapper's binding by
    /// name. Suppression of the naming warnings is local to this private type.
    /// </summary>
#pragma warning disable IDE1006, CA1707, SA1300 // snake_case maps to PG columns
    private sealed class JobRow
    {
        public string id { get; set; } = string.Empty;
        public string github_url { get; set; } = string.Empty;
        public string? @ref { get; set; }
        public string? solution_path { get; set; }
        public string status { get; set; } = string.Empty;
        public string? stage { get; set; }
        public DateTime created_at { get; set; }
        public DateTime? completed_at { get; set; }
        public DateTime? expires_at { get; set; }
        public string? error_code { get; set; }
        public string? error_message { get; set; }
        public string? pdf_path { get; set; }
        public string? json_path { get; set; }
        public int? score { get; set; }
        public string? grade { get; set; }
        public string? canon_version { get; set; }
        public int? violation_count { get; set; }
        public int? hard_locks_open { get; set; }
    }
#pragma warning restore IDE1006, CA1707, SA1300

    // CultureInfo.Invariant guarded for any int-to-string formatting; reads are
    // always typed (Dapper handles primitives natively).
    private static readonly CultureInfo _invariant = CultureInfo.InvariantCulture;
}
