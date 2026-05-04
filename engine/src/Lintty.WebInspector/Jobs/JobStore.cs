using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lintty.WebInspector.Configuration;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Thin SQLite repository for the <c>jobs</c> and <c>rate_limits</c> tables.
/// We avoid EF Core deliberately — the schema is closed (5 columns of state
/// + 5 of metrics) and a few hand-written queries beat the runtime cost
/// and pinned version of an ORM. ADR-style note: if the schema ever grows
/// beyond ~3 tables, revisit.
///
/// Concurrency: SQLite is opened in WAL mode and the connection-string is
/// shared (one file). The worker is single-threaded by design (one job at a
/// time, see <see cref="QueueOptions.MaxLength"/>), so contention is limited
/// to API writes vs the worker's polling read.
/// </summary>
public interface IJobStore
{
    Task InitializeAsync(CancellationToken ct);

    Task InsertJobAsync(Job job, CancellationToken ct);

    Task<Job?> GetAsync(string jobId, CancellationToken ct);

    Task UpdateAsync(Job job, CancellationToken ct);

    Task<Job?> ClaimNextQueuedAsync(CancellationToken ct);

    Task<int> CountActiveAsync(CancellationToken ct);

    Task<int> IncrementRateLimitAsync(string ip, string day, CancellationToken ct);

    Task<int> GetRateLimitCountAsync(string ip, string day, CancellationToken ct);
}

public sealed class JobStore : IJobStore
{
    private readonly string _connectionString;
    private readonly ILogger<JobStore> _logger;

