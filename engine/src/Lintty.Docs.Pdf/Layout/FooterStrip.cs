using System.Collections.Generic;
using Lintty.Docs.Pdf.Brand;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Docs.Pdf.Layout;

/// <summary>
/// Shared meta strips: cover footer (doc id · version · date · site) and the
/// per-page footer used on content pages (title left, page number right).
/// Mono font for IDs and version numbers; sans for everything else.
/// </summary>
internal static class FooterStrip
{
    public static void ComposeMetaStrip(IContainer container, CoverContent c)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(10).Height(0.6f).Background(BrandColors.LineFaint);

            var parts = new List<(string text, bool mono)>();
            if (!string.IsNullOrWhiteSpace(c.DocumentId)) parts.Add((c.DocumentId!, true));
            if (!string.IsNullOrWhiteSpace(c.Version))    parts.Add((c.Version!,    true));
            if (!string.IsNullOrWhiteSpace(c.Date))       parts.Add((c.Date!,       false));
            if (!string.IsNullOrWhiteSpace(c.Site))       parts.Add((c.Site!,       false));

            col.Item().Text(text =>
            {
                for (var i = 0; i < parts.Count; i++)
                {
                    var (value, mono) = parts[i];
                    text.Span(value)
                        .FontFamily(mono ? BrandFonts.Mono : BrandFonts.Sans)
                        .FontSize(8.5f)
                        .FontColor(BrandColors.TextMuted);

                    if (i < parts.Count - 1)
                    {
                        text.Span("  ·  ")
                            .FontFamily(BrandFonts.Sans).FontSize(8.5f)
                            .FontColor(BrandColors.LineFaint);
                    }
                }
            });
        });
    }

    /// <summary>Footer used on every non-cover page.</summary>
    public static void ComposePageFooter(IContainer container, CoverContent c)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(8).Height(0.6f).Background(BrandColors.LineFaint);
            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text(c.Title)
                    .FontFamily(BrandFonts.Sans).FontSize(8.5f)
                    .FontColor(BrandColors.TextMuted);

                row.ConstantItem(60).AlignRight().Text(text =>
                {
                    text.Span("p. ")
                        .FontFamily(BrandFonts.Sans).FontSize(8.5f).FontColor(BrandColors.TextMuted);
                    text.CurrentPageNumber()
                        .FontFamily(BrandFonts.Mono).FontSize(8.5f).FontColor(BrandColors.TextMuted);
                });
            });
        });
    }
}
