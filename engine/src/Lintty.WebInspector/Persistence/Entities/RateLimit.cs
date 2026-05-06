namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// Per-IP daily counter for the anonymous V0 flow. Composite PK on
/// <c>(ip, day)</c>. Schema absorbed by EF in Sprint 2 (was bootstrapped via
/// embedded SQL in Sprint 1) — runtime path still goes through
/// <see cref="Lintty.WebInspector.Jobs.PostgresJobStore.IncrementRateLimitAsync"/>
/// (Dapper, hand-tuned <c>ON CONFLICT DO UPDATE ... RETURNING count</c>).
/// </summary>
public sealed class RateLimit
{
    public string Ip { get; set; } = string.Empty;

    /// <summary>Date in <c>yyyy-MM-dd</c> (string, not <c>date</c>, to keep parity with V0 schema).</summary>
    public string Day { get; set; } = string.Empty;

    public int Count { get; set; }
}
