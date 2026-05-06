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
/// V0 measure: a method whose body exceeds 60 physical LoC is reported. The
/// metric tag in the JSON is <c>tokenizer=loc_v1</c> — honest about what we
/// actually count today. <c>token_count_estimate</c> stays as <c>loc × 25</c>
/// for clients that already consume the field, but it is informational only;
/// the threshold is LoC.
///
/// Test code escapes this rule (and only this rule) — methods in
/// <c>*.Tests.csproj</c> projects are skipped to avoid false positives on
/// Arrange/Act/Assert blocks that are legitimately long. The other analyzers
/// (LNTY-001/002/003/006/007/008) still run on tests because their concerns
/// (domain isolation, persistence leaks, port crossings, cycles) apply
/// equally to test code.
///
/// V1+ may swap the LoC ruler for a real tokenizer over the slice; that is a
/// canon revision, not a cosmetic change.
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
            // Test projects are exempt from LNTY-009 only — see class summary.
            if (TestProjectFilter.IsTestProject(project)) continue;
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
                            ["tokenizer"] = "loc_v1",
                        }));
                }
            }
        }

        return results;
    }
}
