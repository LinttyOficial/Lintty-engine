using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lintty.Engine.Core.Output;
using Lintty.Engine.Core.Workspace;
using Xunit;

namespace Lintty.Engine.Core.Tests;

/// <summary>
/// ADR 0006 §9.3: running the engine against the lintty.yml-driven fixture
/// must produce the same observable analysis result as the .sln-driven Saint
/// fixture, modulo <c>solution_path</c> (and the deterministic ids derived
/// from it). Same code, same scope -> same verdict.
/// </summary>
public sealed class SaintNoSlnFixtureTests
{
    [Fact]
    public async Task Resolves_Like_Saint()
    {
        var resolved = TargetResolver.Resolve(
            targetArg: FixturePaths.SaintNoSlnYaml,
            cwd: Directory.GetCurrentDirectory(),
            mode: TargetResolverMode.Cli);

        Assert.False(resolved.IsSolution);
        Assert.Equal(4, resolved.ProjectListPaths.Count);

        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(resolved, canonVersionOverride: null);

        // Same observable analysis result as Saint:
        Assert.Equal("A", report.Grade);
        Assert.Equal(100, report.Score);
        Assert.True(report.SealEligible);
        Assert.Empty(report.HardLocksHit);
        Assert.Empty(report.Violations);

        // But solution_path now points at the YAML, not the .sln.
        Assert.Equal("lintty.yml", report.SolutionPath);

        // Layer summary and metrics match Saint byte-for-byte.
        Assert.Equal(4, report.Metrics.ProjectsAnalyzed);
        Assert.Equal(289, report.Metrics.TotalSlocPhysical);

        // ── JSON-level cross-check: serialise and compare to expected.json.
        // We use the compact form (the same shape the CLI writes when no
        // --output-file is given). Match must be byte-for-byte.
        var json = JsonReport.Serialize(report, indented: false);
        var expectedPath = Path.Combine(FixturePaths.SaintNoSlnDir, "expected.json");
        var expected = File.ReadAllText(expectedPath).TrimEnd('\r', '\n');
        Assert.Equal(expected, json);
    }
}
