using System;
using System.IO;
using System.Linq;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// Walks upward from the test runner cwd to find the repo root and from
/// there resolves fixture paths and the engine CLI dll. Mirrors the helper
/// in <c>Lintty.Engine.Reporter.Tests/FixturePaths.cs</c>.
/// </summary>
internal static class TestPaths
{
    public static string RepoRoot { get; } = LocateRoot();

    public static string FixturesDir => Path.Combine(RepoRoot, "fixtures");
    public static string SaintFixture  => Path.Combine(FixturesDir, "the-saint");
    public static string SinnerFixture => Path.Combine(FixturesDir, "the-sinner");
    public static string SaintNoSlnFixture => Path.Combine(FixturesDir, "the-saint-no-sln");
    public static string ForeignerFixture => Path.Combine(FixturesDir, "the-foreigner");

    /// <summary>
    /// Returns the path to <c>lintty-engine.dll</c> built by the sibling
    /// <c>Lintty.Engine.Cli</c> project. Looks in Release first, then Debug.
    /// </summary>
    public static string EngineCliDll
    {
        get
        {
            var cliBin = Path.Combine(RepoRoot, "engine", "src", "Lintty.Engine.Cli", "bin");
            foreach (var cfg in new[] { "Release", "Debug" })
            {
                var path = Path.Combine(cliBin, cfg, "net8.0", "lintty-engine.dll");
                if (File.Exists(path)) return path;
            }
            // Last-ditch: pick whatever copy we find under bin/.
            var found = Directory.EnumerateFiles(cliBin, "lintty-engine.dll", SearchOption.AllDirectories)
                .OrderBy(p => p)
                .FirstOrDefault();
            if (found is null)
                throw new FileNotFoundException(
                    $"lintty-engine.dll not found under {cliBin}. Build the Lintty.Engine.Cli project first.");
            return found;
        }
    }

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
