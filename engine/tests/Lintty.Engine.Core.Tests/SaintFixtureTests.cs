using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.Engine.Core.Tests;

public sealed class SaintFixtureTests
{
    [Fact]
    public async Task Saint_ProducesGradeA_NoViolations()
    {
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Saint, canonVersionOverride: null);

        Assert.Equal("A", report.Grade);
        Assert.Equal(100, report.Score);
        Assert.True(report.SealEligible);
        Assert.Empty(report.HardLocksHit);
        Assert.Empty(report.Violations);
    }
}
