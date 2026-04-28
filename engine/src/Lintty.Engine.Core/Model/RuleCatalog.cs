using System.Collections.Generic;

namespace Lintty.Engine.Core.Model;

/// <summary>
/// Source of truth for severity + hard-lock metadata of every LNTY rule.
/// is_hard_lock is COPIED FROM CANON in the JSON output, never computed
/// at runtime — this is the table.
/// </summary>
public static class RuleCatalog
{
    public sealed record RuleMeta(string Id, Severity Severity, bool IsHardLock, string Name);

    public static readonly IReadOnlyDictionary<string, RuleMeta> All =
        new Dictionary<string, RuleMeta>
        {
            ["LNTY-001"] = new("LNTY-001", Severity.Critical, true,  "Domain Layer Isolation"),
            ["LNTY-002"] = new("LNTY-002", Severity.Critical, true,  "Persistence Contamination"),
            ["LNTY-003"] = new("LNTY-003", Severity.Medium,   false, "Aggregate Root / Forbidden Instantiation"),
            ["LNTY-004"] = new("LNTY-004", Severity.High,     false, "Business Logic in Repository"),
            ["LNTY-005"] = new("LNTY-005", Severity.Medium,   false, "Anemic Domain"),
            ["LNTY-006"] = new("LNTY-006", Severity.Low,      false, "Ubiquitous Language Leak / Repository Contract Placement"),
            ["LNTY-007"] = new("LNTY-007", Severity.Critical, true,  "Dependency Cycles"),
            ["LNTY-008"] = new("LNTY-008", Severity.High,     false, "Ports at Boundaries"),
            ["LNTY-009"] = new("LNTY-009", Severity.Medium,   false, "Method Exceeds Analyzability"),
        };

    public static bool IsHardLock(string ruleId)
        => All.TryGetValue(ruleId, out var meta) && meta.IsHardLock;

    public static Severity SeverityOf(string ruleId)
        => All.TryGetValue(ruleId, out var meta) ? meta.Severity : Severity.Low;
}
