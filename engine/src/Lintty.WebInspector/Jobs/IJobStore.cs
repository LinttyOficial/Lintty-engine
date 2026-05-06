using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Storage contract for the Web Inspector job pipeline. The V0 implementation
/// was SQLite (<c>JobStore</c>); ADR 0007 Sprint 1 replaces it with
/// <see cref="PostgresJobStore"/>. The interface stays stable on purpose —
/// callers (<c>JobsEndpoints</c>, <c>JobWorker</c>) are unchanged across the
/// cut.
///
/// Sprint 2+ will extend this contract with multi-tenant scan methods
/// (<c>ListScansForRepoAsync</c>, <c>GetScanByPublicIdAsync</c>) per ADR 0007
/// §3.4. For now the surface is the V0 surface, and the implementation is
/// Postgres.
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
