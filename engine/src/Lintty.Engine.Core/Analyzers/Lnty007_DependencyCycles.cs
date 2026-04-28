using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lintty.Engine.Core.Model;
using Lintty.Engine.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// LNTY-007 — Dependency Cycles (Critical, Hard Lock).
///
/// Two passes, both via Tarjan's strongly-connected-components algorithm:
///
/// Pass 1 (project graph): nodes = csproj names, edges = ProjectReferences
/// reported by the workspace loader. Any SCC with size > 1 is a project
/// cycle.
///
/// Pass 2 (bounded-context graph): a bounded context is the second segment
/// of a fully qualified name (e.g. <c>Sinner.Application.X</c> → context
/// <c>Application</c>). For every type in every Domain/Application/Infra/
/// Presentation document we walk symbols referenced by base types, parameter
/// types, return types, member accesses, and identifier references; any
/// cross-context edge is added to the graph. Tarjan finds back-edges that
/// the project-reference graph cannot see, including the smuggled-namespace
/// case where Infrastructure declares types inside the Application
/// namespace.
/// </summary>
public sealed class Lnty007_DependencyCycles : IAnalyzer
{
    public string RuleId => "LNTY-007";

    public async Task<IReadOnlyList<Violation>> AnalyzeAsync(AnalysisContext context)
    {
        var results = new List<Violation>();

        // Pass 1: project graph.
        var projectGraph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (project, _) in context.Projects)
        {
            projectGraph[project.Name] = new HashSet<string>(StringComparer.Ordinal);
            if (context.ProjectReferences.TryGetValue(project.Name, out var refs))
            {
                foreach (var r in refs)
                    if (!string.Equals(r, project.Name, StringComparison.Ordinal))
                        projectGraph[project.Name].Add(r);
            }
        }

        foreach (var scc in TarjanScc(projectGraph))
        {
            if (scc.Count <= 1) continue;
            var members = scc.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            results.Add(BuildCycleViolation(context, members, "project_cycle"));
        }

        // Pass 2: bounded-context graph.
        var contextGraph = await BuildBoundedContextGraphAsync(context).ConfigureAwait(false);
        foreach (var scc in TarjanScc(contextGraph))
        {
            if (scc.Count <= 1) continue;
            var members = scc.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            results.Add(BuildCycleViolation(context, members, "bounded_context_cycle"));
        }

