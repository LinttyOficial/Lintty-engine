using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lintty.Engine.Core.Tagging;

namespace Lintty.Engine.Core.Workspace;

/// <summary>
/// Resolves the analysis scope (target) following ADR 0006 §5. Accepts either
/// a <c>.sln</c>, a <c>.csproj</c>, a <c>lintty.yml</c> path, or a directory,
/// and returns a <see cref="ResolvedTarget"/> the engine can consume.
///
/// Mode (<see cref="TargetResolverMode.Cli"/> vs
/// <see cref="TargetResolverMode.WebInspector"/>) controls a single delta: the
/// implicit "exactly 1 .csproj in the tree" exception is accepted only in
/// WebInspector mode (§5.4). The CLI requires the user to be explicit.
///
/// Errors are signalled via <see cref="TargetResolutionException"/> with a
/// canonical <see cref="TargetResolutionException.ErrorCode"/> (§5.2). The CLI
/// surfaces them in stderr and exits 2; the JobWorker maps them to
/// <c>JobErrorCode</c> values per §8.1.
/// </summary>
public static class TargetResolver
{
    /// <summary>
    /// Resolves <paramref name="targetArg"/> against <paramref name="cwd"/>.
    /// </summary>
    /// <param name="targetArg">
    /// User-supplied path (file or directory). When null/empty, the resolver
    /// falls back to <paramref name="cwd"/>.
    /// </param>
    /// <param name="cwd">
    /// Working directory used as the fallback target. Required so the worker
    /// can pass a sandbox path explicitly without depending on
    /// <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
    /// <param name="mode">
    /// <see cref="TargetResolverMode.Cli"/> for the local CLI;
    /// <see cref="TargetResolverMode.WebInspector"/> for the worker (enables
    /// the 1-csproj implicit fallback).
    /// </param>
    public static ResolvedTarget Resolve(string? targetArg, string cwd, TargetResolverMode mode)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            throw new ArgumentException("cwd is required.", nameof(cwd));

        // Step 1 — explicit flag.
        if (!string.IsNullOrWhiteSpace(targetArg))
        {
            var target = targetArg!;
            // .sln file
            if (target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(target))
                    throw NewError(TargetResolutionErrorCode.TargetNotFound, $"Target not found: {target}");
                var abs = Path.GetFullPath(target);
                return ResolvedTarget.ForSolution(abs);
            }

