using System.Collections.Generic;
using Lintty.Docs.Pdf.Brand;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Docs.Pdf.Layout;

/// <summary>
/// Renders the body of the document — the "rest" that is filled in per
/// document. Content is supplied as a list of <see cref="ContentBlock"/>s
/// (heading, paragraph, code, bullet list, spacer, callout). When the list
/// is empty, a soft placeholder is drawn so the cover-only PDF still shows
/// where future content will land.
/// </summary>
internal static class ContentPage
{
    public static void Compose(IContainer container, IReadOnlyList<ContentBlock> blocks)
    {
        container.Column(col =>
        {
            if (blocks.Count == 0)
            {
                col.Item().PaddingTop(120).AlignCenter().Text("Conteúdo do documento")
                    .FontFamily(BrandFonts.Sans).FontSize(11).SemiBold()
                    .FontColor(BrandColors.TextMuted).LetterSpacing(0.12f);
                col.Item().PaddingTop(8).AlignCenter().Text("Inserido conforme o contexto de cada documento.")
                    .FontFamily(BrandFonts.Sans).FontSize(10)
                    .FontColor(BrandColors.TextMuted);
                return;
            }

            foreach (var block in blocks)
                Render(col, block);
        });
    }

    private static void Render(ColumnDescriptor col, ContentBlock block)
    {
        switch (block)
        {
            case ContentBlock.H1 h1:
                col.Item().PaddingTop(24).Text(h1.Text)
                    .FontFamily(BrandFonts.Sans).FontSize(22).Bold()
                    .FontColor(BrandColors.Navy);
                col.Item().PaddingTop(6).Width(40).Height(1.5f).Background(BrandColors.Accent);
                break;

            case ContentBlock.H2 h2:
                col.Item().PaddingTop(20).Text(h2.Text)
                    .FontFamily(BrandFonts.Sans).FontSize(15).SemiBold()
                    .FontColor(BrandColors.Navy);
                break;

            case ContentBlock.Paragraph p:
                col.Item().PaddingTop(10).Text(p.Text)
                    .FontFamily(BrandFonts.Sans).FontSize(10.5f)
                    .FontColor(BrandColors.Ink).LineHeight(1.55f);
                break;

            case ContentBlock.Code c:
                col.Item().PaddingTop(12).Background(BrandColors.Surface)
                    .Padding(12).Text(c.Text)
                        .FontFamily(BrandFonts.Mono).FontSize(9.5f)
                        .FontColor(BrandColors.Ink).LineHeight(1.45f);
                break;

            case ContentBlock.Bullets bs:
                col.Item().PaddingTop(8).Column(inner =>
                {
                    foreach (var item in bs.Items)
                    {
                        inner.Item().PaddingTop(4).Row(row =>
                        {
                            row.ConstantItem(14).Text("•")
                                .FontFamily(BrandFonts.Sans).FontSize(11)
                                .FontColor(BrandColors.Accent);
                            row.RelativeItem().Text(item)
                                .FontFamily(BrandFonts.Sans).FontSize(10.5f)
                                .FontColor(BrandColors.Ink).LineHeight(1.5f);
                        });
                    }
                });
                break;

            case ContentBlock.Callout co:
                col.Item().PaddingTop(14).Row(row =>
                {
                    row.ConstantItem(3).Background(BrandColors.Accent);
                    row.ConstantItem(12);
                    row.RelativeItem().Background(BrandColors.AccentSoft).Padding(12)
                        .Text(co.Text)
                            .FontFamily(BrandFonts.Sans).FontSize(10.5f)
                            .FontColor(BrandColors.Ink).LineHeight(1.5f);
                });
                break;

            case ContentBlock.Spacer s:
                col.Item().Height(s.Height);
                break;
        }
    }
}