        return results;
    }

    private static async Task<Dictionary<string, HashSet<string>>> BuildBoundedContextGraphAsync(
        AnalysisContext context)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void AddNode(string ctx)
        {
            if (!graph.ContainsKey(ctx))
                graph[ctx] = new HashSet<string>(StringComparer.Ordinal);
        }

        void AddEdge(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.Ordinal)) return;
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return;
            AddNode(from);
            AddNode(to);
            graph[from].Add(to);
        }

        foreach (var (project, compilation) in context.Projects)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeFilter.IsGenerated(tree)) continue;
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync().ConfigureAwait(false);

                foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    var declSym = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                    if (declSym is null) continue;

                    var fromCtx = BoundedContextOf(declSym.ContainingNamespace?.ToDisplayString());
                    if (string.IsNullOrEmpty(fromCtx)) continue;
                    AddNode(fromCtx);

                    // Symbols referenced inside this type.
                    foreach (var idNode in typeDecl.DescendantNodes().OfType<IdentifierNameSyntax>())
                    {
                        // Skip the LHS of using directives (already structural).
                        if (idNode.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null) continue;

                        var sym = model.GetSymbolInfo(idNode).Symbol;
                        if (sym is null) continue;
                        if (sym is INamespaceSymbol) continue;

                        var symNs = sym.ContainingNamespace?.ToDisplayString();
                        var toCtx = BoundedContextOf(symNs);
                        if (string.IsNullOrEmpty(toCtx)) continue;

                        // Only count cross-root namespace edges as inter-context edges.
                        // A reference inside the same root namespace family that maps to
                        // the same context produces no edge; cross-context produces one.
                        AddEdge(fromCtx, toCtx);
                    }

                    // Also process using directives at the file level — visiting them
                    // multiple times for multi-type files is harmless (HashSet).
                    var unit = root as CompilationUnitSyntax ?? root.AncestorsAndSelf().OfType<CompilationUnitSyntax>().FirstOrDefault();
                    var usings = unit?.Usings ?? default;
                    foreach (var u in usings)
                    {
                        if (u.Name is null) continue;
                        var info = model.GetSymbolInfo(u.Name);
                        var s = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                        string? ns;
                        if (s is INamespaceSymbol nsSym) ns = nsSym.ToDisplayString();
                        else ns = u.Name.ToString();
                        var toCtx = BoundedContextOf(ns);
                        if (!string.IsNullOrEmpty(toCtx))
                            AddEdge(fromCtx, toCtx);
                    }
                }
            }
        }

        return graph;
    }

    /// <summary>
    /// Returns the second segment of a dotted namespace, e.g.
    /// "Sinner.Application.Foo" → "Sinner.Application". The first two
    /// segments together form the bounded-context identity, mirroring the
    /// canon's "second pass on bounded contexts".
    /// </summary>
    internal static string BoundedContextOf(string? ns)
    {
        if (string.IsNullOrEmpty(ns)) return string.Empty;
        if (ns.StartsWith("System", StringComparison.Ordinal)) return string.Empty;
        if (ns.StartsWith("Microsoft", StringComparison.Ordinal)) return string.Empty;
        var parts = ns.Split('.');
        if (parts.Length < 2) return parts[0];
        return parts[0] + "." + parts[1];
    }

    private Violation BuildCycleViolation(
        AnalysisContext context, IReadOnlyList<string> members, string cycleKind)
    {
        var description = string.Join(" <-> ", members);
        return new Violation(
            RuleId: "LNTY-007",
            Severity: Severity.Critical,
            IsHardLock: true,
            File: $"<{cycleKind}>",
            Line: 1,
            Column: 1,
            SymbolFqn: description,
            CodeSnippet: description,
            AstKind: cycleKind == "project_cycle" ? "ProjectGraphCycle" : "BoundedContextGraphCycle",
            AdditionalContext: new Dictionary<string, string>
            {
                ["cycle_kind"] = cycleKind,
                ["cycle_members"] = string.Join(",", members),
            });
    }

    /// <summary>Iterative Tarjan SCC. Stable ordering: nodes processed alphabetically.</summary>
    private static List<List<string>> TarjanScc(Dictionary<string, HashSet<string>> graph)
    {
        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLink = new Dictionary<string, int>(StringComparer.Ordinal);
        var sccs = new List<List<string>>();

        var nodes = graph.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        foreach (var v in nodes)
        {
            if (!indices.ContainsKey(v))
                StrongConnect(v);
        }

        return sccs;

        void StrongConnect(string v)
        {
            // Iterative emulation of Tarjan to avoid stack overflows on deep graphs.
            var work = new Stack<(string node, IEnumerator<string> succ)>();
            indices[v] = index;
            lowLink[v] = index;
            index++;
            stack.Push(v);
            onStack.Add(v);
            work.Push((v, graph[v].OrderBy(s => s, StringComparer.Ordinal).GetEnumerator()));

            while (work.Count > 0)
            {
                var (node, en) = work.Peek();
                if (en.MoveNext())
                {
                    var w = en.Current;
                    if (!graph.ContainsKey(w)) continue; // edge to outside-graph node — skip
                    if (!indices.ContainsKey(w))
                    {
                        indices[w] = index;
                        lowLink[w] = index;
                        index++;
                        stack.Push(w);
                        onStack.Add(w);
                        work.Push((w, graph[w].OrderBy(s => s, StringComparer.Ordinal).GetEnumerator()));
                    }
                    else if (onStack.Contains(w))
                    {
                        lowLink[node] = System.Math.Min(lowLink[node], indices[w]);
                    }
                }
                else
                {
                    work.Pop();
                    if (lowLink[node] == indices[node])
                    {
                        var scc = new List<string>();
                        string popped;
                        do
                        {
                            popped = stack.Pop();
                            onStack.Remove(popped);
                            scc.Add(popped);
                        } while (popped != node);
                        scc.Sort(StringComparer.Ordinal);
                        sccs.Add(scc);
                    }
                    if (work.Count > 0)
                    {
                        var parent = work.Peek().node;
                        lowLink[parent] = System.Math.Min(lowLink[parent], lowLink[node]);
                    }
                }
            }
        }
    }
}
