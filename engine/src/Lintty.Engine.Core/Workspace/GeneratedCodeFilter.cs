using System.IO;
using Microsoft.CodeAnalysis;

namespace Lintty.Engine.Core.Workspace;

/// <summary>
/// Skips compiler-generated and tooling-generated trees so analyzers don't
/// emit violations for code the user didn't write. Cheap text checks first,
/// SyntaxTree.IsGeneratedCode last (it pulls a SemanticModel).
/// </summary>
public static class GeneratedCodeFilter
{
    public static bool IsGenerated(SyntaxTree tree)
    {
        var path = tree.FilePath ?? string.Empty;
        var norm = path.Replace('\\', '/');
        if (string.IsNullOrEmpty(path)) return false;

        if (norm.Contains("/obj/", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (norm.Contains("/bin/", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (norm.Contains("/Generated/", System.StringComparison.OrdinalIgnoreCase)) return true;

        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.EndsWith(".designer.cs", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.Contains(".AssemblyInfo.", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.Contains(".AssemblyAttributes", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.Contains(".GlobalUsings.", System.StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