            // .csproj file
            if (target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(target))
                    throw NewError(TargetResolutionErrorCode.TargetNotFound, $"Target not found: {target}");
                var abs = Path.GetFullPath(target);
                return ResolvedTarget.ForProjectList(
                    new[] { abs },
                    solutionPathForReporting: abs,
                    yamlDir: null);
            }

            // Explicit lintty.yml path.
            if (File.Exists(target)
                && string.Equals(Path.GetFileName(target), "lintty.yml", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveFromYaml(Path.GetFullPath(target));
            }

            // Existing directory.
            if (Directory.Exists(target))
            {
                return ResolveFromDir(Path.GetFullPath(target), mode);
            }

            // Anything else.
            throw NewError(TargetResolutionErrorCode.TargetNotFound, $"Target not found: {target}");
        }

        // Step 2 — no flag, fall back to cwd.
        return ResolveFromDir(Path.GetFullPath(cwd), mode);
    }

    private static ResolvedTarget ResolveFromDir(string dir, TargetResolverMode mode)
    {
        // 2a — .sln in the root takes precedence (back-compat).
        var slns = Directory
            .EnumerateFiles(dir, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var yamlPath = Path.Combine(dir, "lintty.yml");
        var hasYaml = File.Exists(yamlPath);
        var yamlConfig = hasYaml ? LinttyConfig.LoadOrDefault(yamlPath) : null;
        var yamlDeclaresProjects = yamlConfig is not null && yamlConfig.Projects.Count > 0;

        if (slns.Count >= 1)
        {
            if (slns.Count > 1)
            {
                var list = string.Join(", ", slns.Select(Path.GetFileName));
                throw NewError(
                    TargetResolutionErrorCode.AmbiguousTargetMultipleSlns,
                    $"Multiple .sln files found in {dir}: {list}. Pick one with --target.");
            }

            if (yamlDeclaresProjects)
            {
                throw NewError(
                    TargetResolutionErrorCode.AmbiguousTargetSlnAndProjects,
                    $"Both {slns[0]} and 'projects:' in {yamlPath} declare scope. " +
                    "Remove 'projects:' or analyze the .csproj list directly with --target <path/to/lintty.yml>.");
            }

            return ResolvedTarget.ForSolution(slns[0]);
        }

        // 2b — no .sln; try lintty.yml with projects:
        if (hasYaml)
        {
            return ResolveFromYaml(yamlPath);
        }

        // 2c — Web Inspector only: lone .csproj exception.
        if (mode == TargetResolverMode.WebInspector)
        {
            var csprojs = Directory
                .EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !ContainsObjOrBin(p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (csprojs.Count == 1)
            {
                return ResolvedTarget.ForProjectList(
                    new[] { csprojs[0] },
                    solutionPathForReporting: csprojs[0],
                    yamlDir: null);
            }
            if (csprojs.Count >= 2)
            {
                throw NewError(
                    TargetResolutionErrorCode.AmbiguousTargetMultipleCsprojs,
                    "Multiple .csproj files found and no .sln or lintty.yml. " +
                    "Add a lintty.yml with 'projects:' to declare scope.");
            }
        }

        // Fall through: nothing usable.
        var hint = mode == TargetResolverMode.Cli
            ? "Provide --target <path/to/.sln | .csproj | lintty.yml> or add a 'projects:' list to lintty.yml."
            : "Add a lintty.yml with 'projects:' to declare scope, or use the local CLI.";
        throw NewError(
            TargetResolutionErrorCode.NoTarget,
            $"No target found in {dir}. {hint}");
    }

    private static ResolvedTarget ResolveFromYaml(string yamlPath)
    {
        var yamlDir = Path.GetDirectoryName(yamlPath)!;
        var config = LinttyConfig.LoadOrDefault(yamlPath);

        if (config.Projects.Count == 0)
        {
            throw NewError(
                TargetResolutionErrorCode.NoTarget,
                $"No target found in {yamlDir}. " +
                "Add a 'projects:' list to lintty.yml or place a .sln in the same directory.");
        }

        var resolved = ValidateAndResolveProjects(config.Projects, yamlDir);
        return ResolvedTarget.ForProjectList(
            resolved,
            solutionPathForReporting: yamlPath,
            yamlDir: yamlDir);
    }

    /// <summary>
    /// Validates each entry in <c>lintty.yml.projects:</c> per ADR 0006 §4.2
    /// and returns absolute paths, preserving the literal declaration order.
    /// </summary>
    internal static IReadOnlyList<string> ValidateAndResolveProjects(
        IReadOnlyList<string> rawEntries,
        string yamlDir)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<string>(rawEntries.Count);
        var yamlDirAbs = Path.GetFullPath(yamlDir);

        for (var i = 0; i < rawEntries.Count; i++)
        {
            var entry = rawEntries[i];
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw NewError(
                    TargetResolutionErrorCode.InvalidProjectsEntry,
                    $"projects[{i}] is empty.");
            }

            // Reject globs (any '*' or '?' anywhere in the path).
            if (entry.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                throw NewError(
                    TargetResolutionErrorCode.InvalidProjectsEntry,
                    $"globs are not supported in v1.0; declare each .csproj explicitly. Got: {entry}");
            }

            // Reject absolute paths.
            if (Path.IsPathRooted(entry))
            {
                throw NewError(
                    TargetResolutionErrorCode.InvalidProjectsEntry,
                    $"projects[{i}] must be a relative path; got absolute: {entry}");
            }

            // Must end in .csproj.
            if (!entry.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                throw NewError(
                    TargetResolutionErrorCode.InvalidProjectsEntry,
                    $"projects[{i}] must end in .csproj; got: {entry}");
            }

            // Resolve against yaml dir.
            var combined = Path.Combine(yamlDirAbs, entry);
            var abs = Path.GetFullPath(combined);

            // Containment check: must remain within yamlDir.
            // Compare with trailing separator so e.g. /foo doesn't match /foo-bar.
            var sep = Path.DirectorySeparatorChar;
            var normalisedYamlDir = yamlDirAbs.TrimEnd(sep) + sep;
            var normalisedAbs = abs;
            if (!(normalisedAbs + sep).StartsWith(normalisedYamlDir, StringComparison.OrdinalIgnoreCase))
            {
                throw NewError(
                    TargetResolutionErrorCode.InvalidProjectsEntry,
                    $"projects[{i}] must not escape the lintty.yml directory; got: {entry}");
            }

            // Duplicate check (case-insensitive on disk paths).
            if (!seen.Add(abs))
            {
                throw NewError(
                    TargetResolutionErrorCode.InvalidProjectsEntry,
                    $"projects[] contains duplicate: {entry}");
            }

            // Existence check.
            if (!File.Exists(abs))
            {
                throw NewError(
                    TargetResolutionErrorCode.TargetNotFound,
                    $"projects[{i}] not found relative to lintty.yml: {entry}");
            }

            resolved.Add(abs);
        }

        return resolved;
    }

    private static bool ContainsObjOrBin(string path)
    {
        var norm = path.Replace('\\', '/');
        return norm.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || norm.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static TargetResolutionException NewError(string code, string message)
        => new(code, message);
}

