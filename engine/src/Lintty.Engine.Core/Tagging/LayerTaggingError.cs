using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lintty.Engine.Core.Tagging;

/// <summary>
/// Thrown when one or more projects in the loaded scope cannot be classified
/// into a Canon layer (Domain / DomainAbstractions / Application /
/// Infrastructure / Presentation).
///
/// The Canon (<c>docs/02-canon-v1.md</c> §"Fail-fast") forbids silent
/// fallback to <c>Layer.Unknown</c>: an unclassified project means the
/// analyzer cannot reason about violations on it, so the engine refuses to
/// produce a laudo it can't stand behind. Resolution is on the user side —
/// add the project to <c>lintty.yml</c> under <c>explicit_map</c>, or extend
/// <c>convention_map</c> with a pattern that matches it.
///
/// The CLI surfaces the formatted message on stderr and exits with code 2
/// (execution error). The Web Inspector worker maps this to
/// <c>JobErrorCode.LayerTaggingError</c> (already wired in
/// <c>JobWorker.ClassifyEngineError</c>).
/// </summary>
public sealed class LayerTaggingError : Exception
{
    public IReadOnlyList<string> UnclassifiedProjects { get; }

    public LayerTaggingError(IReadOnlyList<string> unclassifiedProjects)
        : base(BuildMessage(unclassifiedProjects))
    {
        UnclassifiedProjects = unclassifiedProjects;
    }

    private static string BuildMessage(IReadOnlyList<string> projects)
    {
        var sb = new StringBuilder();
        sb.Append("LayerTaggingError: ");
        if (projects.Count == 1)
        {
            sb.Append("project '").Append(projects[0]).Append("' could not be classified to a layer.");
        }
        else
        {
            sb.Append(projects.Count).Append(" projects could not be classified to a layer:");
            foreach (var p in projects.OrderBy(s => s, StringComparer.Ordinal))
                sb.Append("\n  - ").Append(p);
        }

        sb.Append("\n\nAdd ");
        sb.Append(projects.Count == 1 ? "it" : "them");
        sb.Append(" to lintty.yml under explicit_map:\n");
        sb.Append("\n  layer_tagging:");
        sb.Append("\n    mode: explicit");
        sb.Append("\n    explicit_map:");
        foreach (var p in projects.OrderBy(s => s, StringComparer.Ordinal))
        {
            // Suggest a sensible default — Application is the safest neutral
            // bucket for code the user hasn't classified yet.
            sb.Append("\n      ").Append(p).Append(".csproj: application");
        }
        sb.Append("\n\nAccepted layer tags: domain | domain.abstractions | application | infrastructure | presentation.");
        sb.Append("\nSee: docs/03-motor-cli.md#configuracao-de-camadas");
        return sb.ToString();
    }
}
