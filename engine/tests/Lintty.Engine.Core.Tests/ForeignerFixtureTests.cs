using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lintty.Engine.Core.Output;
using Xunit;

namespace Lintty.Engine.Core.Tests;

/// <summary>
/// Foreigner fixture: three projects with names that match no convention
/// pattern, no <c>lintty.yml</c>. Permissive layer-tagging (2026-05-06)
/// must produce a complete laudo: grade A, an "Unknown" row in
/// <c>layer_summary</c>, no exception.
///
/// The byte-for-byte JSON match against <c>expected.json</c> is the same
/// regression contract the other fixture tests rely on — any drift in
/// counts, ordering, or new fields will fail the test loudly.
/// </summary>
public sealed class ForeignerFixtureTests
{
    [Fact]
    public async Task Foreigner_ProducesGradeA_With_UnknownLayerSummary()
    {
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Foreigner, canonVersionOverride: null);

        Assert.Equal("A", report.Grade);
        Assert.Equal(100, report.Score);
        Assert.True(report.SealEligible);
        Assert.Empty(report.HardLocksHit);
        Assert.Empty(report.Violations);

        // The diagnostic the user must see in the laudo: layer_summary
        // surfaces the Unknown bucket with the project count, so the PDF
        // CTA can ask the user to add a lintty.yml.
        Assert.True(report.LayerSummary.ContainsKey("Unknown"));
        Assert.Equal(3, report.LayerSummary["Unknown"].Projects);
        Assert.Equal(3, report.LayerSummary["Unknown"].Files);
        Assert.Equal(0, report.LayerSummary["Unknown"].Violations);

        // metrics.sloc_per_layer carries the unknown bucket too.
        Assert.True(report.Metrics.SlocPerLayer.ContainsKey("unknown"));
        Assert.True(report.Metrics.SlocPerLayer["unknown"] > 0);

        // Solution path is the basename only (deterministic).
        Assert.Equal("Foreigner.sln", report.SolutionPath);
    }

    [Fact]
    public async Task Foreigner_JsonMatches_ExpectedFixture_ByteForByte()
    {
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Foreigner, canonVersionOverride: null);

        var json = JsonReport.Serialize(report, indented: false);
        var expectedPath = Path.Combine(FixturePaths.ForeignerDir, "expected.json");
        var expected = File.ReadAllText(expectedPath).TrimEnd('\r', '\n');
        Assert.Equal(expected, json);
    }
}
