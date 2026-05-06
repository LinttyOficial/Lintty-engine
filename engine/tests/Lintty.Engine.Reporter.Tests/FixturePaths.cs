using System.IO;

namespace Lintty.Engine.Reporter.Tests;

/// <summary>
/// Walks upward from the test runner cwd to find the repo root containing
/// <c>fixtures/the-saint</c>. Mirrors the helper used by the Core tests so
/// the Reporter tests can stay self-contained.
/// </summary>
internal static class FixturePaths
{
    public static string RepoRoot { get; } = LocateRoot();

    public static string SaintExpectedJson  => Path.Combine(RepoRoot, "fixtures", "the-saint",   "expected.json");
    public static string SinnerExpectedJson => Path.Combine(RepoRoot, "fixtures", "the-sinner",  "expected.json");
    public static string Ninja01ExpectedJson => Path.Combine(RepoRoot, "fixtures", "the-ninja-01", "expected.json");
    public static string SaintNoSlnExpectedJson => Path.Combine(RepoRoot, "fixtures", "the-saint-no-sln", "expected.json");
    public static string ForeignerExpectedJson => Path.Combine(RepoRoot, "fixtures", "the-foreigner", "expected.json");

    private static string LocateRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "fixtures", "the-saint")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root containing 'fixtures/the-saint'.");
    }
}
