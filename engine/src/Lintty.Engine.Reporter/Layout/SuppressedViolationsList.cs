using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Section dedicated to violations the engine detected but a valid
/// <c>@lintty-ignore</c> directive accepted with a justification. These are
/// architectural exceptions that already passed review — surfacing them as
/// "violations" in the main list misleads a CTO scanning the laudo. Here the
/// justification (the <c>reason="…"</c> from the directive) is the primary
/// readable; rule_id and code locator stay subordinate, for traceability.
///
/// Hard locks are NEVER projected into this section: the canon refuses to
/// honor an <c>@lintty-ignore</c> on LNTY-001 / 002 / 007. They always show
/// up in <see cref="ViolationsList"/> as active, period.
/// </summary>
internal static class SuppressedViolationsList
{
    public static void Compose(IContainer container, ReportView report)
    {
        container.Column(col =>
        {
            col.Item().Text("Violações Suprimidas com Justificativa")
                .FontFamily(EmbeddedFonts.Sans).FontSize(14).Bold()
                .FontColor(LinttyColors.TextPrimary);

            col.Item().PaddingTop(2).LineHorizontal(0.6f).LineColor(LinttyColors.LineFaint);

            if (report.SuppressedViolations.Count == 0)
            {
                col.Item().PaddingTop(10).Text("Nenhuma violação suprimida com @lintty-ignore nesta análise.")
                    .FontFamily(EmbeddedFonts.Sans).FontSize(10)
                    .FontColor(LinttyColors.TextSecondary);
                return;
            }

            // Short framing so a reader skimming the section knows what it's
            // looking at without flipping back to the canon spec. Hardcoded
            // PT-BR string — no locale-dependent formatting, deterministic.
            col.Item().PaddingTop(8).Text(
                "Violações detectadas pelo motor para as quais a equipe registrou uma " +
                "justificativa via diretiva @lintty-ignore. O motor preserva a evidência " +
                "técnica (regra, arquivo:linha, fingerprint) para auditoria, mas estas " +
                "exceções não contam contra o score nem bloqueiam o selo arquitetural.")
                .FontFamily(EmbeddedFonts.Sans).FontSize(9)
                .FontColor(LinttyColors.TextSecondary).LineHeight(1.4f);

            foreach (var item in report.SuppressedViolations)
            {
                col.Item().PaddingTop(14).Element(c => RenderSuppressed(c, item));
            }
        });
    }

    private static void RenderSuppressed(IContainer container, SuppressedViolationView item)
    {
        var v = item.Violation;
        container.Column(block =>
        {
            // Header: "LNTY-XXX — Severity (suprimida)"
            block.Item().Row(row =>
            {
                row.AutoItem().Text(v.RuleId)
                    .FontFamily(EmbeddedFonts.Mono).FontSize(11).Bold()
                    .FontColor(LinttyColors.TextSecondary);

                row.AutoItem().PaddingLeft(8).Text($"— {TextHelpers.SeverityLabel(v.Severity)} (suprimida)")
                    .FontFamily(EmbeddedFonts.Sans).FontSize(11).SemiBold()
                    .FontColor(LinttyColors.TextSecondary);
            });

            // File:line locator (mono, secondary register)
            block.Item().PaddingTop(2).Text(text =>
            {
                text.Span("Arquivo: ")
                    .FontFamily(EmbeddedFonts.Sans).FontSize(9).SemiBold()
                    .FontColor(LinttyColors.TextSecondary);
                text.Span(TextHelpers.FormatFileLine(v.File, v.Line))
                    .FontFamily(EmbeddedFonts.Mono).FontSize(9)
                    .FontColor(LinttyColors.TextPrimary);
            });

            // Justification — the headline of this section. Rendered as a
            // soft-background block so the eye finds it before the rule id /
            // fingerprint. Body register, slightly larger than the technical
            // metadata.
            block.Item().PaddingTop(8)
                .Background(LinttyColors.SnippetBackground)
                .BorderLeft(2.5f).BorderColor(LinttyColors.BrandAccent)
                .Padding(10)
                .Column(j =>
                {
                    j.Item().Text("Justificativa registrada")
                        .FontFamily(EmbeddedFonts.Sans).FontSize(8).SemiBold()
                        .FontColor(LinttyColors.TextSecondary).LetterSpacing(0.10f);

                    j.Item().PaddingTop(3).Text(item.Justification)
                        .FontFamily(EmbeddedFonts.Sans).FontSize(10)
                        .FontColor(LinttyColors.TextPrimary).LineHeight(1.4f);
                });

            // Fingerprint stays as the auditable trail-end on the right.
            if (!string.IsNullOrWhiteSpace(v.Fingerprint))
            {
                block.Item().PaddingTop(4).AlignRight().Text("fingerprint: " + v.Fingerprint)
                    .FontFamily(EmbeddedFonts.Mono).FontSize(7)
                    .FontColor(LinttyColors.TextSecondary);
            }
        });
    }
}
