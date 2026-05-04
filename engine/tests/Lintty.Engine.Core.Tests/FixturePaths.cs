using System.IO;

namespace Lintty.Engine.Core.Tests;

/// <summary>
/// Helper to locate the repo-root /fixtures folder regardless of where the
/// test runner sets the cwd. Walks upward looking for "fixtures/the-saint".
/// </summary>
public static class FixturePaths
{
    public static string RepoRoot { get; } = LocateRoot();

    public static string Saint => Path.Combine(RepoRoot, "fixtures", "the-saint", "Saint.sln");
    public static string Sinner => Path.Combine(RepoRoot, "fixtures", "the-sinner", "Sinner.sln");
    public static string Ninja01 => Path.Combine(RepoRoot, "fixtures", "the-ninja-01", "Ninja01.sln");
    public static string SaintNoSlnDir => Path.Combine(RepoRoot, "fixtures", "the-saint-no-sln");
    public static string SaintNoSlnYaml => Path.Combine(SaintNoSlnDir, "lintty.yml");

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
