using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.Engine.Core.Tests;

public sealed class Ninja01FixtureTests
{
    [Fact]
    public async Task Ninja01_DetectsConstantFoldedSqlAsLnty002()
    {
        var engine = new LinttyEngine();
        var report = await engine.AnalyzeAsync(FixturePaths.Ninja01, canonVersionOverride: null);

        var lnty002 = report.Violations.Where(v => v.RuleId == "LNTY-002").ToList();
        Assert.True(lnty002.Count >= 1,
            $"Expected ≥1 LNTY-002 via constant folding, got {lnty002.Count}.");
        Assert.Contains(lnty002, v => v.Evidence.AdditionalContext.ContainsKey("constant_value"));
        Assert.Equal("F", report.Grade);
    }
}
