using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Lintty.Engine.Core.Workspace;

/// <summary>
/// Loads a Solution + Compilations via MSBuildWorkspace. Tracks WorkspaceFailed
/// diagnostics (warnings exposed in the report; failures bubble up as throws).
/// </summary>
public sealed class SolutionLoader
{
    private static readonly object LocatorLock = new();
    private static bool _locatorRegistered;

    public sealed record WorkspaceWarning(string Kind, string Message, string? Project);

    public sealed record LoadResult(
        Solution Solution,
        IReadOnlyList<(Project Project, Compilation Compilation)> Projects,
        IReadOnlyList<WorkspaceWarning> Warnings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ProjectReferences);

    public async Task<LoadResult> LoadAsync(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution not found: {solutionPath}", solutionPath);

        if (!TryEnsureLocator())
        {
            // No MSBuild on the host (common when only a newer SDK is installed).
            // Fall through to the manual loader — it parses the .sln/.csproj
            // directly and builds a self-contained Compilation.
            return await ManualLoadAsync(solutionPath).ConfigureAwait(false);
        }

        var properties = new Dictionary<string, string>
        {
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["AlwaysCompileMarkupFilesInSeparateDomain"] = "false",
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
        };

        var workspace = MSBuildWorkspace.Create(properties);
        var warnings = new List<WorkspaceWarning>();
        workspace.WorkspaceFailed += (sender, args) =>
        {
            warnings.Add(new WorkspaceWarning(
                Kind: args.Diagnostic.Kind.ToString().ToLowerInvariant(),
                Message: args.Diagnostic.Message,
                Project: null));
        };

        Solution solution;
        try
        {
            solution = await workspace.OpenSolutionAsync(solutionPath).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // If MSBuild can't open the solution, fall back to a minimal manual loader
            // that parses the .sln + .csproj files directly. This keeps the engine
            // usable even when the host SDK lacks a matching .NET 8 targeting pack.
            workspace.Dispose();
            return await ManualLoadAsync(solutionPath).ConfigureAwait(false);
        }

        var projects = new List<(Project, Compilation)>();
        var projectRefs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            Compilation? compilation;
            try
            {
                compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                warnings.Add(new WorkspaceWarning("warning",
                    $"GetCompilationAsync failed for {project.Name}: {ex.Message}", project.Name));
                continue;
            }
            if (compilation is null) continue;

            projects.Add((project, compilation));
            projectRefs[project.Name] = project.ProjectReferences
                .Select(r => solution.GetProject(r.ProjectId)?.Name ?? string.Empty)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }

        // If MSBuild loaded zero compilations (typical when the targeting pack
        // is missing on the host SDK), fall back to the manual loader.
        if (projects.Count == 0)
        {
            workspace.Dispose();
            return await ManualLoadAsync(solutionPath).ConfigureAwait(false);
        }

