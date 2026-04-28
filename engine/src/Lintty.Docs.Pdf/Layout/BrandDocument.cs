using System;
using System.Collections.Generic;
using Lintty.Docs.Pdf.Brand;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lintty.Docs.Pdf.Layout;

/// <summary>
/// QuestPDF <see cref="IDocument"/> for white-label Lintty documents.
/// Cover page is mandatory. A second page is always rendered: it shows the
/// supplied <see cref="ContentBlock"/>s, or a soft "conteúdo do documento"
/// hint when the body is empty (so previews communicate "rest goes here").
///
/// Document metadata is pinned to fixed timestamps so the binary stays
/// reproducible across runs (same input → same PDF). Mirrors the determinism
/// contract of the audit Reporter (ADR 0003 §5.1).
/// </summary>
internal sealed class BrandDocument : IDocument
{
    private readonly CoverContent _cover;
    private readonly IReadOnlyList<ContentBlock> _body;

    private static readonly DateTime FixedTimestamp =
        DateTime.SpecifyKind(new DateTime(2000, 1, 1, 0, 0, 0), DateTimeKind.Utc);

    public BrandDocument(CoverContent cover, IReadOnlyList<ContentBlock> body)
    {
        _cover = cover;
        _body = body;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = _cover.Title,
        Author = "Lintty",
        Subject = _cover.Subtitle ?? _cover.Title,
        Keywords = "lintty, document, brand",
        Producer = "lintty-docs (QuestPDF)",
        Creator = "lintty-docs 0.1.0",
        CreationDate = FixedTimestamp,
        ModifiedDate = FixedTimestamp,
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        // ── Cover page ───────────────────────────────────────────────────
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(48);
            page.PageColor(BrandColors.Paper);
            page.DefaultTextStyle(s => s
                .FontFamily(BrandFonts.Sans).FontSize(10)
                .FontColor(BrandColors.Ink));

            page.Header().Element(c => CoverPage.ComposeHeader(c, _cover));
            page.Content().Element(c => CoverPage.ComposeContent(c, _cover));
            page.Footer().Element(c => CoverPage.ComposeFooter(c, _cover));
        });

        // ── Body page (placeholder when empty) ───────────────────────────
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(48);
            page.PageColor(BrandColors.Paper);
            page.DefaultTextStyle(s => s
                .FontFamily(BrandFonts.Sans).FontSize(10.5f)
                .FontColor(BrandColors.Ink));

            page.Header().PaddingBottom(16).Row(row =>
            {
                // Full transparent logo (icon + wordmark + tagline) at 32pt
                // height. The PNG aspect ratio (~2:1) gives roughly 64pt of
                // width — fits the header strip without crowding the doc-id.
                row.ConstantItem(110).Height(32).AlignLeft().AlignMiddle().Element(c =>
                    BrandLogo.Compose(c, _cover.LogoPath, BrandLogo.Variant.LogoPng));
                row.RelativeItem();
                row.ConstantItem(160).AlignRight().AlignMiddle().Text(_cover.DocumentId ?? string.Empty)
                    .FontFamily(BrandFonts.Mono).FontSize(8.5f)
                    .FontColor(BrandColors.TextMuted);
            });

            page.Content().Element(c => ContentPage.Compose(c, _body));

            page.Footer().Element(c => FooterStrip.ComposePageFooter(c, _cover));
        });
    }
}
