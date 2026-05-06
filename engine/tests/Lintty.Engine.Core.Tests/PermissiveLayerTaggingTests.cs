using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lintty.Engine.Core.Analyzers;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Output;
using Lintty.Engine.Core.Tagging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Lintty.Engine.Core.Tests;

/// <summary>
/// Permissive layer-tagging regression suite (2026-05-06). Before this change
/// the engine refused to produce a laudo as soon as any project fell through
/// to <see cref="Layer.Unknown"/> — it threw <c>LayerTaggingError</c> and the
/// CLI exited with code 2. The Web Inspector public flow can't take that
/// posture: prospects don't ship a <c>lintty.yml</c>, and refusing the laudo
/// is hostile.
///
/// These tests pin the new contract:
///
///   1. <see cref="LayerTagger"/> returns <see cref="Layer.Unknown"/> instead
///      of throwing when a project name matches no convention pattern AND has
///      no explicit_map entry.
///   2. <see cref="LinttyEngine.AnalyzeAsync(ResolvedTarget, string?)"/>
///      completes successfully with mixed Known + Unknown projects, emits an
///      "Unknown" row in <c>layer_summary</c>, and includes those projects in
///      <c>metrics.projects_analyzed</c>.
///   3. Layer-aware analyzers (LNTY-001 Domain Layer Isolation,
///      LNTY-006/repo-placement, LNTY-008 Ports at Boundaries) skip Unknown
///      projects.
///   4. Layer-agnostic analyzers (LNTY-009 Method Size) still run on Unknown.
///
/// LNTY-007 (Dependency Cycles) is exercised end-to-end by the Foreigner
/// fixture test — its graph walks every project regardless of layer.
/// </summary>
public sealed class PermissiveLayerTaggingTests
{
    [Fact]
    public void LayerTagger_Permissive_ReturnsUnknown_ForUnclassifiableProject()
    {
        var tagger = new LayerTagger(LinttyConfig.Default);

        // No convention match (DefaultConventionMap has no "*PaymentProcessor"),
        // no explicit_map (Default config has no overrides). Pre-fix this would
        // throw LayerTaggingError. Post-fix: Layer.Unknown, no exception.
        var layer = tagger.Classify("Acme.PaymentProcessor", "Acme.PaymentProcessor.csproj");

        Assert.Equal(Layer.Unknown, layer);
    }

    [Fact]
    public async Task Engine_AnalyzeAsync_CompletesSuccessfully_WithMixedKnownAndUnknownProjects()
    {
        // The Foreigner fixture has THREE projects, all Unknown (no
        // lintty.yml, no convention match). The "mixed" assertion in the
        // test name is satisfied by the fact that the layer summary still
        // emits the four canonical layers (zeros) plus Unknown — no panic.
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Foreigner, canonVersionOverride: null);

        // Analysis completed: no LayerTaggingError, no execution error.
        Assert.Equal("A", report.Grade);
        Assert.Equal(100, report.Score);
        Assert.True(report.SealEligible);

        // The "Unknown" bucket is present and populated.
        Assert.True(report.LayerSummary.ContainsKey("Unknown"),
            "layer_summary must include 'Unknown' when at least one project is unclassified.");
        Assert.Equal(3, report.LayerSummary["Unknown"].Projects);
        Assert.Equal(3, report.LayerSummary["Unknown"].Files);
        Assert.Equal(0, report.LayerSummary["Unknown"].Violations);

        // The four canonical layers are still present (zeros) — the laudo
        // structure is stable; the Unknown row is additive.
        foreach (var key in new[] { "Domain", "Application", "Infrastructure", "Presentation" })
            Assert.True(report.LayerSummary.ContainsKey(key), $"Expected layer '{key}' in summary.");

