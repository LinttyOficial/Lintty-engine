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
///
/// Two entry points (ADR 0006 §6.1):
///   * <see cref="LoadFromSolutionAsync"/> — given a <c>.sln</c>, mirrors the
///     pre-ADR-0006 behaviour.
///   * <see cref="LoadFromProjectListAsync"/> — given an explicit list of
///     <c>.csproj</c> paths (declared in <c>lintty.yml</c> or a single
///     <c>--target Foo.csproj</c>), builds the same <see cref="LoadResult"/>
///     shape via <see cref="AdhocWorkspace"/>.
///
/// <see cref="LoadAsync"/> remains as a thin alias of
/// <see cref="LoadFromSolutionAsync"/> for backward compatibility with callers
/// that have a <c>.sln</c> in hand.
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

    public Task<LoadResult> LoadAsync(string solutionPath)
        => LoadFromSolutionAsync(solutionPath);

    /// <summary>
    /// Loads a workspace from a <c>.sln</c> file. Tries MSBuildWorkspace first;
    /// falls back to a manual <see cref="AdhocWorkspace"/> built from the
    /// <c>.sln</c> project list when MSBuild is unusable on the host.
    /// </summary>
    public async Task<LoadResult> LoadFromSolutionAsync(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution not found: {solutionPath}", solutionPath);

        if (!TryEnsureLocator())
        {
            // No MSBuild on the host (common when only a newer SDK is installed).
            // Fall through to the manual loader — it parses the .sln/.csproj
            // directly and builds a self-contained Compilation.
            return await ManualLoadFromSolutionAsync(solutionPath).ConfigureAwait(false);
        }

        var (workspace, warnings) = OpenMsBuildWorkspace();

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
            return await ManualLoadFromSolutionAsync(solutionPath).ConfigureAwait(false);
        }

        var (projects, projectRefs) = await CollectProjectsAndCompilationsAsync(solution, warnings).ConfigureAwait(false);

        // If MSBuild loaded zero compilations (typical when the targeting pack
        // is missing on the host SDK), fall back to the manual loader.
        if (projects.Count == 0)
        {
            workspace.Dispose();
            return await ManualLoadFromSolutionAsync(solutionPath).ConfigureAwait(false);
        }

        return new LoadResult(solution, projects, warnings, projectRefs);
    }

    /// <summary>
    /// Creates an MSBuildWorkspace with the canonical design-time properties
    /// and a WorkspaceFailed handler that captures non-fatal diagnostics into
    /// <c>warnings</c>. Centralised so the fail-fast policy stays in one
    /// place.
    /// </summary>
    private static (MSBuildWorkspace Workspace, List<WorkspaceWarning> Warnings) OpenMsBuildWorkspace()
    {
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
        return (workspace, warnings);
    }

    /// <summary>
    /// Iterates the loaded solution's projects (alphabetical) and pulls a
    /// compilation per project, capturing failures as warnings rather than
    /// throws. The project_references map is keyed by name so the engine can
    /// surface the architectural graph without holding a Solution instance.
    /// </summary>
    private static async Task<(List<(Project, Compilation)> Projects,
                              Dictionary<string, IReadOnlyList<string>> ProjectReferences)>
        CollectProjectsAndCompilationsAsync(Solution solution, List<WorkspaceWarning> warnings)
    {
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

        return (projects, projectRefs);
    }

    /// <summary>
    /// Loads a workspace from a declared list of <c>.csproj</c> absolute paths,
    /// in the given order (ADR 0006 §4.3, §6.1). The reporting path is what
    /// the engine will render into <c>report.solution_path</c>.
    /// </summary>
    public Task<LoadResult> LoadFromProjectListAsync(
        IReadOnlyList<string> csprojAbsList,
        string solutionPathForReporting)
    {
        if (csprojAbsList is null) throw new ArgumentNullException(nameof(csprojAbsList));
        if (csprojAbsList.Count == 0)
            throw new ArgumentException("Project list cannot be empty.", nameof(csprojAbsList));

        var raw = new List<(string Name, string CsprojAbs)>(csprojAbsList.Count);
        foreach (var csprojAbs in csprojAbsList)
        {
            if (!File.Exists(csprojAbs))
                throw new FileNotFoundException($"Project not found: {csprojAbs}", csprojAbs);
            // Use the assembly-name fallback: file name without extension. The
            // BuildSolutionFromProjects pass overrides this with <RootNamespace>
            // when the csproj declares one. Same convention SolutionLoader has
            // used for years for fixtures with no AssemblyName override.
            var name = Path.GetFileNameWithoutExtension(csprojAbs);
            raw.Add((name, csprojAbs));
        }

        return BuildSolutionFromProjectsAsync(raw, solutionPathForReporting);
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
    /// Fallback loader: parses the .sln for project paths, then delegates to
    /// <see cref="BuildSolutionFromProjectsAsync"/>. Sufficient for type-aware
    /// analysis on the fixtures since they reference only BCL types.
    /// </summary>
    private static async Task<LoadResult> ManualLoadFromSolutionAsync(string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var solutionText = await File.ReadAllTextAsync(solutionPath).ConfigureAwait(false);

        // Parse "Project(...) = "Name", "relPath", "{guid}"" lines.
        var projectRegex = new System.Text.RegularExpressions.Regex(
            "Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"\\s*,\\s*\"\\{([^}]+)\\}\"",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var raw = new List<(string Name, string CsprojAbs)>();
        foreach (System.Text.RegularExpressions.Match m in projectRegex.Matches(solutionText))
        {
            var name = m.Groups[1].Value;
            var rel = m.Groups[2].Value.Replace('\\', Path.DirectorySeparatorChar);
            var abs = Path.GetFullPath(Path.Combine(solutionDir, rel));
            if (!File.Exists(abs)) continue;
            raw.Add((name, abs));
        }

        return await BuildSolutionFromProjectsAsync(raw, solutionPath).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds an <see cref="AdhocWorkspace"/>-backed Solution from the given
    /// <c>(Name, CsprojAbs)</c> pairs in the supplied order. This is the
    /// shared core used by both the .sln fallback path and the
    /// <see cref="LoadFromProjectListAsync"/> path. Iteration order is
    /// preserved as-given (ADR 0006 §4.3, §6.3).
    /// </summary>
    private static async Task<LoadResult> BuildSolutionFromProjectsAsync(
        IReadOnlyList<(string Name, string CsprojAbs)> raw,
        string solutionPathForReporting)
    {
        var workspace = new AdhocWorkspace();
        var solutionId = SolutionId.CreateNewId();
        workspace.AddSolution(SolutionInfo.Create(solutionId, VersionStamp.Default));

        var (idByName, nameByCsprojPath) = CreateProjectIds(raw);
        var metadataRefs = BuildMetadataReferences();

        var projectRefs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var infos = new List<ProjectInfo>();

        foreach (var (name, csprojAbs) in raw)
        {
            if (!idByName.TryGetValue(name, out var pid)) continue;

            var (rootNs, refNames) = ParseCsproj(csprojAbs, nameByCsprojPath);
            projectRefs[name] = refNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();

            var docInfos = LoadProjectDocuments(pid, csprojAbs);
            var info = CreateProjectInfo(pid, name, csprojAbs, docInfos, metadataRefs);
            infos.Add(info.WithDefaultNamespace(rootNs));
        }

        var resolvedInfos = ResolveProjectReferences(infos, projectRefs, idByName);

        var solution = workspace.AddSolution(
            SolutionInfo.Create(
                solutionId,
                VersionStamp.Default,
                filePath: solutionPathForReporting,
                projects: resolvedInfos));

        var (compilations, warnings) = await CompileInDeclarationOrderAsync(solution, raw, idByName).ConfigureAwait(false);
        return new LoadResult(solution, compilations, warnings, projectRefs);
    }

    /// <summary>
    /// Pass 1: assign a stable <see cref="ProjectId"/> to each unique project
    /// name, and index csproj path → project name so ProjectReference lookups
    /// later can find their target by absolute path.
    /// </summary>
    private static (Dictionary<string, ProjectId> IdByName,
                    Dictionary<string, string> NameByCsprojPath)
        CreateProjectIds(IReadOnlyList<(string Name, string CsprojAbs)> raw)
    {
        var idByName = new Dictionary<string, ProjectId>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in raw)
        {
            if (idByName.ContainsKey(name)) continue;
            idByName[name] = ProjectId.CreateNewId(debugName: name);
        }

        var nameByCsprojPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, abs) in raw)
        {
            nameByCsprojPath[abs] = name;
        }

        return (idByName, nameByCsprojPath);
    }

    /// <summary>
    /// Standard MetadataReferences: every BCL DLL on the trusted-platform list.
    /// Sufficient for type-aware analysis on fixtures that reference only the
    /// BCL.
    /// </summary>
    private static List<MetadataReference> BuildMetadataReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")) ?? string.Empty;
        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();
    }

    /// <summary>
    /// Reads <c>RootNamespace</c> and resolves in-scope <c>ProjectReference</c>
    /// includes against <paramref name="nameByCsprojPath"/>. ADR 0006 §6.4:
    /// references outside the declared scope are silently ignored.
    /// </summary>
    private static (string RootNamespace, List<string> ReferencedProjectNames)
        ParseCsproj(string csprojAbs, Dictionary<string, string> nameByCsprojPath)
    {
        var fallbackName = Path.GetFileNameWithoutExtension(csprojAbs);
        var rootNs = fallbackName;
        var refNames = new List<string>();
        var projDir = Path.GetDirectoryName(csprojAbs)!;

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

        return (rootNs, refNames);
    }

    /// <summary>
    /// Enumerates <c>*.cs</c> files under the project directory in ordinal
    /// order, skipping <c>obj/</c> and <c>bin/</c>. Order preservation is part
    /// of the determinism contract (ADR 0006 §6.3).
    /// </summary>
    private static List<DocumentInfo> LoadProjectDocuments(ProjectId pid, string csprojAbs)
    {
        var projDir = Path.GetDirectoryName(csprojAbs)!;
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
        return docInfos;
    }

    private static ProjectInfo CreateProjectInfo(
        ProjectId pid,
        string name,
        string csprojAbs,
        List<DocumentInfo> docInfos,
        List<MetadataReference> metadataRefs)
    {
        return ProjectInfo.Create(
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
    }

    /// <summary>
    /// Pass 2: now that every project name has an Id, materialise the
    /// ProjectReference list for each ProjectInfo.
    /// </summary>
    private static List<ProjectInfo> ResolveProjectReferences(
        List<ProjectInfo> infos,
        Dictionary<string, IReadOnlyList<string>> projectRefs,
        Dictionary<string, ProjectId> idByName)
    {
        var resolvedInfos = new List<ProjectInfo>();
        foreach (var info in infos)
        {
            var refs = projectRefs[info.Name]
                .Where(idByName.ContainsKey)
                .Select(n => new ProjectReference(idByName[n]))
                .ToArray();
            resolvedInfos.Add(info.WithProjectReferences(refs));
        }
        return resolvedInfos;
    }

    /// <summary>
    /// Iterate compilations in the original declaration order, matching the
    /// raw list. This is critical for ADR 0006 §4.3 / §6.3 — the engine (and
    /// downstream metrics aggregation) must see projects in the order
    /// declared in lintty.yml, not the alphabetical order of project.Name.
    /// </summary>
    private static async Task<(List<(Project, Compilation)> Compilations,
                              List<WorkspaceWarning> Warnings)>
        CompileInDeclarationOrderAsync(
            Solution solution,
            IReadOnlyList<(string Name, string CsprojAbs)> raw,
            Dictionary<string, ProjectId> idByName)
    {
        var compilations = new List<(Project, Compilation)>();
        var warnings = new List<WorkspaceWarning>();
        foreach (var (name, _) in raw)
        {
            if (!idByName.TryGetValue(name, out var pid)) continue;
            var project = solution.GetProject(pid);
            if (project is null) continue;
            var c = await project.GetCompilationAsync().ConfigureAwait(false);
            if (c is null)
            {
                warnings.Add(new WorkspaceWarning("warning",
                    $"compilation null for {project.Name}", project.Name));
                continue;
            }
            compilations.Add((project, c));
        }
        return (compilations, warnings);
    }
}
