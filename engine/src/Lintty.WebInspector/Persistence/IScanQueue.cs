using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Persistence;

/// <summary>
/// Hot-path queue access for the org-bound <c>scans</c> table. ADR 0007 §3.4
/// + §3.7 — sibling to <see cref="Lintty.WebInspector.Jobs.IJobStore"/> for
/// the V0 anonymous <c>jobs</c> queue.
///
/// <para>
/// <b>Why a separate interface and not an addition to <see cref="Lintty.WebInspector.Jobs.IJobStore"/>.</b>
/// <c>IJobStore</c> is the V0 contract; PR 4 mustn't widen it because the
/// fakes in the test suite (and any future in-memory implementation) would
/// inherit a method they have no business implementing. Two narrow
/// interfaces beats one wide one.
/// </para>
///
/// <para>
/// <b>Why Dapper and not EF Core for the claim.</b> The service-layer reads
/// (<see cref="Lintty.WebInspector.Scans.IScanService"/>) use EF — full LINQ
/// ergonomics, includes, etc. The queue claim, like
/// <see cref="Lintty.WebInspector.Jobs.PostgresJobStore.ClaimNextQueuedAsync"/>,
/// needs a single-statement <c>UPDATE … WHERE id = (SELECT … FOR UPDATE
/// SKIP LOCKED) RETURNING *</c> for race-safety across concurrent workers.
/// Expressing that in EF requires <c>FromSqlRaw</c> anyway. Same canonical
/// pattern as the rest of the project: EF for opinionated CRUD, Dapper for
/// the worker hot path. ADR 0007 Apêndice C/D.
/// </para>
///
/// <para>
/// <b>Coexistence with <c>jobs</c>.</b> <see cref="Lintty.WebInspector.Jobs.JobWorker"/>
/// drains both queues in a fair round-robin (jobs first, then scans) so
/// V0 anonymous traffic and dashboard traffic share the same single worker
/// without one starving the other. Two small claims per poll cycle are
/// cheaper than the cross-table UNION query an "either/or" claim would
/// need, and keep the V0 SQL untouched (regression invariant).
/// </para>
/// </summary>
public interface IScanQueue
{
    /// <summary>
    /// Atomically picks the oldest queued <c>scans</c> row, flips it to
    /// <c>running</c>, sets <c>started_at = now()</c>, and returns the
    /// claimed row. Returns <c>null</c> when no queued scan exists. Race-safe
    /// with multiple workers (<c>FOR UPDATE SKIP LOCKED</c>).
    /// </summary>
    Task<ClaimedScan?> ClaimNextQueuedAsync(CancellationToken ct);

    /// <summary>
    /// Marks a previously claimed scan as completed. Sets <c>status =
    /// 'completed'</c>, <c>completed_at = now()</c>, and persists
    /// <paramref name="hashContent"/>. <c>hashContent</c> is the
    /// <c>sha256</c> of the engine JSON output (same value as the
    /// <c>hash_content</c> field in the report) — the cross-determinism
    /// witness (§3.7).
    /// </summary>
    Task MarkCompletedAsync(long scanId, string hashContent, CancellationToken ct);

    /// <summary>
    /// Marks a previously claimed scan as failed. Persists the error
    /// message verbatim (caller is responsible for redaction; no token
    /// material flows through here in the V0 anonymous-clone-only flow).
    /// </summary>
    Task MarkFailedAsync(long scanId, string errorMessage, CancellationToken ct);
}

/// <summary>
/// The minimal shape <see cref="Lintty.WebInspector.Jobs.JobWorker"/> needs
/// from a claimed scan. The full <c>Scan</c> entity stays an EF concern
/// (loaded by <c>ScanService</c> when an HTTP caller needs it); the worker
/// only needs the columns required to (a) shallow-clone the repo and (b)
/// invoke the engine. Dropping the rest avoids paying the cost of hydrating
/// navigation properties on a hot path.
/// </summary>
/// <remarks>
/// <see cref="GithubUrl"/> is denormalised here (read from <c>repos</c>
/// during the claim's projection) because the worker doesn't otherwise
/// touch <c>repos</c> — fetching it inside the same row read keeps the
/// claim a single round-trip. <see cref="DefaultBranch"/> is the snapshot
/// from <c>repos.default_branch</c> at claim time; <c>null</c> means "let
/// <c>git clone --depth 1</c> resolve HEAD itself".
///
/// <para>
/// <b>Apêndice E §E.9 fields (PR 7).</b> <see cref="IsPrivate"/> and
/// <see cref="AddedByUserId"/> are joined in from the same <c>repos</c>
/// row. The worker reads them to decide whether to fetch the user OAuth
/// token (<c>is_private = true</c> → look up
/// <see cref="Lintty.WebInspector.Auth.IGitHubUserTokenStore.GetActiveTokenAsync"/>
/// for <see cref="AddedByUserId"/>) before invoking <c>git clone</c>.
/// Public repos pass <c>token: null</c> like the V0 anonymous flow.
/// </para>
/// </remarks>
public sealed record ClaimedScan(
    long ScanId,
    Guid PublicId,
    long OrgId,
    long RepoId,
    string GithubUrl,
    string? DefaultBranch,
    string? Ref,
    string CanonVersion,
    bool IsPrivate,
    long AddedByUserId);
