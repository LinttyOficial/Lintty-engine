using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Lintty.Engine.Core.Analyzers;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Output;
using Lintty.Engine.Core.Scoring;
using Lintty.Engine.Core.Suppressions;
using Lintty.Engine.Core.Tagging;
using Lintty.Engine.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace Lintty.Engine.Core;

/// <summary>
/// Top-level façade. Loads a solution, runs every Sprint-0 analyzer in a
/// fixed order, computes the canonical score, and produces a deterministic
/// <see cref="ReportDto"/>.
/// </summary>
public sealed class LinttyEngine
{
    private readonly IReadOnlyList<IAnalyzer> _analyzers;

    public LinttyEngine()
    {
        _analyzers = new IAnalyzer[]
        {
            new Lnty001_DomainLayerIsolation(),
            new Lnty002_PersistenceContamination(),
            new Lnty003_AggregateAndInstantiation(),
            new Lnty006_LanguageAndRepositoryPlacement(),
            new Lnty007_DependencyCycles(),
            new Lnty008_PortsAtBoundaries(),
            new Lnty009_MethodExceedsAnalyzability(),
        };
    }

    public Task<ReportDto> AnalyzeAsync(string solutionPath, string? canonVersionOverride)
    {
        // Back-compat overload: callers passing a raw .sln path keep working.
        // New callers should resolve a ResolvedTarget via TargetResolver
        // (ADR 0006) and use the overload below.
        var resolved = ResolvedTarget.ForSolution(Path.GetFullPath(solutionPath));
        return AnalyzeAsync(resolved, canonVersionOverride);
    }

    public async Task<ReportDto> AnalyzeAsync(ResolvedTarget target, string? canonVersionOverride)
    {
        var (reportingPath, reportingDir, config, canonVersion) = ResolveReportingAndConfig(target, canonVersionOverride);

        var loaded = await LoadAndTag(target, config).ConfigureAwait(false);
        var layerByProject = ClassifyLayersPermissive(loaded, new LayerTagger(config));

        var context = new AnalysisContext
        {
            SolutionPath = reportingPath,
            SolutionDir = reportingDir,
            Projects = loaded.Projects,
            LayerByProject = layerByProject,
            ProjectReferences = loaded.ProjectReferences,
            Tagger = new LayerTagger(config),
            Config = config,
        };

        var allViolations = await RunAllAnalyzersAsync(context, _analyzers).ConfigureAwait(false);
        var suppressions = await CollectSuppressionsAsync(loaded, context).ConfigureAwait(false);

        var scoring = Scorer.Compute(allViolations, suppressions);

        // Sort violations canonically: file, line, column, rule_id.
        var sortedViolations = allViolations
            .OrderBy(v => v.File, StringComparer.Ordinal)
            .ThenBy(v => v.Line)
            .ThenBy(v => v.Column)
            .ThenBy(v => v.RuleId, StringComparer.Ordinal)
            .ThenBy(v => v.SymbolFqn, StringComparer.Ordinal)
            .ToList();

        var inputs = new BuildReportInputs(
            CanonVersion: canonVersion,
            SolutionPath: reportingPath,
            SolutionDir: reportingDir,
            Violations: sortedViolations,
            Suppressions: suppressions,
            Scoring: scoring,
            Warnings: loaded.Warnings,
            LayerByProject: layerByProject,
            Projects: loaded.Projects);

        return BuildReport(inputs);
    }

    /// <summary>
    /// Resolves the paths used as the report's "reporting base" and loads the
    /// <c>lintty.yml</c> config (defaulted when missing). The reporting base
    /// is the directory the report's <c>solution_path</c> is relative to: for
    /// a .sln target, the .sln directory; for a project list (lintty.yml or a
    /// single --target Foo.csproj), the SolutionPathForReporting parent.
    /// </summary>
    private static (string ReportingPath, string ReportingDir, LinttyConfig Config, string CanonVersion)
        ResolveReportingAndConfig(ResolvedTarget target, string? canonVersionOverride)
    {
        var reportingPath = target.SolutionPathForReporting;
        var reportingDir = Path.GetDirectoryName(Path.GetFullPath(reportingPath))!;

        // Config directory: prefer the lintty.yml directory when known, else
        // walk up from the reporting path.
        var configDir = target.YamlDir ?? reportingDir;
        var configPath = Path.Combine(configDir, "lintty.yml");
        var config = LinttyConfig.LoadOrDefault(File.Exists(configPath) ? configPath : null);
        var canonVersion = canonVersionOverride ?? config.CanonVersion;

        return (reportingPath, reportingDir, config, canonVersion);
    }

