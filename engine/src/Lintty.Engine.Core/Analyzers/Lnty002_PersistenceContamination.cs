using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// LNTY-002 — Persistence Contamination (Critical, Hard Lock).
///
/// In every Domain document we visit string-shaped expressions and ask the
/// SemanticModel to resolve them to their compile-time constant value. If the
/// folded value matches the canonical SQL grammar regex, it is a violation —
/// even when no single literal in the file contains a complete SQL statement
/// (the Ninja #1 case).
///
/// We also walk <see cref="InvocationExpressionSyntax"/> for <c>string.Concat</c>
/// calls that the constant folder doesn't reduce on its own and re-fold them
/// manually from each argument's GetConstantValue.
/// </summary>
public sealed class Lnty002_PersistenceContamination : IAnalyzer
{
    public string RuleId => "LNTY-002";

    private static readonly Regex SqlPattern = new(
        @"^\s*(SELECT|INSERT|UPDATE|DELETE|MERGE|CREATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<IReadOnlyList<Violation>> AnalyzeAsync(AnalysisContext context)
    {
        var results = new List<Violation>();
        var seen = new HashSet<(string file, int line, int col)>();

        foreach (var (project, compilation) in context.Projects)
        {
            var layer = context.LayerByProject[project.Name];
            if (layer != Layer.Domain) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync().ConfigureAwait(false);

                foreach (var node in root.DescendantNodes())
                {
                    if (!IsStringShaped(node)) continue;

                    string? folded = TryFold(model, node);
                    if (folded is null) continue;
                    if (!SqlPattern.IsMatch(folded)) continue;

                    var span = node.GetLocation().GetLineSpan();
                    var key = (tree.FilePath, span.StartLinePosition.Line, span.StartLinePosition.Character);
                    if (!seen.Add(key)) continue;

                    var detectionPass = node switch
                    {
                        LiteralExpressionSyntax => "literal",
                        BinaryExpressionSyntax => "constant_folding",
                        InterpolatedStringExpressionSyntax => "constant_folding",
                        InvocationExpressionSyntax => "constant_folding",
                        _ => "constant_folding",
                    };

                    var snippet = node.ToString();
                    if (snippet.Length > 240) snippet = snippet.Substring(0, 240) + "...";

                    results.Add(new Violation(
                        RuleId: "LNTY-002",
                        Severity: Severity.Critical,
                        IsHardLock: true,
                        File: context.RelativePath(tree.FilePath),
                        Line: span.StartLinePosition.Line + 1,
                        Column: span.StartLinePosition.Character + 1,
                        SymbolFqn: NearestSymbolFqn(model, node),
                        CodeSnippet: snippet,
                        AstKind: node.GetType().Name,
                        AdditionalContext: new Dictionary<string, string>
                        {
                            ["constant_value"] = folded,
                            ["detection_pass"] = detectionPass,
                        }));
                }
            }
        }

        return results;
    }

    private static bool IsStringShaped(SyntaxNode node)
    {
        switch (node)
        {
            case LiteralExpressionSyntax lit when lit.Kind() == SyntaxKind.StringLiteralExpression:
                return true;
            case BinaryExpressionSyntax bin when bin.Kind() == SyntaxKind.AddExpression:
                return true;
            case InterpolatedStringExpressionSyntax:
                return true;
            case InvocationExpressionSyntax inv when IsStringConcatCall(inv):
                return true;
            default:
                return false;
        }
    }

    private static bool IsStringConcatCall(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is MemberAccessExpressionSyntax m
            && m.Name.Identifier.ValueText == "Concat")
        {
            // The receiver may be a predefined keyword `string` (PredefinedTypeSyntax)
            // or `String` / `System.String` (IdentifierNameSyntax / qualified).
            switch (m.Expression)
            {
                case PredefinedTypeSyntax pre when pre.Keyword.ValueText == "string":
                    return true;
                case IdentifierNameSyntax ident when ident.Identifier.ValueText is "String" or "string":
                    return true;
                case QualifiedNameSyntax qn when qn.Right.Identifier.ValueText == "String":
                    return true;
            }
        }
        return false;
    }

    private static string? TryFold(SemanticModel model, SyntaxNode node)
    {
        // Fast path: GetConstantValue handles literal, +, and constant interpolations.
        var c = model.GetConstantValue(node);
        if (c.HasValue && c.Value is string s) return s;

        // string.Concat(...) — fold each argument independently then join.
        if (node is InvocationExpressionSyntax inv && IsStringConcatCall(inv))
        {
            var sb = new System.Text.StringBuilder();
            foreach (var arg in inv.ArgumentList.Arguments)
            {
                var argConst = model.GetConstantValue(arg.Expression);
                if (!argConst.HasValue || argConst.Value is not string p) return null;
                sb.Append(p);
            }
            return sb.ToString();
        }

        return null;
    }

    private static string NearestSymbolFqn(SemanticModel model, SyntaxNode node)
    {
        var member = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (member is null) return string.Empty;
        var sym = model.GetDeclaredSymbol(member);
        return sym?.ToDisplayString() ?? string.Empty;
    }
}
