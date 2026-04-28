using System.Collections.Generic;
using System.IO;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Tagging;
using Microsoft.CodeAnalysis;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// Materialised snapshot of the workspace passed to every analyzer.
/// Holds compilations indexed by project name, the layer of each project,
/// and the project-reference graph for LNTY-007 first pass.
/// </summary>
public sealed class AnalysisContext
{
    public required string SolutionPath { get; init; }
    public required string SolutionDir { get; init; }
    public required IReadOnlyList<(Project Project, Compilation Compilation)> Projects { get; init; }
    public required IReadOnlyDictionary<string, Layer> LayerByProject { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> ProjectReferences { get; init; }
    public required LayerTagger Tagger { get; init; }
    public required LinttyConfig Config { get; init; }

    /// <summary>Returns a path relative to the solution directory, with '/' separators.</summary>
    public string RelativePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return string.Empty;
        var rel = Path.GetRelativePath(SolutionDir, absolutePath);
        return rel.Replace('\\', '/');
    }

    /// <summary>Look up the assembly's classification by name.</summary>
    public Layer LayerOf(string assemblyName)
    {
        if (LayerByProject.TryGetValue(assemblyName, out var layer)) return layer;
        return Tagger.Classify(assemblyName);
    }
}
