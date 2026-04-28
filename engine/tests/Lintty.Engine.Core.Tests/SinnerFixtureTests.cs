using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.Engine.Core.Tests;

public sealed class SinnerFixtureTests
{
    [Fact]
    public async Task Sinner_ProducesGradeF_WithThreeHardLocks()
    {
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Sinner, canonVersionOverride: null);

        Assert.Equal("F", report.Grade);
        Assert.False(report.SealEligible);

        Assert.Contains("LNTY-001", report.HardLocksHit);
        Assert.Contains("LNTY-002", report.HardLocksHit);
        Assert.Contains("LNTY-007", report.HardLocksHit);

        Assert.True(report.Violations.Count >= 9,
            $"Expected ≥9 violations, got {report.Violations.Count}.");
    }

    [Fact]
    public async Task Sinner_HasViolationsForKeyRules()
    {
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Sinner, canonVersionOverride: null);

        var rules = report.Violations.Select(v => v.RuleId).Distinct().ToHashSet();
        Assert.Contains("LNTY-001", rules);
        Assert.Contains("LNTY-002", rules);
        Assert.Contains("LNTY-003", rules);
        Assert.Contains("LNTY-006", rules);
        Assert.Contains("LNTY-007", rules);
        Assert.Contains("LNTY-008", rules);
        Assert.Contains("LNTY-009", rules);
    }
}