    private static async Task<SolutionLoader.LoadResult> LoadAndTag(ResolvedTarget target, LinttyConfig config)
    {
        var loader = new SolutionLoader();
        if (target.IsSolution)
        {
            return await loader.LoadFromSolutionAsync(target.SolutionPath!).ConfigureAwait(false);
        }
        return await loader.LoadFromProjectListAsync(
            target.ProjectListPaths,
            target.SolutionPathForReporting).ConfigureAwait(false);
    }

    /// <summary>
    /// Permissive classification (2026-05-06 Web Inspector UX fix). Every loaded
    /// project gets a layer; projects that match neither convention_map nor
    /// explicit_map are tagged <see cref="Layer.Unknown"/> and surfaced in the
    /// layer summary with a CTA, not aborted. Layer-aware analyzers (LNTY-001,
    /// LNTY-008, the language/repository-placement halves of LNTY-006) skip
    /// Unknown projects; layer-agnostic analyzers (LNTY-007 cycles, LNTY-009
    /// method size) still run on them.
    ///
    /// Rationale: prospects on the public Web Inspector typically don't ship a
    /// <c>lintty.yml</c>, and demanding one upfront is hostile to the default
    /// flow. The strict <see cref="LayerTaggingError"/> exception remains
    /// available as a future opt-in (e.g. CI fail-fast mode).
    /// </summary>
    private static Dictionary<string, Layer> ClassifyLayersPermissive(
        SolutionLoader.LoadResult loaded,
        LayerTagger tagger)
    {
        var layerByProject = new Dictionary<string, Layer>(StringComparer.Ordinal);
        foreach (var (project, _) in loaded.Projects)
            layerByProject[project.Name] = tagger.Classify(project.Name, project.FilePath);
        return layerByProject;
    }

    private static async Task<List<Violation>> RunAllAnalyzersAsync(
        AnalysisContext context,
        IReadOnlyList<IAnalyzer> analyzers)
    {
        var allViolations = new List<Violation>();
        foreach (var analyzer in analyzers)
        {
            var found = await analyzer.AnalyzeAsync(context).ConfigureAwait(false);
            allViolations.AddRange(found);
        }
        return allViolations;
    }