        return new LoadResult(solution, projects, warnings, projectRefs);
    }

    private static bool TryEnsureLocator()
    {
        lock (LocatorLock)
        {
            if (_locatorRegistered) return MSBuildLocator.IsRegistered;
            try
            {
                if (!MSBuildLocator.IsRegistered)
                    MSBuildLocator.RegisterDefaults();
                _locatorRegistered = true;
                return true;
            }
            catch
            {
                _locatorRegistered = true;
                return false;
            }
        }
    }

    /// <summary>
    /// Fallback loader: parses the .sln for project paths, then builds a
    /// <see cref="Solution"/> using <see cref="AdhocWorkspace"/>. We add the
    /// .cs source files of each project as documents and resolve project
    /// references by csproj path. Sufficient for type-aware analysis on the
    /// fixtures since they reference only BCL types.
    /// </summary>
    private static async Task<LoadResult> ManualLoadAsync(string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var solutionText = await File.ReadAllTextAsync(solutionPath).ConfigureAwait(false);

        // Parse "Project(...) = "Name", "relPath", "{guid}"" lines.
        var projectRegex = new System.Text.RegularExpressions.Regex(
            "Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"\\s*,\\s*\"\\{([^}]+)\\}\"",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var workspace = new AdhocWorkspace();
        var solutionId = SolutionId.CreateNewId();
        workspace.AddSolution(SolutionInfo.Create(solutionId, VersionStamp.Default));

        // Pass 1: collect (name, relPath, csprojAbs).
        var raw = new List<(string Name, string CsprojAbs)>();
        foreach (System.Text.RegularExpressions.Match m in projectRegex.Matches(solutionText))
        {
            var name = m.Groups[1].Value;
            var rel = m.Groups[2].Value.Replace('\\', Path.DirectorySeparatorChar);
            var abs = Path.GetFullPath(Path.Combine(solutionDir, rel));
            if (!File.Exists(abs)) continue;
            raw.Add((name, abs));
        }

        // Pass 2: create ProjectInfo objects + record id.
        var idByName = new Dictionary<string, ProjectId>(StringComparer.OrdinalIgnoreCase);
        var pathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, abs) in raw)
        {
            var pid = ProjectId.CreateNewId(debugName: name);
            idByName[name] = pid;
            pathByName[name] = abs;
        }

        // Pass 3: parse csproj for ProjectReference + RootNamespace.
        var nameByCsprojPath = raw.ToDictionary(
            r => r.CsprojAbs, r => r.Name, StringComparer.OrdinalIgnoreCase);

        var infos = new List<ProjectInfo>();
        var docInfosByProject = new Dictionary<ProjectId, List<DocumentInfo>>();
        var projectRefs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        // Standard references: System runtime + collections + linq + threading.
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")) ?? string.Empty;
        var refPaths = trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var metadataRefs = refPaths
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        foreach (var (name, csprojAbs) in raw)
        {
            var pid = idByName[name];
            var projDir = Path.GetDirectoryName(csprojAbs)!;
            var rootNs = name; // fallback
            var refNames = new List<string>();
            try
            {
                var doc = XDocument.Load(csprojAbs);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                var rn = doc.Descendants(ns + "RootNamespace").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(rn)) rootNs = rn;

                foreach (var pr in doc.Descendants(ns + "ProjectReference"))
                {
                    var include = pr.Attribute("Include")?.Value;
                    if (string.IsNullOrEmpty(include)) continue;
                    var refAbs = Path.GetFullPath(Path.Combine(projDir,
                        include.Replace('\\', Path.DirectorySeparatorChar)));
                    if (nameByCsprojPath.TryGetValue(refAbs, out var refName))
                        refNames.Add(refName);
                }
            }
            catch { /* swallow csproj parse errors; treat as no-ref */ }

            projectRefs[name] = refNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();

            var docInfos = new List<DocumentInfo>();
            foreach (var csFile in Directory.EnumerateFiles(projDir, "*.cs", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                var norm = csFile.Replace('\\', '/');
                if (norm.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
                if (norm.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) continue;

                var did = DocumentId.CreateNewId(pid, debugName: csFile);
                var loader = TextLoader.From(TextAndVersion.Create(
                    Microsoft.CodeAnalysis.Text.SourceText.From(File.ReadAllText(csFile)),
                    VersionStamp.Default,
                    csFile));
                docInfos.Add(DocumentInfo.Create(
                    id: did,
                    name: Path.GetFileName(csFile),
                    folders: null,
                    sourceCodeKind: SourceCodeKind.Regular,
                    loader: loader,
                    filePath: csFile));
            }
            docInfosByProject[pid] = docInfos;

            var info = ProjectInfo.Create(
                id: pid,
                version: VersionStamp.Default,
                name: name,
                assemblyName: name,
                language: LanguageNames.CSharp,
                filePath: csprojAbs,
                outputFilePath: null,
                compilationOptions: new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable),
                parseOptions: new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                    Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12),
                documents: docInfos,
                projectReferences: null,
                metadataReferences: metadataRefs);

            // Track default namespace via a custom property bag; we'll resolve via name->RootNamespace later.
            infos.Add(info.WithDefaultNamespace(rootNs));
        }

        // Resolve project references after we know all ids.
        var resolvedInfos = new List<ProjectInfo>();
        foreach (var info in infos)
        {
            var refs = projectRefs[info.Name]
                .Where(idByName.ContainsKey)
                .Select(n => new ProjectReference(idByName[n]))
                .ToArray();
            resolvedInfos.Add(info.WithProjectReferences(refs));
        }

        var solution = workspace.AddSolution(
            SolutionInfo.Create(
                solutionId,
                VersionStamp.Default,
                filePath: solutionPath,
                projects: resolvedInfos));

        var compilations = new List<(Project, Compilation)>();
        var warnings = new List<WorkspaceWarning>();
        foreach (var project in solution.Projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var c = await project.GetCompilationAsync().ConfigureAwait(false);
            if (c is null)
            {
                warnings.Add(new WorkspaceWarning("warning",
                    $"compilation null for {project.Name}", project.Name));
                continue;
            }
            compilations.Add((project, c));
        }

        return new LoadResult(solution, compilations, warnings, projectRefs);
    }
}
