using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// One block per detected violation with rule_id header, severity tag,
/// file:line locator, ast_kind, justification context and the code snippet
/// in mono with a faint background. Layout per <c>laudo-mock.md</c> Página 2.
/// </summary>
internal static class ViolationsList
{
    public static void Compose(IContainer container, ReportView report)
    {
        container.Column(col =>
        {
            col.Item().Text("Lista de Violações Detectadas")
                .FontFamily(EmbeddedFonts.Sans).FontSize(14).Bold()
                .FontColor(LinttyColors.TextPrimary);

            col.Item().PaddingTop(2).LineHorizontal(0.6f).LineColor(LinttyColors.LineFaint);

            if (report.Violations.Count == 0)
            {
                col.Item().PaddingTop(10).Text("Nenhuma violação detectada nesta análise.")
                    .FontFamily(EmbeddedFonts.Sans).FontSize(10)
                    .FontColor(LinttyColors.TextSecondary);
                return;
            }

            foreach (var v in report.Violations)
            {
                col.Item().PaddingTop(14).Element(c => RenderViolation(c, v));
            }
        });
    }

    private static void RenderViolation(IContainer container, ViolationView v)
    {
        container.Column(block =>
        {
            // Rule header line: "LNTY-002 — Crítica (Hard Lock)"
            block.Item().Row(row =>
            {
                row.AutoItem().Text(v.RuleId)
                    .FontFamily(EmbeddedFonts.Mono).FontSize(11).Bold()
                    .FontColor(v.IsHardLock ? LinttyColors.HardLockRed : LinttyColors.TextPrimary);

                row.AutoItem().PaddingLeft(8).Text($"— {TextHelpers.SeverityLabel(v.Severity)}{(v.IsHardLock ? " (Hard Lock)" : "")}")
                    .FontFamily(EmbeddedFonts.Sans).FontSize(11).SemiBold()
                    .FontColor(v.IsHardLock ? LinttyColors.HardLockRed : LinttyColors.TextPrimary);
            });

            // File:line and ast_kind
            block.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Arquivo: ")
                        .FontFamily(EmbeddedFonts.Sans).FontSize(9).SemiBold()
                        .FontColor(LinttyColors.TextSecondary);
                    text.Span(TextHelpers.FormatFileLine(v.File, v.Line))
                        .FontFamily(EmbeddedFonts.Mono).FontSize(9)
                        .FontColor(LinttyColors.TextPrimary);
                });
                row.AutoItem().Text(text =>
                {
                    text.Span("ast_kind: ")
                        .FontFamily(EmbeddedFonts.Sans).FontSize(9).SemiBold()
                        .FontColor(LinttyColors.TextSecondary);
                    text.Span(v.Evidence.AstKind)
                        .FontFamily(EmbeddedFonts.Mono).FontSize(9)
                        .FontColor(LinttyColors.TextPrimary);
                });
            });

            // Symbol FQN (small)
            if (!string.IsNullOrWhiteSpace(v.SymbolFqn))
            {
                block.Item().PaddingTop(2).Text(text =>
                {
                    text.Span("symbol: ")
                        .FontFamily(EmbeddedFonts.Sans).FontSize(8).SemiBold()
                        .FontColor(LinttyColors.TextSecondary);
                    text.Span(v.SymbolFqn)
                        .FontFamily(EmbeddedFonts.Mono).FontSize(8)
                        .FontColor(LinttyColors.TextSecondary);
                });
            }

            // Per-rule explanation blocks (display-only copy from RuleCatalog).
            // Two registers: stakeholder ("Por que importa") and architect
            // ("Análise arquitetural"). Graceful fallback: if the rule isn't
            // catalogued (shouldn't happen for the 7 V0 rules), skip both.
            var ruleCopy = RuleCatalog.TryGet(v.RuleId);
            if (ruleCopy is not null)
            {
                block.Item().PaddingTop(6).Text(text =>
                {
                    text.Span("Por que importa — ")
                        .FontFamily(EmbeddedFonts.Sans).FontSize(9).SemiBold()
                        .FontColor(LinttyColors.TextSecondary);
                    text.Span(ruleCopy.WhyItMatters)
                        .FontFamily(EmbeddedFonts.Sans).FontSize(9)
                        .FontColor(LinttyColors.TextPrimary).LineHeight(1.35f);
                });

                block.Item().PaddingTop(3).Text(text =>
                {
                    text.Span("Análise arquitetural — ")
                        .FontFamily(EmbeddedFonts.Sans).FontSize(9).SemiBold()
                        .FontColor(LinttyColors.TextSecondary);
                    text.Span(ruleCopy.ArchitecturalReasoning)
                        .FontFamily(EmbeddedFonts.Sans).FontSize(9)
                        .FontColor(LinttyColors.TextPrimary).LineHeight(1.35f);
                });
            }

            // Additional evidence (key=value pairs from the analyzer, e.g.,
            // constant_value, detection_pass). Renamed from "Justificativa
            // técnica" — this block is evidence, not justification.
            if (v.Evidence.AdditionalContext.Count > 0)
            {
                block.Item().PaddingTop(4).Text(text =>
                {
                    text.Span("Evidência adicional: ")
                        .FontFamily(EmbeddedFonts.Sans).FontSize(9).SemiBold()
                        .FontColor(LinttyColors.TextSecondary);

                    var first = true;
                    foreach (var kv in v.Evidence.AdditionalContext)
                    {
                        if (!first) text.Span("; ").FontFamily(EmbeddedFonts.Sans).FontSize(9).FontColor(LinttyColors.TextPrimary);
                        text.Span(kv.Key + "=").FontFamily(EmbeddedFonts.Sans).FontSize(9).FontColor(LinttyColors.TextPrimary);
                        text.Span(kv.Value).FontFamily(EmbeddedFonts.Mono).FontSize(9).FontColor(LinttyColors.TextPrimary);
                        first = false;
                    }
                });
            }

            // Code snippet block
            if (!string.IsNullOrWhiteSpace(v.Evidence.CodeSnippet))
            {
                block.Item().PaddingTop(6)
                    .Background(LinttyColors.SnippetBackground)
                    .Border(0.5f).BorderColor(LinttyColors.LineFaint)
                    .Padding(8)
                    .Text(v.Evidence.CodeSnippet)
                    .FontFamily(EmbeddedFonts.Mono).FontSize(9)
                    .FontColor(LinttyColors.TextPrimary).LineHeight(1.35f);
            }

            // Fingerprint (hash) in micro-print
            if (!string.IsNullOrWhiteSpace(v.Fingerprint))
            {
                block.Item().PaddingTop(3).AlignRight().Text("fingerprint: " + v.Fingerprint)
                    .FontFamily(EmbeddedFonts.Mono).FontSize(7)
                    .FontColor(LinttyColors.TextSecondary);
            }
        });
    }
}