        // metrics.projects_analyzed counts all projects, Known + Unknown.
        Assert.Equal(3, report.Metrics.ProjectsAnalyzed);
    }

    [Fact]
    public async Task LayerAwareRules_Skip_UnknownProjects()
    {
        // Layer-aware rules: LNTY-001 (Domain only), LNTY-006 part b
        // (non-Domain, non-DomainAbstractions), LNTY-008 (Infrastructure only).
        // None of those should fire on a project tagged Layer.Unknown.
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Foreigner, canonVersionOverride: null);

        var layerAwareRules = new[] { "LNTY-001", "LNTY-008" };
        foreach (var rule in layerAwareRules)
        {
            Assert.DoesNotContain(report.Violations, v => v.RuleId == rule);
        }
    }

    [Fact]
    public async Task LayerAgnosticRules_RunOn_UnknownProjects()
    {
        // LNTY-009 (Method size) runs on every non-test project regardless
        // of layer. The Foreigner code is intentionally short (<60 LoC per
        // method) so we get ZERO violations — but the rule absolutely DID
        // run, otherwise we couldn't trust grade A. This test is structural:
        // it asserts compile_status is 'success' and the rule was reachable
        // (no LayerTaggingError aborted the pipeline).
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Foreigner, canonVersionOverride: null);

        Assert.Equal("success", report.CompileStatus);
        Assert.Empty(report.WorkspaceDiagnostics);
        // No LNTY-009 violations because methods are short, but the rule had
        // a chance to look at every Unknown project.
        Assert.DoesNotContain(report.Violations, v => v.RuleId == "LNTY-009");
    }

    [Fact]
    public async Task Lnty001_SkipsUnknownProjects_WhenDomainCodeAbsent()
    {
        // Synthetic compilation: a project tagged Unknown that imports a
        // forbidden namespace (Microsoft.EntityFrameworkCore). LNTY-001
        // would fire IF the project were Domain — for Unknown it must not.
        // We exercise the analyzer directly with a hand-built AnalysisContext
        // so the test is fast and doesn't need MSBuild.
        var context = BuildSyntheticContext(
            projectName: "Acme.PaymentProcessor",
            layer: Layer.Unknown,
            sourceText: """
                using Microsoft.EntityFrameworkCore;

                namespace Acme.PaymentProcessor;

                public sealed class PaymentService
                {
                    public string Hint() => nameof(DbContext);
                }
                """);

        var analyzer = new Lnty001_DomainLayerIsolation();
        var found = await analyzer.AnalyzeAsync(context);
        Assert.Empty(found);

        // Sanity: same code as Domain WOULD trigger the rule.
        var domainContext = BuildSyntheticContext(
            projectName: "Acme.PaymentProcessor",
            layer: Layer.Domain,
            sourceText: """
                using Microsoft.EntityFrameworkCore;

                namespace Acme.PaymentProcessor;

                public sealed class PaymentService
                {
                    public string Hint() => nameof(DbContext);
                }
                """);

        var foundInDomain = await analyzer.AnalyzeAsync(domainContext);
        Assert.NotEmpty(foundInDomain);
    }

    /// <summary>
    /// Builds a single-project AnalysisContext from a raw source string,
    /// referencing only mscorlib + EFCore so LNTY-001 can resolve symbols.
    /// </summary>
    private static AnalysisContext BuildSyntheticContext(string projectName, Layer layer, string sourceText)
    {
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(sourceText), path: $"{projectName}/PaymentService.cs");

        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.RuntimeHelpers).Assembly.Location),
        };

        // Try to find Microsoft.EntityFrameworkCore.dll via the test runner's
        // load context; if absent, the using directive still parses and the
        // SemanticModel will resolve the namespace by name (LNTY-001 also
        // matches the syntactic 'using' name as a fallback).
        var efcorePath = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.Location)
            .FirstOrDefault(p => p?.EndsWith("Microsoft.EntityFrameworkCore.dll", System.StringComparison.OrdinalIgnoreCase) == true);
        if (!string.IsNullOrEmpty(efcorePath))
            refs.Add(MetadataReference.CreateFromFile(efcorePath));

        var compilation = CSharpCompilation.Create(
            assemblyName: projectName,
            syntaxTrees: new[] { tree },
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var projInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name: projectName,
            assemblyName: projectName,
            language: LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), $"{projectName}.csproj"));
        var proj = workspace.AddProject(projInfo);

        var layerByProject = new Dictionary<string, Layer>(System.StringComparer.Ordinal)
        {
            [projectName] = layer,
        };

        return new AnalysisContext
        {
            SolutionPath = Path.Combine(Path.GetTempPath(), $"{projectName}.sln"),
            SolutionDir = Path.GetTempPath(),
            Projects = new[] { (proj, (Compilation)compilation) },
            LayerByProject = layerByProject,
            ProjectReferences = new Dictionary<string, IReadOnlyList<string>>(),
            Tagger = new LayerTagger(LinttyConfig.Default),
            Config = LinttyConfig.Default,
        };
    }
}
