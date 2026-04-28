using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Lintty.Engine.Core.Output;

/// <summary>
/// Top-level JSON contract emitted by the engine. Property order is locked
/// via JsonPropertyOrder to keep byte-for-byte determinism across runs and
/// platforms.
/// </summary>
public sealed class ReportDto
{
    [JsonPropertyName("schema_version")] [JsonPropertyOrder(1)]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("run_id")] [JsonPropertyOrder(2)]
    public string RunId { get; init; } = string.Empty;

    [JsonPropertyName("canon_version")] [JsonPropertyOrder(3)]
    public string CanonVersion { get; init; } = "1.0.0";

    [JsonPropertyName("rule_set_version")] [JsonPropertyOrder(4)]
    public string RuleSetVersion { get; init; } = "1.0.0";

    [JsonPropertyName("solution_path")] [JsonPropertyOrder(5)]
    public string SolutionPath { get; init; } = string.Empty;

    [JsonPropertyName("score")] [JsonPropertyOrder(6)]
    public int Score { get; init; }

    [JsonPropertyName("grade")] [JsonPropertyOrder(7)]
    public string Grade { get; init; } = "F";

    [JsonPropertyName("seal_eligible")] [JsonPropertyOrder(8)]
    public bool SealEligible { get; init; }

    [JsonPropertyName("hard_locks_hit")] [JsonPropertyOrder(9)]
    public IReadOnlyList<string> HardLocksHit { get; init; } = System.Array.Empty<string>();

    [JsonPropertyName("layer_summary")] [JsonPropertyOrder(10)]
    public IReadOnlyDictionary<string, LayerSummaryDto> LayerSummary { get; init; }
        = new Dictionary<string, LayerSummaryDto>();

    [JsonPropertyName("violations")] [JsonPropertyOrder(11)]
    public IReadOnlyList<ViolationDto> Violations { get; init; } = System.Array.Empty<ViolationDto>();

    [JsonPropertyName("exceptions")] [JsonPropertyOrder(12)]
    public IReadOnlyList<ExceptionDto> Exceptions { get; init; } = System.Array.Empty<ExceptionDto>();

    [JsonPropertyName("workspace_diagnostics")] [JsonPropertyOrder(13)]
    public IReadOnlyList<WorkspaceDiagnosticDto> WorkspaceDiagnostics { get; init; }
        = System.Array.Empty<WorkspaceDiagnosticDto>();

    [JsonPropertyName("inference_signature")] [JsonPropertyOrder(14)]
    public object? InferenceSignature { get; init; }

    [JsonPropertyName("compile_status")] [JsonPropertyOrder(15)]
    public string CompileStatus { get; init; } = "success";

    [JsonPropertyName("metrics")] [JsonPropertyOrder(16)]
    public MetricsDto Metrics { get; init; } = new();

    [JsonPropertyName("ai_candidates")] [JsonPropertyOrder(17)]
    public IReadOnlyList<object> AiCandidates { get; init; } = System.Array.Empty<object>();

    [JsonPropertyName("sandbox_integrity")] [JsonPropertyOrder(18)]
    public SandboxDto SandboxIntegrity { get; init; } = new();

    [JsonPropertyName("scan_id")] [JsonPropertyOrder(19)]
    public string ScanId { get; init; } = string.Empty;
}

public sealed class LayerSummaryDto
{
    [JsonPropertyName("projects")] [JsonPropertyOrder(1)] public int Projects { get; init; }
    [JsonPropertyName("files")] [JsonPropertyOrder(2)] public int Files { get; init; }
    [JsonPropertyName("violations")] [JsonPropertyOrder(3)] public int Violations { get; init; }
}

public sealed class ViolationDto
{
    [JsonPropertyName("rule_id")] [JsonPropertyOrder(1)] public string RuleId { get; init; } = string.Empty;
    [JsonPropertyName("severity")] [JsonPropertyOrder(2)] public string Severity { get; init; } = "low";
    [JsonPropertyName("is_hard_lock")] [JsonPropertyOrder(3)] public bool IsHardLock { get; init; }
    [JsonPropertyName("file")] [JsonPropertyOrder(4)] public string File { get; init; } = string.Empty;
    [JsonPropertyName("line")] [JsonPropertyOrder(5)] public int Line { get; init; }
    [JsonPropertyName("column")] [JsonPropertyOrder(6)] public int Column { get; init; }
    [JsonPropertyName("symbol_fqn")] [JsonPropertyOrder(7)] public string SymbolFqn { get; init; } = string.Empty;
    [JsonPropertyName("evidence")] [JsonPropertyOrder(8)] public EvidenceDto Evidence { get; init; } = new();
    [JsonPropertyName("fingerprint")] [JsonPropertyOrder(9)] public string Fingerprint { get; init; } = string.Empty;
}

public sealed class EvidenceDto
{
    [JsonPropertyName("code_snippet")] [JsonPropertyOrder(1)] public string CodeSnippet { get; init; } = string.Empty;
    [JsonPropertyName("ast_kind")] [JsonPropertyOrder(2)] public string AstKind { get; init; } = string.Empty;
    [JsonPropertyName("additional_context")] [JsonPropertyOrder(3)]
    public IReadOnlyDictionary<string, string> AdditionalContext { get; init; }
        = new Dictionary<string, string>();
}

public sealed class ExceptionDto
{
    [JsonPropertyName("file")] [JsonPropertyOrder(1)] public string File { get; init; } = string.Empty;
    [JsonPropertyName("line")] [JsonPropertyOrder(2)] public int Line { get; init; }
    [JsonPropertyName("rule_id")] [JsonPropertyOrder(3)] public string RuleId { get; init; } = string.Empty;
    [JsonPropertyName("justification")] [JsonPropertyOrder(4)] public string Justification { get; init; } = string.Empty;
    [JsonPropertyName("author_git_email")] [JsonPropertyOrder(5)] public string? AuthorGitEmail { get; init; }
    [JsonPropertyName("valid")] [JsonPropertyOrder(6)] public bool Valid { get; init; }
    [JsonPropertyName("invalid_reason")] [JsonPropertyOrder(7)] public string? InvalidReason { get; init; }
}

public sealed class WorkspaceDiagnosticDto
{
    [JsonPropertyName("kind")] [JsonPropertyOrder(1)] public string Kind { get; init; } = "warning";
    [JsonPropertyName("message")] [JsonPropertyOrder(2)] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("project")] [JsonPropertyOrder(3)] public string? Project { get; init; }
}

public sealed class MetricsDto
{
    [JsonPropertyName("total_sloc_physical")] [JsonPropertyOrder(1)]
    public int TotalSlocPhysical { get; init; }

    [JsonPropertyName("sloc_per_layer")] [JsonPropertyOrder(2)]
    public IReadOnlyDictionary<string, int> SlocPerLayer { get; init; }
        = new Dictionary<string, int>();

    [JsonPropertyName("projects_analyzed")] [JsonPropertyOrder(3)]
    public int ProjectsAnalyzed { get; init; }
}

public sealed class SandboxDto
{
    [JsonPropertyName("source_destroyed_at")] [JsonPropertyOrder(1)]
    public string? SourceDestroyedAt { get; init; }

    [JsonPropertyName("egress_violations")] [JsonPropertyOrder(2)]
    public IReadOnlyList<string> EgressViolations { get; init; } = System.Array.Empty<string>();
}