/// <summary>
/// Resolver mode toggle (ADR 0006 §5.4): only WebInspector mode honors the
/// implicit "single .csproj in the tree" exception.
/// </summary>
public enum TargetResolverMode
{
    Cli,
    WebInspector,
}

/// <summary>
/// Discriminated union returned by <see cref="TargetResolver.Resolve"/>.
/// Either <see cref="SolutionPath"/> is non-null (load via .sln) or
/// <see cref="ProjectListPaths"/> is non-empty (load declared projects).
/// </summary>
public sealed record ResolvedTarget
{
    /// <summary>Absolute path to a <c>.sln</c> file, or null when this is a project-list target.</summary>
    public string? SolutionPath { get; init; }

    /// <summary>
    /// Absolute paths of <c>.csproj</c> files to load, in declaration order
    /// (ADR 0006 §4.3). Empty when <see cref="SolutionPath"/> is set.
    /// </summary>
    public IReadOnlyList<string> ProjectListPaths { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Path that should be reported in <c>report.solution_path</c>. Always
    /// absolute on the host; the engine renders it relative to its own
    /// reporting base (ADR 0006 §6.1):
    /// <list type="bullet">
    ///   <item><description>.sln mode: the .sln path itself.</description></item>
    ///   <item><description>1-csproj mode: the .csproj path.</description></item>
    ///   <item><description>lintty.yml-driven: the lintty.yml path.</description></item>
    /// </list>
    /// </summary>
    public string SolutionPathForReporting { get; init; } = string.Empty;

    /// <summary>
    /// Directory of the <c>lintty.yml</c> file, when one drove resolution.
    /// Null when the target is a .sln, a bare .csproj on the CLI, or the
    /// 1-csproj WebInspector exception.
    /// </summary>
    public string? YamlDir { get; init; }

    /// <summary>True when <see cref="SolutionPath"/> is set.</summary>
    public bool IsSolution => SolutionPath is not null;

    public static ResolvedTarget ForSolution(string slnAbs)
        => new()
        {
            SolutionPath = slnAbs,
            SolutionPathForReporting = slnAbs,
        };

    public static ResolvedTarget ForProjectList(
        IReadOnlyList<string> csprojAbsList,
        string solutionPathForReporting,
        string? yamlDir)
        => new()
        {
            ProjectListPaths = csprojAbsList,
            SolutionPathForReporting = solutionPathForReporting,
            YamlDir = yamlDir,
        };
}

/// <summary>
/// Stable error codes for <see cref="TargetResolutionException"/>. Mirror the
/// table in ADR 0006 §5.2; consumers may compare with <see cref="string.Equals(string, string, StringComparison)"/>.
/// </summary>
public static class TargetResolutionErrorCode
{
    public const string NoTarget = "no_target";
    public const string TargetNotFound = "target_not_found";
    public const string AmbiguousTargetMultipleSlns = "ambiguous_target_multiple_slns";
    public const string AmbiguousTargetMultipleCsprojs = "ambiguous_target_multiple_csprojs";
    public const string AmbiguousTargetSlnAndProjects = "ambiguous_target_sln_and_projects";
    public const string InvalidProjectsEntry = "invalid_projects_entry";
}

/// <summary>
/// Typed exception thrown by <see cref="TargetResolver"/>. Carries a stable
/// <see cref="ErrorCode"/> consumers can branch on without parsing
/// <see cref="Exception.Message"/>.
/// </summary>
public sealed class TargetResolutionException : Exception
{
    public string ErrorCode { get; }

    public TargetResolutionException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
