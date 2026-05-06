using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Lists @lintty-ignore suppressions from <c>report.exceptions</c>.
/// Header is monospaced, rows alternate by neutral hairline only (no zebra
/// striping per ADR 0003 §4.3 "professional, not dashboard").
/// </summary>
internal static class ExceptionsSummary
{
    public static void Compose(IContainer container, ReportView report)
    {
        container.Column(col =>
        {
            col.Item().Text("Sumário de Exceções")
                .FontFamily(EmbeddedFonts.Sans).FontSize(14).Bold()
                .FontColor(LinttyColors.TextPrimary);

            col.Item().PaddingTop(2).LineHorizontal(0.6f).LineColor(LinttyColors.LineFaint);

            if (report.Exceptions.Count == 0)
            {
                col.Item().PaddingTop(8).Text("Nenhuma supressão `@lintty-ignore` registrada nesta análise.")
                    .FontFamily(EmbeddedFonts.Sans).FontSize(10)
                    .FontColor(LinttyColors.TextSecondary);
                return;
            }

            col.Item().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(70);   // rule_id
                    c.RelativeColumn(2.5f); // file:line
                    c.RelativeColumn(2.5f); // author
                    c.RelativeColumn(5);    // justification
                    c.ConstantColumn(50);   // valid?
                });

                Header(table, "rule_id");
                Header(table, "arquivo:linha");
                Header(table, "autor_git");
                Header(table, "justificativa");
                Header(table, "válida");

                foreach (var ex in report.Exceptions)
                {
                    Cell(table, ex.RuleId, mono: true);
                    Cell(table, TextHelpers.FormatFileLine(ex.File, ex.Line), mono: true);
                    Cell(table, ex.AuthorGitEmail ?? "—");
                    Cell(table, ex.Justification);
                    Cell(table, ex.Valid ? "sim" : ("não" + (ex.InvalidReason is null ? "" : $" ({ex.InvalidReason})")),
                        color: ex.Valid ? LinttyColors.TextPrimary : LinttyColors.HardLockRed);
                }
            });
        });
    }

    private static void Header(TableDescriptor table, string text)
    {
        table.Cell().BorderBottom(0.6f).BorderColor(LinttyColors.LineFaint).PaddingBottom(4)
            .Text(text)
            .FontFamily(EmbeddedFonts.Mono).FontSize(8).Bold()
            .FontColor(LinttyColors.TextSecondary);
    }

    private static void Cell(TableDescriptor table, string text, bool mono = false, string? color = null)
    {
        // Exception file paths can be deep and justifications can be long
        // single tokens. The 2024.3+ layout engine wraps mid-token within the
        // declared column width, which keeps the table from triggering
        // "conflicting size constraints" on pathological inputs.
        table.Cell().BorderBottom(0.4f).BorderColor(LinttyColors.LineFaint).PaddingVertical(4).PaddingRight(6)
            .Text(text)
            .FontFamily(mono ? EmbeddedFonts.Mono : EmbeddedFonts.Sans).FontSize(8.5f)
            .FontColor(color ?? LinttyColors.TextPrimary);
    }
}
