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
            if (layer != Layer.Infrastructure) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync().ConfigureAwait(false);

                foreach (var typeDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!IsPublic(typeDecl)) continue;

                    var declSym = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                    if (declSym is null) continue;

                    // Skip types that already implement at least one interface from
                    // Domain or Domain.Abstractions.
                    if (declSym.AllInterfaces.Any(i =>
                        IsDomainAssembly(context, i.ContainingAssembly?.Name)))
                    {
                        continue;
                    }

                    // Heuristic match per canon MVP: I<TypeName> in Domain.
                    if (domainInterfaceNames.Contains("I" + declSym.Name)) continue;

                    // Cross-assembly consumption check via FindReferencesAsync. If the
                    // call throws (the AdhocWorkspace fallback can refuse this), treat
                    // it as "consumed across assemblies" since the type is public —
                    // the conservative choice for LNTY-008 in Sprint 0.
                    bool crossAssemblyConsumed = true;
                    try
                    {
                        var allRefs = await SymbolFinder.FindReferencesAsync(
                            declSym, project.Solution).ConfigureAwait(false);
                        crossAssemblyConsumed = allRefs.SelectMany(r => r.Locations)
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
                        // FindReferencesAsync fall-through; assume cross-assembly so we
                        // don't silently swallow a real LNTY-008 case.
                        crossAssemblyConsumed = true;
                    }

                    if (!crossAssemblyConsumed) continue;

                    var span = typeDecl.Identifier.GetLocation().GetLineSpan();
                    results.Add(new Violation(
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
                        }));
                }
            }
        }

        return results;
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
