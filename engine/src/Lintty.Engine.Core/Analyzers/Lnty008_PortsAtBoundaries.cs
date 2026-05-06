using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// LNTY-008 — Ports at Boundaries (High).
///
/// For every public class/struct declared in an Infrastructure project we
/// check whether at least one reference exists outside the declaring assembly
/// (cross-assembly consumption). When that condition holds AND no Domain (or
/// Domain.Abstractions) project declares an interface named <c>I&lt;TypeName&gt;</c>,
/// we raise a violation: a public adapter is reaching across the assembly
/// boundary without a Domain port.
/// </summary>
public sealed class Lnty008_PortsAtBoundaries : IAnalyzer
{
    public string RuleId => "LNTY-008";

    public async Task<IReadOnlyList<Violation>> AnalyzeAsync(AnalysisContext context)
    {
        var results = new List<Violation>();

        var domainInterfaceNames = CollectDomainInterfaceNames(context);

        foreach (var (project, compilation) in context.Projects)
        {
            var layer = context.LayerByProject[project.Name];
            // Layer.Unknown (permissive tagging, 2026-05-06) is naturally
            // skipped here — we only flag adapters in Infrastructure projects.
            if (layer != Layer.Infrastructure) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync().ConfigureAwait(false);

                foreach (var typeDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!IsBoundaryCandidate(typeDecl, model, out var declSym))
                        continue;

                    if (HasMatchingPortInDomain(declSym!, context, domainInterfaceNames))
                        continue;

                    if (!await IsConsumedAcrossAssembliesAsync(declSym!, project).ConfigureAwait(false))
                        continue;

                    results.Add(BuildViolation(context, tree, typeDecl, declSym!));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Filters out types that don't qualify for the LNTY-008 inspection: the
    /// type must be a public class (private/internal types don't cross
    /// assembly boundaries) and the SemanticModel must successfully resolve
    /// it as a named type.
    /// </summary>
    private static bool IsBoundaryCandidate(
        ClassDeclarationSyntax typeDecl,
        SemanticModel model,
        out INamedTypeSymbol? declSym)
    {
        declSym = null;
        if (!IsPublic(typeDecl)) return false;
        declSym = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        return declSym is not null;
    }

    /// <summary>
    /// Returns true when this Infrastructure type is fronted by a Domain port,
    /// either by implementing a Domain-declared interface OR by matching the
    /// canon MVP heuristic <c>I&lt;TypeName&gt;</c> living in Domain.
    /// </summary>
    private static bool HasMatchingPortInDomain(
        INamedTypeSymbol declSym,
        AnalysisContext context,
        HashSet<string> domainInterfaceNames)
    {
        // Skip types that already implement at least one interface from
        // Domain or Domain.Abstractions.
        if (declSym.AllInterfaces.Any(i =>
            IsDomainAssembly(context, i.ContainingAssembly?.Name)))
        {
            return true;
        }

        // Heuristic match per canon MVP: I<TypeName> in Domain.
        return domainInterfaceNames.Contains("I" + declSym.Name);
    }

    /// <summary>
    /// Cross-assembly consumption check via FindReferencesAsync. When
    /// FindReferences refuses to operate (e.g., AdhocWorkspace fallback), we
    /// conservatively treat the type as consumed across assemblies so we
    /// never silently miss a real LNTY-008 case.
    /// </summary>
    private static async Task<bool> IsConsumedAcrossAssembliesAsync(
        INamedTypeSymbol declSym,
        Project project)
    {
        try
        {
            var allRefs = await SymbolFinder.FindReferencesAsync(
                declSym, project.Solution).ConfigureAwait(false);
            return allRefs.SelectMany(r => r.Locations)
                .Any(loc =>
                {
                    var docProj = loc.Document?.Project;
                    if (docProj is null) return false;
                    return !string.Equals(docProj.AssemblyName,
                        declSym.ContainingAssembly?.Name,
                        StringComparison.Ordinal);
                });
        }
        catch
        {
            // Conservative default — see method doc.
            return true;
        }
    }

    private static Violation BuildViolation(
        AnalysisContext context,
        SyntaxTree tree,
        ClassDeclarationSyntax typeDecl,
        INamedTypeSymbol declSym)
    {
        var span = typeDecl.Identifier.GetLocation().GetLineSpan();
        return new Violation(
            RuleId: "LNTY-008",
            Severity: Severity.High,
            IsHardLock: false,
            File: context.RelativePath(tree.FilePath),
            Line: span.StartLinePosition.Line + 1,
            Column: span.StartLinePosition.Character + 1,
            SymbolFqn: declSym.ToDisplayString(),
            CodeSnippet: $"public class {declSym.Name}",
            AstKind: nameof(ClassDeclarationSyntax),
            AdditionalContext: new Dictionary<string, string>
            {
                ["missing_port"] = "I" + declSym.Name,
                ["expected_in"] = "domain",
            });
    }

    private static HashSet<string> CollectDomainInterfaceNames(AnalysisContext context)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (project, compilation) in context.Projects)
        {
            var layer = context.LayerByProject[project.Name];
            if (layer != Layer.Domain && layer != Layer.DomainAbstractions) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var root = tree.GetRoot();
                foreach (var iface in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
                    set.Add(iface.Identifier.ValueText);
            }
        }
        return set;
    }

    private static bool IsDomainAssembly(AnalysisContext ctx, string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName)) return false;
        var l = ctx.LayerOf(assemblyName);
        return l == Layer.Domain || l == Layer.DomainAbstractions;
    }

    private static bool IsPublic(ClassDeclarationSyntax c)
        => c.Modifiers.Any(m => m.ValueText == "public");
}
