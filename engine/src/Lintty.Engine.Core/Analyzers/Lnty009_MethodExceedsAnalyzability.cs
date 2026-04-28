using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// LNTY-009 — Method Exceeds Analyzability (Medium).
///
/// Sprint 0 placeholder: no Anthropic tokenizer is bundled, so we approximate
/// the budget. A method whose body has more than 60 LoC is reported with an
/// estimated token count of <c>loc × 25</c>. The 8K-token cap from the canon
/// translates to roughly 320 LoC on the same factor; we trip well below that
/// threshold to surface the Sinner's <c>MegaRepository.DoEverything</c>
/// without false positives on the Saint's normal-sized methods.
///
/// TODO Sprint 1: replace with the Anthropic tokenizer over the slice (see
/// docs/03-motor-roslyn.md §5).
/// </summary>
public sealed class Lnty009_MethodExceedsAnalyzability : IAnalyzer
{
    public string RuleId => "LNTY-009";

    private const int MaxLocPlaceholder = 60;
    private const int TokenFactor = 25;

    public async Task<IReadOnlyList<Violation>> AnalyzeAsync(AnalysisContext context)
    {
        var results = new List<Violation>();

        foreach (var (project, compilation) in context.Projects)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync().ConfigureAwait(false);

                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
                    if (body is null) continue;
                    var lineSpan = body.GetLocation().GetLineSpan();
                    var loc = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;
                    if (loc <= MaxLocPlaceholder) continue;

                    var sym = model.GetDeclaredSymbol(method);
                    var idSpan = method.Identifier.GetLocation().GetLineSpan();
                    var tokens = loc * TokenFactor;

                    results.Add(new Violation(
                        RuleId: "LNTY-009",
                        Severity: Severity.Medium,
                        IsHardLock: false,
                        File: context.RelativePath(tree.FilePath),
                        Line: idSpan.StartLinePosition.Line + 1,
                        Column: idSpan.StartLinePosition.Character + 1,
                        SymbolFqn: sym?.ToDisplayString() ?? method.Identifier.ValueText,
                        CodeSnippet: $"{method.ReturnType} {method.Identifier.ValueText}(...) // {loc} LoC",
                        AstKind: nameof(MethodDeclarationSyntax),
                        AdditionalContext: new Dictionary<string, string>
                        {
                            ["loc_count"] = loc.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["token_count_estimate"] = tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["tokenizer"] = "placeholder_sprint0",
                        }));
                }
            }
        }

        return results;
    }
}
