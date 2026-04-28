namespace Lintty.Engine.Core.Model;

/// <summary>Canon v1.0 severities. Lower-case JSON serialization.</summary>
public enum Severity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public static class SeverityExtensions
{
    public static string ToTag(this Severity s) => s switch
    {
        Severity.Critical => "critical",
        Severity.High => "high",
        Severity.Medium => "medium",
        Severity.Low => "low",
        _ => "low",
    };

    /// <summary>Canon v1.0 weights (docs/02-canon-v1.md §Cálculo de Score).</summary>
    public static int Weight(this Severity s) => s switch
    {
        Severity.Critical => 25,
        Severity.High => 10,
        Severity.Medium => 4,
        Severity.Low => 1,
        _ => 0,
    };
}
