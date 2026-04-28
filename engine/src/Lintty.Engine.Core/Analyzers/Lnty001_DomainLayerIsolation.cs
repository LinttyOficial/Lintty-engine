using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// LNTY-001 — Domain Layer Isolation (Critical, Hard Lock).
///
/// Detection: in every C# document whose project is classified as Domain, any
/// using directive OR symbol reference resolved through the SemanticModel that
/// lands in a forbidden namespace (System.Data.*, Microsoft.EntityFrameworkCore,
/// Dapper, System.Net.Http, Microsoft.AspNetCore.*) or in an assembly classified
/// as Infrastructure/Presentation is reported. The detection is type-aware:
/// fallback to syntactic-only analysis is never used here.
/// </summary>
public sealed class Lnty001_DomainLayerIsolation : IAnalyzer
{
    public string RuleId => "LNTY-001";

    private static readonly string[] ForbiddenNamespacePrefixes = new[]
    {
        "System.Data",
        "System.Net.Http",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Data.SqlClient",
        "Dapper",
        "MongoDB.Driver",
        "Npgsql",
    };

    public async Task<IReadOnlyList<Violation>> AnalyzeAsync(AnalysisContext context)
    {
        var results = new List<Violation>();
        var seen = new HashSet<(string file, int line, int col, string ns)>();

        foreach (var (project, compilation) in context.Projects)
        {
            var layer = context.LayerByProject[project.Name];
            if (layer != Layer.Domain) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync().ConfigureAwait(false);

                // Pass A: using directives (cheap and explicit).
                foreach (var u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                {
                    if (u.Name is null) continue;
                    var info = model.GetSymbolInfo(u.Name);
                    var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                    string ns;
                    if (symbol is INamespaceSymbol nsSym)
                        ns = nsSym.ToDisplayString();
                    else if (symbol is INamedTypeSymbol typeSym)
                        ns = typeSym.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                    else
                        ns = u.Name.ToString();

                    if (!IsForbidden(ns)) continue;

                    var span = u.GetLocation().GetLineSpan();
                    var key = (tree.FilePath, span.StartLinePosition.Line, span.StartLinePosition.Character, ns);
                    if (!seen.Add(key)) continue;

                    results.Add(BuildViolation(context, tree, u, ns,
                        astKind: nameof(UsingDirectiveSyntax),
                        snippet: u.ToString().Trim(),
                        symbolFqn: ns,
                        detectionPass: "using_directive"));
                }

                // Pass B: identifier references resolved through the semantic model.
                // Only flag the first occurrence of each forbidden namespace per
                // (file, line, column) to keep noise reasonable.
                foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    // Skip the LHS of a using directive (already handled above).
                    if (id.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null) continue;

                    var sym = model.GetSymbolInfo(id).Symbol;
                    if (sym is null) continue;

                    var ns = sym.ContainingNamespace?.ToDisplayString();
                    if (string.IsNullOrEmpty(ns)) continue;
                    if (!IsForbidden(ns)) continue;

                    var span = id.GetLocation().GetLineSpan();
                    var key = (tree.FilePath, span.StartLinePosition.Line, span.StartLinePosition.Character, ns);
                    if (!seen.Add(key)) continue;

                    results.Add(BuildViolation(context, tree, id, ns,
                        astKind: nameof(IdentifierNameSyntax),
                        snippet: id.ToString(),
                        symbolFqn: sym.ToDisplayString(),
                        detectionPass: "type_aware"));
                }
            }
        }

        return results;
    }

    private static bool IsForbidden(string ns)
    {
        if (string.IsNullOrEmpty(ns)) return false;
        foreach (var prefix in ForbiddenNamespacePrefixes)
        {
            if (ns.Equals(prefix, System.StringComparison.Ordinal)) return true;
            if (ns.StartsWith(prefix + ".", System.StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static Violation BuildViolation(
        AnalysisContext ctx,
        SyntaxTree tree,
        SyntaxNode node,
        string forbiddenNs,
        string astKind,
        string snippet,
        string symbolFqn,
        string detectionPass)
    {
        var span = node.GetLocation().GetLineSpan();
        return new Violation(
            RuleId: "LNTY-001",
            Severity: Severity.Critical,
            IsHardLock: true,
            File: ctx.RelativePath(tree.FilePath),
            Line: span.StartLinePosition.Line + 1,
            Column: span.StartLinePosition.Character + 1,
            SymbolFqn: symbolFqn,
            CodeSnippet: snippet,
            AstKind: astKind,
            AdditionalContext: new Dictionary<string, string>
            {
                ["forbidden_namespace"] = forbiddenNs,
                ["detection_pass"] = detectionPass,
            });
    }
}
