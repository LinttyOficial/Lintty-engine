using System;
using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Top-level QuestPDF <see cref="IDocument"/> for the Lintty audit report.
/// Orchestrates all pages and pins document metadata to fixed values so the
/// PDF binary is bit-for-bit deterministic (ADR 0003 §5.1).
/// </summary>
internal sealed class LinttyDocument : IDocument
{
    private readonly ReportView _report;
    private readonly string _hashContent;

    // Fixed creation/modification timestamps. Any non-deterministic value here
    // would leak into the PDF metadata stream and break sha256 reproducibility.
    private static readonly DateTime FixedTimestamp =
        DateTime.SpecifyKind(new DateTime(2000, 1, 1, 0, 0, 0), DateTimeKind.Utc);

    public LinttyDocument(ReportView report, string hashContent)
    {
        _report = report;
        _hashContent = hashContent;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = "Lintty — Laudo de Auditoria Arquitetural",
        Author = "Lintty Engine",
        Subject = $"Architectural audit for {_report.SolutionPath}",
        Keywords = "lintty, architecture, audit, " + _report.Grade,
        Producer = "Lintty Engine (QuestPDF)",
        Creator = "Lintty Engine 0.1.0",
        CreationDate = FixedTimestamp,
        ModifiedDate = FixedTimestamp,
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.PageColor(LinttyColors.PageBackground);
            page.DefaultTextStyle(s => s
                .FontFamily(EmbeddedFonts.Sans)
                .FontSize(10)
                .FontColor(LinttyColors.TextPrimary));

            page.Content().Element(c => CoverPage.Compose(c, _report));
            page.Footer().Element(c => TechnicalFooter.Compose(c, _report, _hashContent));
        });

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.PageColor(LinttyColors.PageBackground);
            page.DefaultTextStyle(s => s
                .FontFamily(EmbeddedFonts.Sans)
                .FontSize(10)
                .FontColor(LinttyColors.TextPrimary));

            page.Header().Element(c => PageHeader.Compose(c, _report));
            page.Content().PaddingTop(12).Column(col =>
            {
                col.Item().Element(c => ExecutiveSummary.Compose(c, _report));
                col.Item().PaddingTop(20).Element(c => ExceptionsSummary.Compose(c, _report));
                col.Item().PaddingTop(20).Element(c => DependencyGraphPlaceholder.Compose(c, _report));
            });

            page.Footer().Element(c => TechnicalFooter.Compose(c, _report, _hashContent));
        });

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.PageColor(LinttyColors.PageBackground);
            page.DefaultTextStyle(s => s
                .FontFamily(EmbeddedFonts.Sans)
                .FontSize(10)
                .FontColor(LinttyColors.TextPrimary));

            page.Header().Element(c => PageHeader.Compose(c, _report));
            page.Content().PaddingTop(12).Element(c => ViolationsList.Compose(c, _report));
            page.Footer().Element(c => TechnicalFooter.Compose(c, _report, _hashContent));
        });
    }
}
