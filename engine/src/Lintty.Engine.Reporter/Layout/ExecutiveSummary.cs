using System;
using System.Globalization;
using System.IO;
using System.Text;
using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Auto-generated executive summary paragraph (ADR 0003 §4.1, mock §"Sumário
/// Executivo"). Pure projection of the JSON counts — no LLM, no creativity.
///
/// ADR 0006 §7.1: also renders a small "Escopo da análise" block describing
/// which target form drove the scan (.sln, single .csproj, or lintty.yml-driven).
/// The block reads the suffix of <c>solution_path</c> and the count from
/// <c>metrics.projects_analyzed</c>; no JSON schema change required.
/// </summary>
internal static class ExecutiveSummary
{
    public static void Compose(IContainer container, ReportView report)
    {
        container.Column(col =>
        {
            col.Item().Text("Sumário Executivo")
                .FontFamily(EmbeddedFonts.Sans).FontSize(14).Bold()
                .FontColor(LinttyColors.TextPrimary);

            col.Item().PaddingTop(2).LineHorizontal(0.6f).LineColor(LinttyColors.LineFaint);

            col.Item().PaddingTop(8).Text(BuildParagraph(report))
                .FontFamily(EmbeddedFonts.Sans).FontSize(10)
                .FontColor(LinttyColors.TextPrimary).LineHeight(1.4f);

            // ADR 0006 §7.1: scope block, hardcoded PT-BR strings, no
            // timestamp, no locale-dependent formatting.
            col.Item().PaddingTop(14).Text("Escopo da análise")
                .FontFamily(EmbeddedFonts.Sans).FontSize(11).Bold()
                .FontColor(LinttyColors.TextPrimary);

            col.Item().PaddingTop(2).LineHorizontal(0.4f).LineColor(LinttyColors.LineFaint);

            var (line1, line2) = BuildScopeLines(report);
            col.Item().PaddingTop(6).Text(line1)
                .FontFamily(EmbeddedFonts.Sans).FontSize(10)
                .FontColor(LinttyColors.TextPrimary).LineHeight(1.4f);
            if (line2 is not null)
            {
                col.Item().PaddingTop(2).Text(line2)
                    .FontFamily(EmbeddedFonts.Sans).FontSize(10)
                    .FontColor(LinttyColors.TextPrimary).LineHeight(1.4f);
            }
        });
    }

    internal static string BuildParagraph(ReportView r)
    {
        var totalProjects = 0;
        var totalFiles = 0;
        foreach (var entry in r.LayerSummary.Values)
        {
            totalProjects += entry.Projects;
            totalFiles += entry.Files;
        }
        var loc = r.Metrics.TotalSlocPhysical;
        var v = r.Violations.Count;
        var hardLocks = r.HardLocksHit.Count;

        var sb = new StringBuilder();
        sb.Append("Análise estática type-aware sobre solution `");
        sb.Append(r.SolutionPath);
        sb.Append("` (");
        sb.Append(totalProjects.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(totalProjects == 1 ? "projeto" : "projetos");
        sb.Append(", ");
        sb.Append(totalFiles.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(totalFiles == 1 ? "arquivo" : "arquivos");
        sb.Append(", ");
        sb.Append(loc.ToString(CultureInfo.InvariantCulture)).Append(" LoC). ");
        sb.Append(v.ToString(CultureInfo.InvariantCulture)).Append(v == 1 ? " violação detectada, " : " violações detectadas, ");
        sb.Append(hardLocks.ToString(CultureInfo.InvariantCulture)).Append(hardLocks == 1 ? " hard lock atingido. " : " hard locks atingidos. ");
        sb.Append("Selo arquitetural ");
        sb.Append(r.SealEligible ? "emitido" : "NÃO emitido");
        sb.Append(" conforme Lintty Canon v").Append(r.CanonVersion).Append(". ");
        sb.Append("Score: ").Append(r.Grade).Append(" (");
        sb.Append(r.Score.ToString(CultureInfo.InvariantCulture)).Append("/100).");
        return sb.ToString();
    }

    /// <summary>
    /// Returns (line1, line2?) for the "Escopo da análise" block per ADR 0006
    /// §7.1. Choice of variant is derived from the suffix of
    /// <c>solution_path</c>:
    /// <list type="bullet">
    ///   <item><description>ends in .sln → "N projetos carregados via {file}.sln."</description></item>
    ///   <item><description>ends in .csproj → "1 projeto: {file}.csproj."</description></item>
    ///   <item><description>basename is lintty.yml → "N projetos declarados via lintty.yml. " + LNTY-007 note.</description></item>
    /// </list>
    /// </summary>
    internal static (string Line1, string? Line2) BuildScopeLines(ReportView r)
    {
        var n = r.Metrics.ProjectsAnalyzed;
        var nStr = n.ToString(CultureInfo.InvariantCulture);
        var solutionPath = r.SolutionPath ?? string.Empty;
        var basename = Path.GetFileName(solutionPath);
        if (string.IsNullOrEmpty(basename)) basename = solutionPath;

        if (solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var noun = n == 1 ? "projeto carregado" : "projetos carregados";
            return ($"{nStr} {noun} via {basename}.", null);
        }

        if (solutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            // 1-csproj mode: always exactly one project. Spec text is fixed.
            return (
                $"1 projeto: {basename}.",
                "Detecção de ciclos (LNTY-007) limitada ao subgrafo declarado.");
        }

        if (string.Equals(basename, "lintty.yml", StringComparison.OrdinalIgnoreCase))
        {
            var noun = n == 1 ? "projeto declarado" : "projetos declarados";
            return (
                $"{nStr} {noun} via lintty.yml.",
                "Detecção de ciclos (LNTY-007) limitada ao subgrafo declarado.");
        }

        // Fallback (defensive): unknown suffix. Render a generic line so the
        // PDF still has the section. No LNTY-007 note (we don't know if the
        // graph is partial).
        var fallbackNoun = n == 1 ? "projeto analisado" : "projetos analisados";
        return ($"{nStr} {fallbackNoun} via {basename}.", null);
    }
}
