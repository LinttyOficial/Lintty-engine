using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// LNTY-006 — Two complementary detections, both Low severity:
///
/// (a) Ubiquitous Language Leak (canon): identifiers in Domain whose name
///     matches the regex blacklist (Manager|Helper|Util|Utils|Utility)$ or
///     ^(Data|Info|Temp|Stuff|Thing|Misc|Generic)\d*$.
///
/// (b) Repository Contract Placement (user spec): an interface named
///     <c>I*Repository</c> declared in any project that is NOT classified as
///     Domain. The Domain port belongs in Domain; declaring it elsewhere
///     reverses the dependency arrow.
/// </summary>
public sealed class Lnty006_LanguageAndRepositoryPlacement : IAnalyzer
{
    public string RuleId => "LNTY-006";

    private static readonly Regex BannedExact = new(
        @"^(Data|Info|Temp|Stuff|Thing|Misc|Generic)\d*$",
        RegexOptions.Compiled);

    private static readonly Regex BannedSuffix = new(
        @"(Manager|Helper|Util|Utils|Utility)$",
        RegexOptions.Compiled);

    public async Task<IReadOnlyList<Violation>> AnalyzeAsync(AnalysisContext context)
    {
        var results = new List<Violation>();

        foreach (var (project, compilation) in context.Projects)
        {
            var layer = context.LayerByProject[project.Name];

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync().ConfigureAwait(false);

                if (layer == Layer.Domain)
                    DetectLanguageLeak(context, tree, root, model, results);

                if (layer != Layer.Domain && layer != Layer.DomainAbstractions)
                    DetectRepositoryContractMisplacement(context, tree, root, model, results);
            }
        }

        return results;
    }

    private static void DetectLanguageLeak(
        AnalysisContext ctx, SyntaxTree tree, SyntaxNode root,
        SemanticModel model, List<Violation> results)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var name = typeDecl.Identifier.ValueText;
            if (string.IsNullOrEmpty(name)) continue;
            if (!BannedExact.IsMatch(name) && !BannedSuffix.IsMatch(name)) continue;

            var sym = model.GetDeclaredSymbol(typeDecl);
            var span = typeDecl.Identifier.GetLocation().GetLineSpan();
            results.Add(new Violation(
                RuleId: "LNTY-006",
                Severity: Severity.Low,
                IsHardLock: false,
                File: ctx.RelativePath(tree.FilePath),
                Line: span.StartLinePosition.Line + 1,
                Column: span.StartLinePosition.Character + 1,
                SymbolFqn: sym?.ToDisplayString() ?? name,
                CodeSnippet: $"{KindKeyword(typeDecl)} {name}",
                AstKind: typeDecl.GetType().Name,
                AdditionalContext: new Dictionary<string, string>
                {
                    ["detection_kind"] = "ubiquitous_language_leak",
                    ["matched_name"] = name,
                }));
        }
    }

    private static string KindKeyword(BaseTypeDeclarationSyntax typeDecl) => typeDecl switch
    {
        ClassDeclarationSyntax => "class",
        InterfaceDeclarationSyntax => "interface",
        StructDeclarationSyntax => "struct",
        RecordDeclarationSyntax => "record",
        EnumDeclarationSyntax => "enum",
        _ => "type",
    };

    private static void DetectRepositoryContractMisplacement(
        AnalysisContext ctx, SyntaxTree tree, SyntaxNode root,
        SemanticModel model, List<Violation> results)
    {
        foreach (var iface in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
        {
            var name = iface.Identifier.ValueText;
            if (!name.StartsWith("I", System.StringComparison.Ordinal)) continue;
            if (!name.EndsWith("Repository", System.StringComparison.Ordinal)) continue;

            var sym = model.GetDeclaredSymbol(iface);
            var span = iface.Identifier.GetLocation().GetLineSpan();
            results.Add(new Violation(
                RuleId: "LNTY-006",
                Severity: Severity.Low,
                IsHardLock: false,
                File: ctx.RelativePath(tree.FilePath),
                Line: span.StartLinePosition.Line + 1,
                Column: span.StartLinePosition.Character + 1,
                SymbolFqn: sym?.ToDisplayString() ?? name,
                CodeSnippet: $"public interface {name}",
                AstKind: nameof(InterfaceDeclarationSyntax),
                AdditionalContext: new Dictionary<string, string>
                {
                    ["detection_kind"] = "repository_contract_placement",
                    ["expected_layer"] = "domain",
                }));
        }
    }
}
