using System;
using System.IO;
using System.Linq;
using Lintty.Engine.Core.Workspace;
using Xunit;

namespace Lintty.Engine.Core.Tests;

/// <summary>
/// Coverage for ADR 0006 §5.1 / §5.2. Each test scaffolds a temporary
/// directory representing one of the canonical scenarios in §5.5 and asserts
/// the resolver's decision.
/// </summary>
public sealed class TargetResolverTests
{
    [Fact]
    public void Sln_Path_Wins_When_Both_Present()
    {
        // .sln in the root + lintty.yml with projects: -> ambiguous_target_sln_and_projects.
        using var dir = TempDir.New();
        var slnPath = Path.Combine(dir.Path, "Foo.sln");
        File.WriteAllText(slnPath, "Microsoft Visual Studio Solution File, Format Version 12.00\n");
        var csprojPath = WriteFakeCsproj(dir.Path, "Foo.Domain");
        File.WriteAllText(Path.Combine(dir.Path, "lintty.yml"),
            "canon_version: 1.0.0\nprojects:\n  - " + RelativeFromYaml(dir.Path, csprojPath) + "\n");

        var ex = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.Cli));
        Assert.Equal(TargetResolutionErrorCode.AmbiguousTargetSlnAndProjects, ex.ErrorCode);
    }

    [Fact]
    public void Empty_Dir_Yields_NoTarget()
    {
        using var dir = TempDir.New();
        var ex = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.Cli));
        Assert.Equal(TargetResolutionErrorCode.NoTarget, ex.ErrorCode);
    }

    [Fact]
    public void Multiple_Slns_Yields_AmbiguousTarget()
    {
        using var dir = TempDir.New();
        File.WriteAllText(Path.Combine(dir.Path, "Foo.sln"), string.Empty);
        File.WriteAllText(Path.Combine(dir.Path, "Bar.sln"), string.Empty);

        var ex = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.Cli));
        Assert.Equal(TargetResolutionErrorCode.AmbiguousTargetMultipleSlns, ex.ErrorCode);
    }

    [Fact]
    public void Single_Csproj_WebInspector_Mode_Resolves()
    {
        // ADR 0006 §5.4: 1-csproj implicit fallback is exclusive to WebInspector.
        using var dir = TempDir.New();
        var csprojPath = WriteFakeCsproj(Path.Combine(dir.Path, "src", "Lone"), "Lone");

        var web = TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.WebInspector);
        Assert.False(web.IsSolution);
        Assert.Single(web.ProjectListPaths);
        Assert.Equal(Path.GetFullPath(csprojPath), web.ProjectListPaths[0]);
        Assert.Equal(csprojPath, web.SolutionPathForReporting);

        // CLI mode should refuse — explicit only.
        var cliEx = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.Cli));
        Assert.Equal(TargetResolutionErrorCode.NoTarget, cliEx.ErrorCode);
    }

    [Fact]
    public void Two_Csprojs_WebInspector_Mode_Yields_Ambiguous()
    {
        using var dir = TempDir.New();
        WriteFakeCsproj(Path.Combine(dir.Path, "src", "A"), "A");
        WriteFakeCsproj(Path.Combine(dir.Path, "src", "B"), "B");

        var ex = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.WebInspector));
        Assert.Equal(TargetResolutionErrorCode.AmbiguousTargetMultipleCsprojs, ex.ErrorCode);
    }

    [Fact]
    public void Projects_With_Glob_Yields_InvalidConfig()
    {
        using var dir = TempDir.New();
        File.WriteAllText(Path.Combine(dir.Path, "lintty.yml"),
            "canon_version: 1.0.0\nprojects:\n  - src/**/*.csproj\n");

        var ex = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.Cli));
        Assert.Equal(TargetResolutionErrorCode.InvalidProjectsEntry, ex.ErrorCode);
        Assert.Contains("globs are not supported", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Projects_With_Absolute_Path_Yields_InvalidConfig()
    {
        using var dir = TempDir.New();
        // Use a Windows-rooted path for portability — Path.IsPathRooted treats
        // any path that starts with a drive letter or '/' as rooted on Windows.
        var rooted = OperatingSystem.IsWindows() ? "C:/etc/passwd.csproj" : "/etc/passwd.csproj";
        File.WriteAllText(Path.Combine(dir.Path, "lintty.yml"),
            "canon_version: 1.0.0\nprojects:\n  - " + rooted + "\n");

        var ex = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.Cli));
        Assert.Equal(TargetResolutionErrorCode.InvalidProjectsEntry, ex.ErrorCode);
        Assert.Contains("absolute", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projects_With_Path_Escape_Yields_InvalidConfig()
    {
        using var dir = TempDir.New();
        File.WriteAllText(Path.Combine(dir.Path, "lintty.yml"),
            "canon_version: 1.0.0\nprojects:\n  - ../escape/Other.csproj\n");

        var ex = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.Cli));
        Assert.Equal(TargetResolutionErrorCode.InvalidProjectsEntry, ex.ErrorCode);
        Assert.Contains("escape", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_Project_File_Yields_TargetNotFound()
    {
        using var dir = TempDir.New();
        File.WriteAllText(Path.Combine(dir.Path, "lintty.yml"),
            "canon_version: 1.0.0\nprojects:\n  - src/DoesNotExist/DoesNotExist.csproj\n");

        var ex = Assert.Throws<TargetResolutionException>(() =>
            TargetResolver.Resolve(targetArg: null, cwd: dir.Path, mode: TargetResolverMode.Cli));
        Assert.Equal(TargetResolutionErrorCode.TargetNotFound, ex.ErrorCode);
    }

    private static string WriteFakeCsproj(string projDir, string name)
    {
        Directory.CreateDirectory(projDir);
        var path = Path.Combine(projDir, name + ".csproj");
        File.WriteAllText(path,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net8.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n");
        return path;
    }

    private static string RelativeFromYaml(string yamlDir, string filePath)
    {
        var rel = Path.GetRelativePath(yamlDir, filePath);
        return rel.Replace('\\', '/');
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        private TempDir(string path) { Path = path; }

        public static TempDir New()
        {
            var p = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "lintty-resolver-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(p);
            return new TempDir(p);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
