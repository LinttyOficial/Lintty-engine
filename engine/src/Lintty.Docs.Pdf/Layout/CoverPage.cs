using Lintty.Docs.Pdf.Brand;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Docs.Pdf.Layout;

/// <summary>
/// White-label cover page. Renders into the three nominal slots of a QuestPDF
/// page (header / content / footer). Three placeholders driven by
/// <see cref="CoverContent"/>: logo (header), title and subtitle (content),
/// meta strip (footer).
/// Layout principles in <c>docs/brand/pdf-identity.md</c>.
/// </summary>
internal static class CoverPage
{
    /// <summary>Composes the header band: master logo (icon + wordmark + tagline).
    /// The logo asset already carries the wordmark and tagline, so no separate
    /// "LINTTY" text is rendered here.</summary>
    public static void ComposeHeader(IContainer container, CoverContent content)
    {
        container.Height(64).AlignLeft().Element(c =>
            BrandLogo.Compose(c, content.LogoPath, BrandLogo.Variant.LogoPng));
    }

    /// <summary>Central title block, anchored low so the upper third stays
    /// empty (editorial cover convention).</summary>
    public static void ComposeContent(IContainer container, CoverContent content)
    {
        container.AlignBottom().Column(col =>
        {
            if (!string.IsNullOrWhiteSpace(content.Kicker))
            {
                col.Item().Text(content.Kicker!.ToUpperInvariant())
                    .FontFamily(BrandFonts.Sans).FontSize(10).SemiBold()
                    .FontColor(BrandColors.Accent).LetterSpacing(0.22f);
                col.Item().Height(14);
            }

            col.Item().Text(content.Title)
                .FontFamily(BrandFonts.Sans).FontSize(38).Bold()
                .FontColor(BrandColors.Navy).LineHeight(1.1f);

            // Single accent rule below the title.
            col.Item().PaddingTop(20).Width(56).Height(1.5f).Background(BrandColors.Accent);

            if (!string.IsNullOrWhiteSpace(content.Subtitle))
            {
                col.Item().PaddingTop(20).Text(content.Subtitle)
                    .FontFamily(BrandFonts.Sans).FontSize(15)
                    .FontColor(BrandColors.TextSecondary).LineHeight(1.45f);
            }

            col.Item().Height(60);
        });
    }

    /// <summary>Composes the meta strip (doc id · version · date · site).</summary>
    public static void ComposeFooter(IContainer container, CoverContent content)
        => FooterStrip.ComposeMetaStrip(container, content);
}
