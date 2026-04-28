using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Continuous header for body pages (page 2 onwards). The cover carries the
/// full master logo PNG; from there on, identity is anchored by the
/// "Lintty" wordmark in BrandNavy on the left and the run_id in mono on the
/// right — same pattern documented in <c>docs/brand/pdf-identity.md</c> §6
/// for institutional documents, adapted to the audit reporter.
///
/// We deliberately do NOT render the logo PNG here: at ~22pt the full master
/// (icon + wordmark + tagline) becomes illegible (anti-pattern in
/// <c>pdf-identity.md</c> §2.4) and we don't ship a vector icon-only variant
/// in this assembly. The wordmark text + run_id is enough chancela for body
/// pages without competing with the violations content.
/// </summary>
internal static class PageHeader
{
    public static void Compose(IContainer container, ReportView report)
    {
        container.PaddingBottom(8).BorderBottom(0.5f).BorderColor(LinttyColors.LineFaint)
            .PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().AlignLeft().Text("Lintty")
                .FontFamily(EmbeddedFonts.Sans).FontSize(11).Bold()
                .FontColor(LinttyColors.BrandNavy);

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(s => s
                    .FontFamily(EmbeddedFonts.Mono).FontSize(8)
                    .FontColor(LinttyColors.TextSecondary));
                text.Span("run_id ").SemiBold();
                text.Span(report.RunId);
            });
        });
    }
}
