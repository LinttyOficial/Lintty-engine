using System.Collections.Generic;
using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Sales Cut fallback for the dependency graph: textual list "Layer →
/// projects → violations" derived from <c>report.layer_summary</c>, plus an
/// explicit notice that the graphical PNG/SVG diagram lands in V1+
/// (ADR 0003 §4.1 + §10).
/// </summary>
internal static class DependencyGraphPlaceholder
{
    // Canonical iteration order — never depend on Dictionary key order.
    private static readonly IReadOnlyList<string> LayerOrder = new[]
    {
        "Domain",
        "Application",
        "Infrastructure",
        "Presentation",
    };

    public static void Compose(IContainer container, ReportView report)
    {
        container.Column(col =>
        {
            col.Item().Text("Grafo de Dependências (alto nível)")
                .FontFamily(EmbeddedFonts.Sans).FontSize(14).Bold()
                .FontColor(LinttyColors.TextPrimary);

            col.Item().PaddingTop(2).LineHorizontal(0.6f).LineColor(LinttyColors.LineFaint);

            col.Item().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(140); // layer
                    c.ConstantColumn(70);  // projects
                    c.ConstantColumn(70);  // files
                    c.RelativeColumn();    // violations
                });

                Header(table, "layer");
                Header(table, "projetos");
                Header(table, "arquivos");
                Header(table, "violações");

                foreach (var layer in LayerOrder)
                {
                    if (!report.LayerSummary.TryGetValue(layer, out var entry))
                        continue;
                    Cell(table, layer);
                    Cell(table, TextHelpers.Number(entry.Projects), mono: true);
                    Cell(table, TextHelpers.Number(entry.Files), mono: true);
                    Cell(table, TextHelpers.Number(entry.Violations), mono: true,
                        color: entry.Violations > 0 ? LinttyColors.HardLockRed : LinttyColors.TextPrimary);
                }

                // Render any extra layers not in canonical order, sorted alphabetically
                // so output is stable regardless of input dict ordering.
                var seen = new HashSet<string>(LayerOrder);
                var extra = new List<string>();
                foreach (var k in report.LayerSummary.Keys)
                    if (!seen.Contains(k)) extra.Add(k);
                extra.Sort(System.StringComparer.Ordinal);
                foreach (var layer in extra)
                {
                    var entry = report.LayerSummary[layer];
                    Cell(table, layer);
                    Cell(table, TextHelpers.Number(entry.Projects), mono: true);
                    Cell(table, TextHelpers.Number(entry.Files), mono: true);
                    Cell(table, TextHelpers.Number(entry.Violations), mono: true,
                        color: entry.Violations > 0 ? LinttyColors.HardLockRed : LinttyColors.TextPrimary);
                }
            });

            // Hard-lock callout, if any
            if (report.HardLocksHit.Count > 0)
            {
                col.Item().PaddingTop(10).Background("#FEF2F2").Border(0.5f).BorderColor(LinttyColors.HardLockRed)
                    .Padding(8).Text(text =>
                    {
                        text.Span("Hard locks atingidos: ")
                            .FontFamily(EmbeddedFonts.Sans).FontSize(9).Bold().FontColor(LinttyColors.HardLockRed);
                        text.Span(string.Join(", ", report.HardLocksHit))
                            .FontFamily(EmbeddedFonts.Mono).FontSize(9).FontColor(LinttyColors.HardLockRed);
                    });
            }

            col.Item().PaddingTop(10).Background("#FFFBEB").Border(0.5f).BorderColor(LinttyColors.WarnAmber)
                .Padding(8).Text("Diagrama gráfico do grafo de dependências (PNG/SVG embedado) entra em V1+. Sales Cut usa esta tabela textual derivada de layer_summary.")
                .FontFamily(EmbeddedFonts.Sans).FontSize(8.5f)
                .FontColor(LinttyColors.WarnAmber).LineHeight(1.4f);
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
        table.Cell().BorderBottom(0.4f).BorderColor(LinttyColors.LineFaint).PaddingVertical(4).PaddingRight(6)
            .Text(text)
            .FontFamily(mono ? EmbeddedFonts.Mono : EmbeddedFonts.Sans).FontSize(9)
            .FontColor(color ?? LinttyColors.TextPrimary);
    }
}
