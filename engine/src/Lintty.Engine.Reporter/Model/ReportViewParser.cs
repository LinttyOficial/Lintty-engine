using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Lintty.Engine.Reporter.Model;

/// <summary>
/// Parses the engine's report JSON into a <see cref="ReportView"/> without
/// taking a dependency on the Core DTO surface. The reporter intentionally
/// re-parses to keep its contract scoped to the documented schema (ADR 0001 §3).
/// Strict mode: missing top-level fields throw with a descriptive message.
/// </summary>
internal static class ReportViewParser
{
    public static ReportView Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Report JSON is empty.", nameof(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var hardLocks = ParseHardLocks(root);
        var layerSummary = ParseLayerSummaryDict(root);
        var violationsList = ParseViolationsArray(root);
        var exceptionsList = ParseExceptionsArray(root);
        var diagnostics = ParseDiagnosticsArray(root);
        var metrics = ParseMetrics(root);

        var (activeViolations, suppressedViolations) = SplitBySuppression(violationsList, exceptionsList);

        return new ReportView(
            SchemaVersion: Str(root, "schema_version", "1.0"),
            RunId: Str(root, "run_id"),
            CanonVersion: Str(root, "canon_version", "1.0.0"),
            RuleSetVersion: Str(root, "rule_set_version", "1.0.0"),
            SolutionPath: Str(root, "solution_path"),
            Score: Int(root, "score"),
            Grade: Str(root, "grade", "F"),
            SealEligible: Bool(root, "seal_eligible"),
            HardLocksHit: hardLocks,
            LayerSummary: layerSummary,
            Violations: violationsList,
            ActiveViolations: activeViolations,
            SuppressedViolations: suppressedViolations,
            Exceptions: exceptionsList,
            WorkspaceDiagnostics: diagnostics,
            CompileStatus: Str(root, "compile_status", "success"),
            Metrics: metrics);
    }

    /// <summary>
    /// Mirrors <c>Scorer.Compute</c>'s suppression window: a valid
    /// <c>@lintty-ignore</c> on line L applies to the directive line and the
    /// next 5 lines (covers the next member declaration). Hard locks are
    /// canon-mandated and never suppressible — they stay active even when a
    /// matching directive exists. Tuple is returned in stable order: the
    /// <c>violations[]</c> array is already sorted (file, line, column,
    /// rule_id) by the engine, so iterating in-place preserves determinism
    /// of the rendered PDF.
    /// </summary>
    private static (List<ViolationView> Active, List<SuppressedViolationView> Suppressed)
        SplitBySuppression(
            IReadOnlyList<ViolationView> violations,
            IReadOnlyList<ExceptionView> exceptions)
    {
        // Index valid suppressions by (file, line+offset, ruleId) for the
        // 5-line window. Path normalization (\ → /) matches Scorer.Compute.
        var suppressionIndex = new Dictionary<(string file, int line, string rule), ExceptionView>();
        foreach (var ex in exceptions)
        {
            if (!ex.Valid) continue;
            var normalizedFile = ex.File.Replace('\\', '/');
            for (var offset = 0; offset <= 5; offset++)
            {
                var key = (normalizedFile, ex.Line + offset, ex.RuleId);
                // First-write-wins: if two suppressions overlap on a key, the
                // earlier one is kept. Engine output is deterministically
                // sorted, so this is stable.
                if (!suppressionIndex.ContainsKey(key))
                    suppressionIndex[key] = ex;
            }
        }

        var active = new List<ViolationView>(violations.Count);
        var suppressed = new List<SuppressedViolationView>();

        foreach (var v in violations)
        {
            if (v.IsHardLock)
            {
                // Canon: hard locks are never suppressible. Always active.
                active.Add(v);
                continue;
            }

            var key = (v.File.Replace('\\', '/'), v.Line, v.RuleId);
            if (suppressionIndex.TryGetValue(key, out var match))
            {
                suppressed.Add(new SuppressedViolationView(
                    Violation: v,
                    Justification: match.Justification,
                    AuthorGitEmail: match.AuthorGitEmail,
                    SuppressionLine: match.Line));
            }
            else
            {
                active.Add(v);
            }
        }

        return (active, suppressed);
    }

    private static string Str(JsonElement el, string name, string fallback = "")
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? fallback;
        return fallback;
    }

    private static int Int(JsonElement el, string name, int fallback = 0)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        return fallback;
    }

    private static bool Bool(JsonElement el, string name, bool fallback = false)
    {
        if (el.TryGetProperty(name, out var v) &&
            (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
            return v.GetBoolean();
        return fallback;
    }

    private static List<string> ParseHardLocks(JsonElement root)
    {
        var hardLocks = new List<string>();
        if (root.TryGetProperty("hard_locks_hit", out var hl) && hl.ValueKind == JsonValueKind.Array)
            foreach (var item in hl.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) hardLocks.Add(item.GetString()!);
        return hardLocks;
    }

    private static Dictionary<string, LayerSummaryView> ParseLayerSummaryDict(JsonElement root)
    {
        var layerSummary = new Dictionary<string, LayerSummaryView>(StringComparer.Ordinal);
        if (root.TryGetProperty("layer_summary", out var ls) && ls.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in ls.EnumerateObject())
            {
                int projects = 0, files = 0, violations = 0;
                if (prop.Value.TryGetProperty("projects", out var p) && p.ValueKind == JsonValueKind.Number)
                    projects = p.GetInt32();
                if (prop.Value.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Number)
                    files = f.GetInt32();
                if (prop.Value.TryGetProperty("violations", out var vc) && vc.ValueKind == JsonValueKind.Number)
                    violations = vc.GetInt32();
                layerSummary[prop.Name] = new LayerSummaryView(projects, files, violations);
            }
        }
        return layerSummary;
    }

    private static List<ViolationView> ParseViolationsArray(JsonElement root)
    {
        var violationsList = new List<ViolationView>();
        if (root.TryGetProperty("violations", out var vs) && vs.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in vs.EnumerateArray())
                violationsList.Add(ParseViolation(v));
        }
        return violationsList;
    }

    private static List<ExceptionView> ParseExceptionsArray(JsonElement root)
    {
        var exceptionsList = new List<ExceptionView>();
        if (root.TryGetProperty("exceptions", out var es) && es.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in es.EnumerateArray())
                exceptionsList.Add(ParseException(e));
        }
        return exceptionsList;
    }

    private static List<WorkspaceDiagnosticView> ParseDiagnosticsArray(JsonElement root)
    {
        var diagnostics = new List<WorkspaceDiagnosticView>();
        if (root.TryGetProperty("workspace_diagnostics", out var wd) && wd.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in wd.EnumerateArray())
            {
                var kind = d.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? (k.GetString() ?? "warning") : "warning";
                var msg = d.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? (m.GetString() ?? "") : "";
                string? project = null;
                if (d.TryGetProperty("project", out var pj) && pj.ValueKind == JsonValueKind.String)
                    project = pj.GetString();
                diagnostics.Add(new WorkspaceDiagnosticView(kind, msg, project));
            }
        }
        return diagnostics;
    }

    private static ViolationView ParseViolation(JsonElement v)
    {
        string ruleId = "", severity = "low", file = "", symbol = "", fingerprint = "";
        bool isHard = false;
        int line = 0, column = 0;

        if (v.TryGetProperty("rule_id", out var r) && r.ValueKind == JsonValueKind.String) ruleId = r.GetString() ?? "";
        if (v.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.String) severity = s.GetString() ?? "low";
        if (v.TryGetProperty("is_hard_lock", out var h) && (h.ValueKind == JsonValueKind.True || h.ValueKind == JsonValueKind.False))
            isHard = h.GetBoolean();
        if (v.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String) file = f.GetString() ?? "";
        if (v.TryGetProperty("line", out var ln) && ln.ValueKind == JsonValueKind.Number) line = ln.GetInt32();
        if (v.TryGetProperty("column", out var c) && c.ValueKind == JsonValueKind.Number) column = c.GetInt32();
        if (v.TryGetProperty("symbol_fqn", out var sf) && sf.ValueKind == JsonValueKind.String) symbol = sf.GetString() ?? "";
        if (v.TryGetProperty("fingerprint", out var fp) && fp.ValueKind == JsonValueKind.String) fingerprint = fp.GetString() ?? "";

        var evidence = ParseEvidence(v);
        return new ViolationView(ruleId, severity, isHard, file, line, column, symbol, evidence, fingerprint);
    }

    private static EvidenceView ParseEvidence(JsonElement violation)
    {
        var snippet = "";
        var astKind = "";
        var ctx = new Dictionary<string, string>(StringComparer.Ordinal);

        if (violation.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Object)
        {
            if (ev.TryGetProperty("code_snippet", out var cs) && cs.ValueKind == JsonValueKind.String)
                snippet = cs.GetString() ?? "";
            if (ev.TryGetProperty("ast_kind", out var ak) && ak.ValueKind == JsonValueKind.String)
                astKind = ak.GetString() ?? "";
            if (ev.TryGetProperty("additional_context", out var ac) && ac.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ac.EnumerateObject())
                {
                    string val = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? "",
                        JsonValueKind.Number => prop.Value.ToString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => "",
                        _ => prop.Value.ToString(),
                    };
                    ctx[prop.Name] = val;
                }
            }
        }
        return new EvidenceView(snippet, astKind, ctx);
    }

    private static ExceptionView ParseException(JsonElement e)
    {
        string file = "", ruleId = "", justification = "";
        int line = 0;
        string? author = null;
        bool valid = false;
        string? invalidReason = null;

        if (e.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String) file = f.GetString() ?? "";
        if (e.TryGetProperty("line", out var ln) && ln.ValueKind == JsonValueKind.Number) line = ln.GetInt32();
        if (e.TryGetProperty("rule_id", out var r) && r.ValueKind == JsonValueKind.String) ruleId = r.GetString() ?? "";
        if (e.TryGetProperty("justification", out var j) && j.ValueKind == JsonValueKind.String) justification = j.GetString() ?? "";
        if (e.TryGetProperty("author_git_email", out var a) && a.ValueKind == JsonValueKind.String) author = a.GetString();
        if (e.TryGetProperty("valid", out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)) valid = v.GetBoolean();
        if (e.TryGetProperty("invalid_reason", out var ir) && ir.ValueKind == JsonValueKind.String) invalidReason = ir.GetString();

        return new ExceptionView(file, line, ruleId, justification, author, valid, invalidReason);
    }

    private static MetricsView ParseMetrics(JsonElement root)
    {
        int totalSloc = 0;
        int projectsAnalyzed = 0;
        var perLayer = new Dictionary<string, int>(StringComparer.Ordinal);

        if (root.TryGetProperty("metrics", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            if (m.TryGetProperty("total_sloc_physical", out var t) && t.ValueKind == JsonValueKind.Number)
                totalSloc = t.GetInt32();
            if (m.TryGetProperty("projects_analyzed", out var pa) && pa.ValueKind == JsonValueKind.Number)
                projectsAnalyzed = pa.GetInt32();
            if (m.TryGetProperty("sloc_per_layer", out var spl) && spl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in spl.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        perLayer[prop.Name] = prop.Value.GetInt32();
            }
        }
        return new MetricsView(totalSloc, perLayer, projectsAnalyzed);
    }
}
