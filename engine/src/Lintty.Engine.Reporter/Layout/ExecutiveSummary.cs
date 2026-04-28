using System.Globalization;
using System.Text;
using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Auto-generated executive summary paragraph (ADR 0003 §4.1, mock §"Sumário
/// Executivo"). Pure projection of the JSON counts — no LLM, no creativity.
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
}
