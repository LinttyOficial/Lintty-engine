using System.Collections.Generic;

namespace Lintty.Engine.Reporter.Model;

/// <summary>
/// Read-only projection of the engine's <c>ReportDto</c> deserialized from JSON.
/// The reporter consumes only this view; it does NOT take a dependency on
/// runtime mutability of the engine's DTOs.
/// </summary>
internal sealed record ReportView(
    string SchemaVersion,
    string RunId,
    string CanonVersion,
    string RuleSetVersion,
    string SolutionPath,
    int Score,
    string Grade,
    bool SealEligible,
    IReadOnlyList<string> HardLocksHit,
    IReadOnlyDictionary<string, LayerSummaryView> LayerSummary,
    IReadOnlyList<ViolationView> Violations,
    IReadOnlyList<ExceptionView> Exceptions,
    IReadOnlyList<WorkspaceDiagnosticView> WorkspaceDiagnostics,
    string CompileStatus,
    MetricsView Metrics);

internal sealed record LayerSummaryView(int Projects, int Files, int Violations);

internal sealed record ViolationView(
    string RuleId,
    string Severity,
    bool IsHardLock,
    string File,
    int Line,
    int Column,
    string SymbolFqn,
    EvidenceView Evidence,
    string Fingerprint);

internal sealed record EvidenceView(
    string CodeSnippet,
    string AstKind,
    IReadOnlyDictionary<string, string> AdditionalContext);

internal sealed record ExceptionView(
    string File,
    int Line,
    string RuleId,
    string Justification,
    string? AuthorGitEmail,
    bool Valid,
    string? InvalidReason);

internal sealed record WorkspaceDiagnosticView(
    string Kind,
    string Message,
    string? Project);

internal sealed record MetricsView(
    int TotalSlocPhysical,
    IReadOnlyDictionary<string, int> SlocPerLayer,
    int ProjectsAnalyzed);
