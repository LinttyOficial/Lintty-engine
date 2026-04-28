using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Cover page for the Lintty audit report.
///
/// Composition (top → bottom):
///   1. Brand chancela: master logo PNG anchored to the top-left corner
///      (ADR 0004 §2.2 — logo is the only brand element allowed on the
///      cover; everything else stays on the functional severity palette so
///      the verdict reads first).
///   2. Kicker "LAUDO DE AUDITORIA ARQUITETURAL" (uppercase, BrandNavy).
///   3. 56pt × 1.5pt teal rule (BrandAccent) — single decorative beat.
///   4. Giant grade letter (A–F), color-coded by severity. Stays the
///      protagonist of the page — what a CTO sees first.
///   5. Seal label ("SELO EMITIDO" green / "SELO NÃO EMITIDO" red).
///   6. Numeric score, project metadata block.
///
/// Layout per <c>docs/sales/laudo-mock.md</c> Página 1 and ADR 0003 §4.1.
/// </summary>
internal static class CoverPage
{
    public static void Compose(IContainer container, ReportView report)
    {
        var logoBytes = EmbeddedLogo.LoadPngBytes();

        container.Column(col =>
        {
            // 1. Brand chancela (top-left). MaxHeight keeps the logo as a
            //    signature, not a headline — anti-pattern explicitly forbids
            //    "logo gigante na capa" stealing focus from the grade letter.
            col.Item().AlignLeft().Height(44).Image(logoBytes).FitHeight();

            // 2. Kicker — institutional title, sits under the logo.
            col.Item().PaddingTop(18).Text("LAUDO DE AUDITORIA ARQUITETURAL")
                .FontFamily(EmbeddedFonts.Sans).FontSize(10).SemiBold()
                .FontColor(LinttyColors.BrandNavy).LetterSpacing(0.18f);

            // 3. Single accent rule — the only decorative beat on the cover.
            col.Item().PaddingTop(8).Width(56).Height(1.5f)
                .Background(LinttyColors.BrandAccent);

            // 4. Verdict cluster: grade letter centered on the page, with the
            //    hexagonal seal as a satellite to the right. The left column
            //    is a transparent spacer matching the seal's width, so the
            //    grade letter remains visually centered (mirror padding) even
            //    though only one side carries the seal.
            col.Item().PaddingTop(48).Row(row =>
            {
                row.ConstantItem(140); // mirror spacer — keeps grade centered

                row.RelativeItem().AlignCenter().AlignMiddle().Text(report.Grade)
                    .FontFamily(EmbeddedFonts.Sans).FontSize(160).Bold()
                    .FontColor(TextHelpers.GradeColor(report.Grade));

                row.ConstantItem(140).AlignCenter().AlignMiddle()
                    .Element(c => SealBadge.Compose(c, report));
            });

            col.Item().PaddingTop(8).AlignCenter()
                .Text($"Score numérico: {TextHelpers.Number(report.Score)}/100")
                .FontFamily(EmbeddedFonts.Sans).FontSize(10)
                .FontColor(LinttyColors.TextSecondary);

            // 6. Project metadata block — anchored visually at the bottom of
            //    the cover via the top padding above and the natural cover
            //    margins. Hairline above the block separates it from the
            //    verdict cluster.
            col.Item().PaddingTop(60).BorderTop(0.5f).BorderColor(LinttyColors.LineFaint)
                .PaddingTop(12).Column(meta =>
            {
                MetaRow(meta, "Solution", report.SolutionPath);
                MetaRow(meta, "Canon",    $"v{report.CanonVersion} (rule_set v{report.RuleSetVersion})");
                MetaRow(meta, "Run ID",   report.RunId, mono: true);
                MetaRow(meta, "Compile",  report.CompileStatus);
                MetaRow(meta, "Grade",    $"{report.Grade}  •  Score {TextHelpers.Number(report.Score)}/100");
            });
        });
    }

    private static void MetaRow(ColumnDescriptor col, string label, string value, bool mono = false)
    {
        col.Item().PaddingVertical(3).Row(row =>
        {
            row.ConstantItem(110).Text(label)
                .FontFamily(EmbeddedFonts.Sans).FontSize(9).SemiBold()
                .FontColor(LinttyColors.TextSecondary);
            row.RelativeItem().Text(value)
                .FontFamily(mono ? EmbeddedFonts.Mono : EmbeddedFonts.Sans).FontSize(10)
                .FontColor(LinttyColors.TextPrimary);
        });
    }
}
