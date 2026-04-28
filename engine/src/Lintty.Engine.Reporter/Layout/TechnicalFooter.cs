using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Per-page technical footer (ADR 0003 §4.1 + §5.2 option B): canon, run_id,
/// hash_content (sha256 of report JSON, NOT of the PDF binary, to avoid
/// circular reference), inference_signature placeholder, generator tag.
/// </summary>
internal static class TechnicalFooter
{
    public const string Generator = "lintty-engine 0.1.0 (QuestPDF reporter)";

    public static void Compose(IContainer container, ReportView report, string hashContentHex)
    {
        // Trim full hex to 16 chars for the on-page footer; full hex still
        // returned by the reporter API for callers that want the long form.
        var hashShort = hashContentHex.Length >= 16 ? hashContentHex.Substring(0, 16) : hashContentHex;

        container.PaddingTop(6).BorderTop(0.4f).BorderColor(LinttyColors.LineFaint).PaddingTop(4)
            .Column(col =>
            {
                col.Item().Text(text =>
                {
                    text.DefaultTextStyle(s => s
                        .FontFamily(EmbeddedFonts.Mono).FontSize(7)
                        .FontColor(LinttyColors.TextSecondary));

                    text.Span("canon_version: ").Bold();
                    text.Span(report.CanonVersion);
                    text.Span("  •  rule_set: ").Bold();
                    text.Span(report.RuleSetVersion);
                    text.Span("  •  run_id: ").Bold();
                    text.Span(report.RunId);
                    text.Span("  •  hash_content: ").Bold();
                    text.Span("sha256:" + hashShort);
                });

                col.Item().Text(text =>
                {
                    text.DefaultTextStyle(s => s
                        .FontFamily(EmbeddedFonts.Mono).FontSize(7)
                        .FontColor(LinttyColors.TextSecondary));

                    text.Span("inference_signature: ").Bold();
                    text.Span("null  ");
                    text.Span("•  generated_by: ").Bold();
                    text.Span(Generator);
                });
            });
    }
}
