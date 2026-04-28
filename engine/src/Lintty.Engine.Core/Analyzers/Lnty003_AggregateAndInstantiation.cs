using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// LNTY-003 — Two complementary detections folded into one rule id, matching
/// the canon (Forbidden Instantiation) and the user-fixture spec (Aggregate
/// Root boundary leak):
///
/// (a) In Domain code, public properties whose declared type is one of the
///     mutable generic collection types (<c>List&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>,
///     <c>IList&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c>) → aggregate-leak. Detected via
///     <c>SemanticModel.GetDeclaredSymbol</c> on the property and inspection
///     of the resulting <see cref="IPropertySymbol.Type"/>.
///
/// (b) In Application or Presentation code, <c>new T()</c> where <c>T</c> is
///     classified as Infrastructure → forbidden instantiation. Records, value
///     types, exceptions, and System.* are excluded.
/// </summary>
public sealed class Lnty003_AggregateAndInstantiation : IAnalyzer
{
    public string RuleId => "LNTY-003";

    private static readonly string[] MutableCollectionFqn = new[]
    {
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.HashSet<T>",
        "System.Collections.Generic.Dictionary<TKey, TValue>",
    };

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
                    DetectAggregateLeaks(context, tree, root, model, results);

                if (layer is Layer.Application or Layer.Presentation)
                    DetectForbiddenInstantiation(context, tree, root, model, results);
            }
        }

        return results;
    }

    private static void DetectAggregateLeaks(
        AnalysisContext ctx, SyntaxTree tree, SyntaxNode root,
        SemanticModel model, List<Violation> results)
    {
        foreach (var prop in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (!prop.Modifiers.Any(m => m.ValueText == "public")) continue;

            var sym = model.GetDeclaredSymbol(prop) as IPropertySymbol;
            if (sym is null) continue;

            // Only flag properties whose type is exactly one of the mutable generics
            // (the constructed type's original definition fqn). Read-only collections
            // like IReadOnlyList<T> or ReadOnlyCollection<T> are deliberately excluded.
            var type = sym.Type as INamedTypeSymbol;
            if (type is null) continue;
            var def = type.OriginalDefinition.ToDisplayString();
            if (!MutableCollectionFqn.Contains(def)) continue;

            // A setter (any kind) makes the leak unambiguous; for fields/auto-props
            // without a setter we still flag the leak because the underlying instance
            // is mutable through the getter.
            var span = prop.Identifier.GetLocation().GetLineSpan();
            results.Add(new Violation(
                RuleId: "LNTY-003",
                Severity: Severity.Medium,
                IsHardLock: false,
                File: ctx.RelativePath(tree.FilePath),
                Line: span.StartLinePosition.Line + 1,
                Column: span.StartLinePosition.Character + 1,
                SymbolFqn: sym.ToDisplayString(),
                CodeSnippet: prop.ToString().Split('\n')[0].Trim(),
                AstKind: nameof(PropertyDeclarationSyntax),
                AdditionalContext: new Dictionary<string, string>
                {
                    ["leak_kind"] = "mutable_collection_property",
                    ["declared_type"] = def,
                }));
        }
    }

    private static void DetectForbiddenInstantiation(
        AnalysisContext ctx, SyntaxTree tree, SyntaxNode root,
        SemanticModel model, List<Violation> results)
    {
        foreach (var newExpr in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var typeInfo = model.GetTypeInfo(newExpr.Type);
            if (typeInfo.Type is not INamedTypeSymbol typeSymbol) continue;

            // Exclusions per canon: records, value types, exceptions, System.*.
            if (typeSymbol.IsRecord) continue;
            if (typeSymbol.IsValueType) continue;
            if (IsExceptionType(typeSymbol)) continue;
            var ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ns.StartsWith("System", System.StringComparison.Ordinal)) continue;

            // Resolve owning project either by assembly symbol (preferred) or by
            // matching the type's containing namespace against known project names.
            var assemblyName = typeSymbol.ContainingAssembly?.Name;
            if (string.IsNullOrEmpty(assemblyName))
            {
                assemblyName = ResolveProjectByNamespace(ctx, ns);
                if (string.IsNullOrEmpty(assemblyName)) continue;
            }

            var typeLayer = ctx.LayerOf(assemblyName);
            if (typeLayer != Layer.Infrastructure) continue;

            var span = newExpr.GetLocation().GetLineSpan();
            results.Add(new Violation(
                RuleId: "LNTY-003",
                Severity: Severity.Medium,
                IsHardLock: false,
                File: ctx.RelativePath(tree.FilePath),
                Line: span.StartLinePosition.Line + 1,
                Column: span.StartLinePosition.Character + 1,
                SymbolFqn: typeSymbol.ToDisplayString(),
                CodeSnippet: newExpr.ToString(),
                AstKind: nameof(ObjectCreationExpressionSyntax),
                AdditionalContext: new Dictionary<string, string>
                {
                    ["leak_kind"] = "forbidden_instantiation",
                    ["instantiated_type"] = typeSymbol.ToDisplayString(),
                    ["instantiated_assembly"] = assemblyName,
                }));
        }
    }

    private static string ResolveProjectByNamespace(AnalysisContext ctx, string ns)
    {
        // Prefer the longest project name that is a prefix of the namespace.
        string best = string.Empty;
        foreach (var project in ctx.LayerByProject.Keys)
        {
            if (string.Equals(ns, project, System.StringComparison.Ordinal)
                || ns.StartsWith(project + ".", System.StringComparison.Ordinal))
            {
                if (project.Length > best.Length) best = project;
            }
        }
        return best;
    }

    private static bool IsExceptionType(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == "System.Exception") return true;
        }
        return false;
    }
}