    /// <summary>
    /// Suppressions: parse every C# document for <c>@lintty-ignore</c>
    /// directives.
    /// </summary>
    private static async Task<List<LinttyIgnoreParser.Suppression>> CollectSuppressionsAsync(
        SolutionLoader.LoadResult loaded,
        AnalysisContext context)
    {
        var suppressions = new List<LinttyIgnoreParser.Suppression>();
        foreach (var (_, compilation) in loaded.Projects)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var sourceText = (await tree.GetTextAsync().ConfigureAwait(false)).ToString();
                foreach (var s in LinttyIgnoreParser.Parse(tree.FilePath, sourceText))
                {
                    suppressions.Add(new LinttyIgnoreParser.Suppression(
                        File: context.RelativePath(s.File),
                        Line: s.Line,
                        RuleId: s.RuleId,
                        Justification: s.Justification,
                        Valid: s.Valid,
                        InvalidReason: s.InvalidReason));
                }
            }
        }
        return suppressions;
    }

    /// <summary>
    /// All inputs <see cref="BuildReport"/> needs to assemble the final
    /// <see cref="ReportDto"/>. Introduced to collapse the original 9-parameter
    /// signature; the underlying assembly logic and field ordering are
    /// preserved bit-for-bit (locked schema 1.0).
    /// </summary>
    private sealed record BuildReportInputs(
        string CanonVersion,
        string SolutionPath,
        string SolutionDir,
        IReadOnlyList<Violation> Violations,
        IReadOnlyList<LinttyIgnoreParser.Suppression> Suppressions,
        Scorer.Result Scoring,
        IReadOnlyList<SolutionLoader.WorkspaceWarning> Warnings,
        IReadOnlyDictionary<string, Layer> LayerByProject,
        IReadOnlyList<(Project Project, Compilation Compilation)> Projects);

    private static ReportDto BuildReport(BuildReportInputs i)
    {
        var layerSummary = BuildLayerSummary(i.Projects, i.LayerByProject, i.Violations, i.SolutionDir);
        var metrics = BuildMetrics(i.Projects, i.LayerByProject);
        var violationsDto = MapViolations(i.Violations, i.CanonVersion);
        var exceptionsDto = MapExceptions(i.Suppressions);
        var diagnosticsDto = MapDiagnostics(i.Warnings);
        var (runId, scanId) = ComputeIds(i.SolutionPath, i.CanonVersion);
        var solutionRel = Path.GetFileName(i.SolutionPath);

        return new ReportDto
        {
            SchemaVersion = "1.0",
            RunId = runId,
            CanonVersion = i.CanonVersion,
            RuleSetVersion = i.CanonVersion,
            SolutionPath = solutionRel,
            Score = i.Scoring.Score,
            Grade = i.Scoring.Grade,
            SealEligible = i.Scoring.SealEligible,
            HardLocksHit = i.Scoring.HardLocksHit,
            LayerSummary = layerSummary,
            Violations = violationsDto,
            Exceptions = exceptionsDto,
            WorkspaceDiagnostics = diagnosticsDto,
            InferenceSignature = null,
            CompileStatus = "success",
            Metrics = metrics,
            AiCandidates = System.Array.Empty<object>(),
            SandboxIntegrity = new SandboxDto(),
            ScanId = scanId,
        };
    }

    /// <summary>
    /// Builds the per-layer counts (projects/files/violations). Iteration order
    /// over <paramref name="projects"/> and <paramref name="violations"/> is
    /// preserved to keep <c>hash_content</c> stable; the final SortedDictionary
    /// always emits Domain/Application/Infrastructure/Presentation in ordinal
    /// order (display-cased).
    ///
    /// Permissive layer-tagging (2026-05-06): the "unknown" bucket is
    /// emitted ONLY when at least one loaded project actually classified to
    /// <see cref="Layer.Unknown"/>. This keeps the existing fixtures
    /// (Saint/Sinner/Ninja-01) byte-identical because all of their projects
    /// classify cleanly via convention; Foreigner-style scans (no
    /// <c>lintty.yml</c>, names not matching convention) get an extra
    /// "Unknown" row that surfaces the CTA in the laudo.
    /// </summary>
    private static SortedDictionary<string, LayerSummaryDto> BuildLayerSummary(
        IReadOnlyList<(Project Project, Compilation Compilation)> projects,
        IReadOnlyDictionary<string, Layer> layerByProject,
        IReadOnlyList<Violation> violations,
        string solutionDir)
    {
        var layerCounts = new Dictionary<string, (int projects, int files, int violations)>(StringComparer.Ordinal);
        foreach (var layer in new[] { "domain", "application", "infrastructure", "presentation" })
            layerCounts[layer] = (0, 0, 0);

        // Detect Unknown projects up-front so we only emit the row when needed.
        // Schema 1.0 stays locked: existing fixtures with all projects
        // classified see no "Unknown" key at all.
        var hasUnknown = false;
        foreach (var kv in layerByProject)
        {
            if (kv.Value == Layer.Unknown) { hasUnknown = true; break; }
        }
        if (hasUnknown)
            layerCounts["unknown"] = (0, 0, 0);

        foreach (var (project, compilation) in projects)
        {
            var layer = layerByProject[project.Name];
            var key = layer.ToTag();
            if (!layerCounts.ContainsKey(key)) continue;

            var fileCount = compilation.SyntaxTrees.Count(t => !GeneratedCodeFilter.IsGenerated(t));
            var current = layerCounts[key];
            layerCounts[key] = (current.projects + 1, current.files + fileCount, current.violations);
        }

        foreach (var v in violations)
        {
            // Map violation file to a layer by matching its path prefix to a project dir.
            var layer = LayerForViolation(v, projects, solutionDir, layerByProject);
            var key = layer.ToTag();
            if (!layerCounts.ContainsKey(key)) continue;
            var current = layerCounts[key];
            layerCounts[key] = (current.projects, current.files, current.violations + 1);
        }

        var layerSummary = new SortedDictionary<string, LayerSummaryDto>(StringComparer.Ordinal);
        foreach (var kv in layerCounts)
        {
            // Capitalize for display per ADR §3.4 example ("Domain", "Application", ...).
            var display = char.ToUpperInvariant(kv.Key[0]) + kv.Key.Substring(1);
            layerSummary[display] = new LayerSummaryDto
            {
                Projects = kv.Value.projects,
                Files = kv.Value.files,
                Violations = kv.Value.violations,
            };
        }
        return layerSummary;
    }

    private static MetricsDto BuildMetrics(
        IReadOnlyList<(Project Project, Compilation Compilation)> projects,
        IReadOnlyDictionary<string, Layer> layerByProject)
    {
        var slocByLayer = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var totalSloc = 0;
        foreach (var (project, compilation) in projects)
        {
            var layer = layerByProject[project.Name];
            var key = layer.ToTag();
            var projectSloc = 0;
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                projectSloc += tree.GetText().Lines.Count;
            }
            slocByLayer.TryGetValue(key, out var existing);
            slocByLayer[key] = existing + projectSloc;
            totalSloc += projectSloc;
        }

        return new MetricsDto
        {
            TotalSlocPhysical = totalSloc,
            SlocPerLayer = slocByLayer,
            ProjectsAnalyzed = projects.Count,
        };
    }

    private static List<ViolationDto> MapViolations(IReadOnlyList<Violation> violations, string canonVersion)
    {
        return violations
            .Select(v => new ViolationDto
            {
                RuleId = v.RuleId,
                Severity = v.Severity.ToTag(),
                IsHardLock = v.IsHardLock,
                File = v.File,
                Line = v.Line,
                Column = v.Column,
                SymbolFqn = v.SymbolFqn,
                Evidence = new EvidenceDto
                {
                    CodeSnippet = v.CodeSnippet,
                    AstKind = v.AstKind,
                    AdditionalContext = new SortedDictionary<string, string>(
                        v.AdditionalContext.ToDictionary(kv => kv.Key, kv => kv.Value),
                        StringComparer.Ordinal),
                },
                Fingerprint = ComputeFingerprint(v.RuleId, v.CodeSnippet, canonVersion),
            })
            .ToList();
    }

    private static List<ExceptionDto> MapExceptions(IReadOnlyList<LinttyIgnoreParser.Suppression> suppressions)
    {
        return suppressions
            .OrderBy(s => s.File, StringComparer.Ordinal)
            .ThenBy(s => s.Line)
            .ThenBy(s => s.RuleId, StringComparer.Ordinal)
            .Select(s => new ExceptionDto
            {
                File = s.File,
                Line = s.Line,
                RuleId = s.RuleId,
                Justification = s.Justification,
                AuthorGitEmail = null,
                Valid = s.Valid,
                InvalidReason = s.InvalidReason,
            })
            .ToList();
    }

    private static List<WorkspaceDiagnosticDto> MapDiagnostics(IReadOnlyList<SolutionLoader.WorkspaceWarning> warnings)
    {
        return warnings
            .OrderBy(w => w.Project ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(w => w.Message, StringComparer.Ordinal)
            .Select(w => new WorkspaceDiagnosticDto
            {
                Kind = w.Kind,
                Message = w.Message,
                Project = w.Project,
            })
            .ToList();
    }

    /// <summary>
    /// run_id and scan_id derived deterministically from solution path + canon
    /// version. Sprint 2 may revisit (see ADR 0001 §5.6).
    /// </summary>
    private static (string RunId, string ScanId) ComputeIds(string solutionPath, string canonVersion)
    {
        var solutionRel = Path.GetFileName(solutionPath);
        var runId = ComputeDeterministicId("run", solutionRel, canonVersion);
        var scanId = ComputeDeterministicId("scan", solutionRel, canonVersion);
        return (runId, scanId);
    }

    private static Layer LayerForViolation(
        Violation v,
        IReadOnlyList<(Project Project, Compilation Compilation)> projects,
        string solutionDir,
        IReadOnlyDictionary<string, Layer> layerByProject)
    {
        if (v.File.StartsWith("<", StringComparison.Ordinal)) return Layer.Unknown;

        // Match the violation's relative path against each project's directory.
        var path = v.File;
        foreach (var (project, _) in projects)
        {
            if (project.FilePath is null) continue;
            var projDir = Path.GetDirectoryName(project.FilePath)!;
            var rel = Path.GetRelativePath(solutionDir, projDir).Replace('\\', '/');
            if (path.StartsWith(rel + "/", StringComparison.Ordinal)
                || path.StartsWith(rel, StringComparison.Ordinal))
            {
                return layerByProject[project.Name];
            }
        }
        return Layer.Unknown;
    }

    private static string ComputeFingerprint(string ruleId, string codeSlice, string canonVersion)
    {
        var normalized = (codeSlice ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\t", "    ")
            .Trim();
        var input = ruleId + "|" + normalized + "|" + canonVersion;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeDeterministicId(string prefix, string solutionRel, string canonVersion)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + "|" + solutionRel + "|" + canonVersion));
        return prefix + "_" + Convert.ToHexString(bytes).Substring(0, 16).ToLowerInvariant();
    }
}
