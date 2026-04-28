using System.Threading.Tasks;
using Lintty.Engine.Cli;
using Xunit;

namespace Lintty.Engine.Cli.Tests;

public sealed class CliTests
{
    [Fact]
    public void ShouldFail_GradeAtThreshold_ReturnsTrue()
    {
        Assert.True(Program.ShouldFail("D", "D"));
        Assert.True(Program.ShouldFail("F", "D"));
    }

    [Fact]
    public void ShouldFail_GradeBetterThanThreshold_ReturnsFalse()
    {
        Assert.False(Program.ShouldFail("A", "D"));
        Assert.False(Program.ShouldFail("B", "D"));
        Assert.False(Program.ShouldFail("C", "D"));
    }
}