    public JobStore(IOptions<JobStorageOptions> options, ILogger<JobStore> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var root = Path.IsPathRooted(opts.Root)
            ? opts.Root
            : Path.Combine(Directory.GetCurrentDirectory(), opts.Root);
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, opts.DatabaseFile);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        _logger.LogInformation("JobStore using SQLite at {DbPath}", dbPath);
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);

        // PRAGMAs are per-connection but safe to set every time.
        await ExecuteAsync(conn, "PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await ExecuteAsync(conn, "PRAGMA foreign_keys=ON;", ct).ConfigureAwait(false);

        await ExecuteAsync(conn, """
            CREATE TABLE IF NOT EXISTS jobs (
              id TEXT PRIMARY KEY,
              github_url TEXT NOT NULL,
              ref TEXT,
              solution_path TEXT,
              status TEXT NOT NULL,
              stage TEXT,
              created_at TEXT NOT NULL,
              completed_at TEXT,
              expires_at TEXT,
              error_code TEXT,
              error_message TEXT,
              pdf_path TEXT,
              json_path TEXT,
              score INTEGER,
              grade TEXT,
              canon_version TEXT,
              violation_count INTEGER,
              hard_locks_open INTEGER
            );
            """, ct).ConfigureAwait(false);

        await ExecuteAsync(conn,
            "CREATE INDEX IF NOT EXISTS idx_jobs_status_created ON jobs(status, created_at);",
            ct).ConfigureAwait(false);

        await ExecuteAsync(conn, """
            CREATE TABLE IF NOT EXISTS rate_limits (
              ip TEXT NOT NULL,
              day TEXT NOT NULL,
              count INTEGER NOT NULL,
              PRIMARY KEY (ip, day)
            );
            """, ct).ConfigureAwait(false);
    }

    public async Task InsertJobAsync(Job job, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO jobs
                (id, github_url, ref, solution_path, status, stage, created_at)
            VALUES
                ($id, $url, $ref, $sln, $status, $stage, $created);
            """;
        cmd.Parameters.AddWithValue("$id", job.Id);
        cmd.Parameters.AddWithValue("$url", job.GithubUrl);
        cmd.Parameters.AddWithValue("$ref", (object?)job.Ref ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sln", (object?)job.SolutionPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", job.Status);
        cmd.Parameters.AddWithValue("$stage", (object?)job.Stage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", FormatUtc(job.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<Job?> GetAsync(string jobId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM jobs WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", jobId);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return Map(reader);
    }

    public async Task UpdateAsync(Job job, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE jobs SET
                ref            = $ref,
                solution_path  = $sln,
                status         = $status,
                stage          = $stage,
                completed_at   = $completed,
                expires_at     = $expires,
                error_code     = $err_code,
                error_message  = $err_msg,
                pdf_path       = $pdf,
                json_path      = $json,
                score          = $score,
                grade          = $grade,
                canon_version  = $canon,
                violation_count = $vcount,
                hard_locks_open = $hlocks
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", job.Id);
        cmd.Parameters.AddWithValue("$ref", (object?)job.Ref ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sln", (object?)job.SolutionPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", job.Status);
        cmd.Parameters.AddWithValue("$stage", (object?)job.Stage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$completed", job.CompletedAt is null ? DBNull.Value : FormatUtc(job.CompletedAt.Value));
        cmd.Parameters.AddWithValue("$expires", job.ExpiresAt is null ? DBNull.Value : FormatUtc(job.ExpiresAt.Value));
        cmd.Parameters.AddWithValue("$err_code", (object?)job.ErrorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$err_msg", (object?)job.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pdf", (object?)job.PdfPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$json", (object?)job.JsonPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$score", (object?)job.Score ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$grade", (object?)job.Grade ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$canon", (object?)job.CanonVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$vcount", (object?)job.ViolationCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hlocks", (object?)job.HardLocksOpen ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically picks the oldest queued job and flips it to <c>running</c>.
    /// Returns null if no queued job exists. The single-statement UPDATE+RETURNING
    /// avoids a TOCTOU race even under concurrent workers (we only have one in V0
    /// but the API itself can be hit concurrently).
    /// </summary>
    public async Task<Job?> ClaimNextQueuedAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using var pickCmd = conn.CreateCommand();
        pickCmd.Transaction = tx;
        pickCmd.CommandText = "SELECT id FROM jobs WHERE status = 'queued' ORDER BY created_at LIMIT 1;";
        var idObj = await pickCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (idObj is null || idObj is DBNull) { await tx.CommitAsync(ct).ConfigureAwait(false); return null; }
        var id = (string)idObj;

        await using var flipCmd = conn.CreateCommand();
        flipCmd.Transaction = tx;
        flipCmd.CommandText = "UPDATE jobs SET status = 'running', stage = 'cloning' WHERE id = $id AND status = 'queued';";
        flipCmd.Parameters.AddWithValue("$id", id);
        var rows = await flipCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (rows == 0) { await tx.CommitAsync(ct).ConfigureAwait(false); return null; }

        await using var loadCmd = conn.CreateCommand();
        loadCmd.Transaction = tx;
        loadCmd.CommandText = "SELECT * FROM jobs WHERE id = $id;";
        loadCmd.Parameters.AddWithValue("$id", id);
        await using var reader = await loadCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        var job = Map(reader);
        await reader.CloseAsync().ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return job;
    }

    public async Task<int> CountActiveAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM jobs WHERE status IN ('queued', 'running');";
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    public async Task<int> IncrementRateLimitAsync(string ip, string day, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rate_limits (ip, day, count) VALUES ($ip, $day, 1)
              ON CONFLICT(ip, day) DO UPDATE SET count = count + 1;
            SELECT count FROM rate_limits WHERE ip = $ip AND day = $day;
            """;
        cmd.Parameters.AddWithValue("$ip", ip);
        cmd.Parameters.AddWithValue("$day", day);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetRateLimitCountAsync(string ip, string day, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count FROM rate_limits WHERE ip = $ip AND day = $day;";
        cmd.Parameters.AddWithValue("$ip", ip);
        cmd.Parameters.AddWithValue("$day", day);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null || v is DBNull ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static Job Map(SqliteDataReader reader)
    {
        // Walk by column ordinals once instead of named lookups in a loop.
        var job = new Job
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            GithubUrl = reader.GetString(reader.GetOrdinal("github_url")),
            CreatedAt = ParseUtc(reader.GetString(reader.GetOrdinal("created_at"))),
            Status = reader.GetString(reader.GetOrdinal("status")),
        };
        job.Ref = ReadNullableString(reader, "ref");
        job.SolutionPath = ReadNullableString(reader, "solution_path");
        job.Stage = ReadNullableString(reader, "stage");
        var completed = ReadNullableString(reader, "completed_at");
        job.CompletedAt = completed is null ? null : ParseUtc(completed);
        var expires = ReadNullableString(reader, "expires_at");
        job.ExpiresAt = expires is null ? null : ParseUtc(expires);
        job.ErrorCode = ReadNullableString(reader, "error_code");
        job.ErrorMessage = ReadNullableString(reader, "error_message");
        job.PdfPath = ReadNullableString(reader, "pdf_path");
        job.JsonPath = ReadNullableString(reader, "json_path");
        job.Score = ReadNullableInt(reader, "score");
        job.Grade = ReadNullableString(reader, "grade");
        job.CanonVersion = ReadNullableString(reader, "canon_version");
        job.ViolationCount = ReadNullableInt(reader, "violation_count");
        job.HardLocksOpen = ReadNullableInt(reader, "hard_locks_open");
        return job;
    }

    private static string? ReadNullableString(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static int? ReadNullableInt(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }

    private static string FormatUtc(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    private static DateTime ParseUtc(string iso)
        => DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
