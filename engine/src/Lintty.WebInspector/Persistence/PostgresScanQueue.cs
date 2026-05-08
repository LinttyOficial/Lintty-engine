using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Lintty.WebInspector.Configuration;

namespace Lintty.WebInspector.Persistence;

/// <summary>
/// Dapper-backed <see cref="IScanQueue"/> against the Postgres <c>scans</c>
/// table. Mirrors <see cref="Lintty.WebInspector.Jobs.PostgresJobStore"/> in
/// shape and conventions (Npgsql + Dapper, manual UTC normalisation, hand-
/// written SQL). ADR 0007 §3.4 + §3.7. Sprint 3 PR 4.
/// </summary>
public sealed class PostgresScanQueue : IScanQueue
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresScanQueue> _logger;

    public PostgresScanQueue(IOptions<PostgresOptions> options, ILogger<PostgresScanQueue> logger)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            throw new InvalidOperationException(
                "Postgres connection string is required. Set 'Postgres:ConnectionString' in " +
                "appsettings.json or the env var LINTTY_POSTGRES__CONNECTIONSTRING.");
        }
        _connectionString = opts.ConnectionString;
        _logger = logger;
    }

    /// <summary>
    /// Single-round-trip claim: pick the oldest queued row in <c>scans</c>,
    /// flip <c>status='running'</c> + <c>started_at = now()</c>, and
    /// project the columns the worker needs (joining <c>repos</c> for the
    /// canonical <c>github_url</c> and <c>default_branch</c>).
    ///
    /// <para>
    /// <b>Race safety:</b> <c>FOR UPDATE SKIP LOCKED</c> means two workers
    /// polling concurrently never claim the same row. <c>WHERE</c> nesting
    /// is required because Postgres doesn't allow <c>FOR UPDATE</c> on the
    /// outer <c>UPDATE … RETURNING</c> directly.
    /// </para>
    ///
    /// <para>
    /// <b>Why not include the join in the inner SELECT.</b> The inner
    /// query's job is to pick exactly one queued <c>scans.id</c>; pulling
    /// in <c>repos</c> there would force the lock acquisition through a
    /// join planner that may not honour <c>SKIP LOCKED</c> the same way.
    /// We re-join on the outer <c>RETURNING</c> via a CTE — clearer and
    /// stays on documented Postgres semantics.
    /// </para>
    /// </summary>
    public async Task<ClaimedScan?> ClaimNextQueuedAsync(CancellationToken ct)
    {
        // CTE pattern:
        //   1. claimed: UPDATE the scans row to running, RETURNING the
        //      columns we need from scans (no join here).
        //   2. final SELECT joins claimed → repos to add github_url +
        //      default_branch. One round trip.
        const string sql = @"
            WITH claimed AS (
                UPDATE scans
                SET status     = 'running',
                    started_at = now() at time zone 'utc'
                WHERE id = (
                    SELECT id FROM scans
                    WHERE status = 'queued'
                    ORDER BY queued_at
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING id, public_id, org_id, repo_id, ""ref"", canon_version
            )
            SELECT
                c.id              AS scan_id,
                c.public_id       AS public_id,
                c.org_id          AS org_id,
                c.repo_id         AS repo_id,
                r.github_url      AS github_url,
                r.default_branch  AS default_branch,
                c.""ref""           AS ""ref"",
                c.canon_version   AS canon_version,
                r.is_private      AS is_private,
                r.added_by_user_id AS added_by_user_id,
                r.scan_projects   AS scan_projects
            FROM claimed c
            JOIN repos r ON r.id = c.repo_id;
        ";

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<ScanRow>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        if (row is null) return null;
        return new ClaimedScan(
            ScanId: row.scan_id,
            PublicId: row.public_id,
            OrgId: row.org_id,
            RepoId: row.repo_id,
            GithubUrl: row.github_url,
            DefaultBranch: row.default_branch,
            Ref: row.@ref,
            CanonVersion: row.canon_version,
            IsPrivate: row.is_private,
            AddedByUserId: row.added_by_user_id,
            ScanProjects: row.scan_projects);
    }

    public async Task MarkCompletedAsync(long scanId, string hashContent, CancellationToken ct)
    {
        const string sql = @"
            UPDATE scans
            SET status        = 'completed',
                completed_at  = now() at time zone 'utc',
                hash_content  = @hashContent,
                error         = NULL
            WHERE id = @scanId;
        ";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
            new { scanId, hashContent }, cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0)
        {
            _logger.LogWarning(
                "MarkCompletedAsync touched 0 rows; scan_id={ScanId} may have been deleted between claim and completion.",
                scanId);
        }
    }

    public async Task MarkFailedAsync(long scanId, string errorMessage, CancellationToken ct)
    {
        const string sql = @"
            UPDATE scans
            SET status        = 'failed',
                completed_at  = now() at time zone 'utc',
                error         = @errorMessage
            WHERE id = @scanId;
        ";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
            new { scanId, errorMessage }, cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0)
        {
            _logger.LogWarning(
                "MarkFailedAsync touched 0 rows; scan_id={ScanId} may have been deleted between claim and failure.",
                scanId);
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>
    /// Dapper row shape for the claim query. snake_case fields match the
    /// projected columns; suppression is local to this private type, same
    /// pattern as <c>PostgresJobStore.JobRow</c>.
    /// </summary>
#pragma warning disable IDE1006, CA1707, SA1300 // snake_case maps to PG columns
    private sealed class ScanRow
    {
        public long scan_id { get; set; }
        public Guid public_id { get; set; }
        public long org_id { get; set; }
        public long repo_id { get; set; }
        public string github_url { get; set; } = string.Empty;
        public string? default_branch { get; set; }
        public string? @ref { get; set; }
        public string canon_version { get; set; } = string.Empty;
        public bool is_private { get; set; }
        public long added_by_user_id { get; set; }
        public string[]? scan_projects { get; set; }
    }
#pragma warning restore IDE1006, CA1707, SA1300
}
