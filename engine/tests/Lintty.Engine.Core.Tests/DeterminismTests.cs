using System.Threading.Tasks;
using Lintty.Engine.Core.Output;
using Xunit;

namespace Lintty.Engine.Core.Tests;

public sealed class DeterminismTests
{
    [Fact]
    public async Task Sinner_TwoRuns_ProduceByteIdenticalJson()
    {
        var engine = new LinttyEngine();
        var report1 = await engine.AnalyzeAsync(FixturePaths.Sinner, canonVersionOverride: null);
        var report2 = await engine.AnalyzeAsync(FixturePaths.Sinner, canonVersionOverride: null);

        var json1 = JsonReport.Serialize(report1, indented: false);
        var json2 = JsonReport.Serialize(report2, indented: false);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public async Task Saint_TwoRuns_ProduceByteIdenticalJson()
    {
        var engine = new LinttyEngine();
        var report1 = await engine.AnalyzeAsync(FixturePaths.Saint, canonVersionOverride: null);
        var report2 = await engine.AnalyzeAsync(FixturePaths.Saint, canonVersionOverride: null);

        var json1 = JsonReport.Serialize(report1, indented: false);
        var json2 = JsonReport.Serialize(report2, indented: false);

        Assert.Equal(json1, json2);
    }
}
