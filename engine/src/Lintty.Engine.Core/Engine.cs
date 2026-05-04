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
        // Reporting base: the directory the report's solution_path is relative
        // to. For a .sln target, that's the .sln directory; for a project list
        // (lintty.yml-driven OR a single --target Foo.csproj), that's the
        // directory of the SolutionPathForReporting (the lintty.yml dir, or
        // the .csproj parent for a bare-csproj target).
        var reportingPath = target.SolutionPathForReporting;
        var reportingDir = Path.GetDirectoryName(Path.GetFullPath(reportingPath))!;

        // Config directory: prefer the lintty.yml directory when known, else
        // walk up from the reporting path.
        var configDir = target.YamlDir ?? reportingDir;
        var configPath = Path.Combine(configDir, "lintty.yml");
        var config = LinttyConfig.LoadOrDefault(File.Exists(configPath) ? configPath : null);
        var canonVersion = canonVersionOverride ?? config.CanonVersion;

        var loader = new SolutionLoader();
        SolutionLoader.LoadResult loaded;
        if (target.IsSolution)
        {
            loaded = await loader.LoadFromSolutionAsync(target.SolutionPath!).ConfigureAwait(false);
        }
        else
        {
            loaded = await loader.LoadFromProjectListAsync(
                target.ProjectListPaths,
                target.SolutionPathForReporting).ConfigureAwait(false);
        }
        var tagger = new LayerTagger(config);

        var layerByProject = new Dictionary<string, Layer>(StringComparer.Ordinal);
        foreach (var (project, _) in loaded.Projects)
            layerByProject[project.Name] = tagger.Classify(project.Name, project.FilePath);

        var context = new AnalysisContext
        {
            SolutionPath = reportingPath,
            SolutionDir = reportingDir,
            Projects = loaded.Projects,
            LayerByProject = layerByProject,
            ProjectReferences = loaded.ProjectReferences,
            Tagger = tagger,
            Config = config,
        };

        var allViolations = new List<Violation>();
        foreach (var analyzer in _analyzers)
        {
            var found = await analyzer.AnalyzeAsync(context).ConfigureAwait(false);
            allViolations.AddRange(found);
        }

        // Suppressions: parse every C# document for @lintty-ignore directives.
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

        var scoring = Scorer.Compute(allViolations, suppressions);

        // Sort violations canonically: file, line, column, rule_id.
        var sortedViolations = allViolations
            .OrderBy(v => v.File, StringComparer.Ordinal)
            .ThenBy(v => v.Line)
            .ThenBy(v => v.Column)
            .ThenBy(v => v.RuleId, StringComparer.Ordinal)
            .ThenBy(v => v.SymbolFqn, StringComparer.Ordinal)
            .ToList();

        var report = BuildReport(
            canonVersion: canonVersion,
            solutionPath: reportingPath,
            solutionDir: reportingDir,
            violations: sortedViolations,
            suppressions: suppressions,
            scoring: scoring,
            warnings: loaded.Warnings,
            layerByProject: layerByProject,
            projects: loaded.Projects);

        return report;
    }

    private static ReportDto BuildReport(
        string canonVersion,
        string solutionPath,
        string solutionDir,
        IReadOnlyList<Violation> violations,
        IReadOnlyList<LinttyIgnoreParser.Suppression> suppressions,
        Scorer.Result scoring,
        IReadOnlyList<SolutionLoader.WorkspaceWarning> warnings,
        IReadOnlyDictionary<string, Layer> layerByProject,
        IReadOnlyList<(Project Project, Compilation Compilation)> projects)
    {
        // Layer summary.
        var layerCounts = new Dictionary<string, (int projects, int files, int violations)>(StringComparer.Ordinal);
        foreach (var layer in new[] { "domain", "application", "infrastructure", "presentation" })
            layerCounts[layer] = (0, 0, 0);

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

        // Metrics.
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

        var metrics = new MetricsDto
        {
            TotalSlocPhysical = totalSloc,
            SlocPerLayer = slocByLayer,
            ProjectsAnalyzed = projects.Count,
        };

        // Map violations to DTOs with deterministic fingerprints.
        var violationsDto = violations
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

        // Exceptions.
        var exceptionsDto = suppressions
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

        var diagnosticsDto = warnings
            .OrderBy(w => w.Project ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(w => w.Message, StringComparer.Ordinal)
            .Select(w => new WorkspaceDiagnosticDto
            {
                Kind = w.Kind,
                Message = w.Message,
                Project = w.Project,
            })
            .ToList();

        var solutionRel = Path.GetFileName(solutionPath);

        // run_id and scan_id derived deterministically from solution path + canon
        // version. Sprint 2 may revisit (see ADR 0001 §5.6).
        var runId = ComputeDeterministicId("run", solutionRel, canonVersion);
        var scanId = ComputeDeterministicId("scan", solutionRel, canonVersion);

        return new ReportDto
        {
            SchemaVersion = "1.0",
            RunId = runId,
            CanonVersion = canonVersion,
            RuleSetVersion = canonVersion,
            SolutionPath = solutionRel,
            Score = scoring.Score,
            Grade = scoring.Grade,
            SealEligible = scoring.SealEligible,
            HardLocksHit = scoring.HardLocksHit,
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
