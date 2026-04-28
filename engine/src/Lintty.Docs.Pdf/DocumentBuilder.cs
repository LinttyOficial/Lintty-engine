using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lintty.Docs.Pdf.Brand;
using Lintty.Docs.Pdf.Layout;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Docs.Pdf;

/// <summary>
/// Fluent entry point for generating Lintty white-label PDFs.
///
/// Usage:
/// <code>
/// var hash = new DocumentBuilder()
///     .Cover("ADR 0004 — Brand PDF Template", subtitle: "Identidade visual...", kicker: "ADR")
///     .WithDocumentId("ADR-0004").WithVersion("v0.1").WithDate("2026-04-27")
///     .AddH1("Contexto").AddParagraph("...")
///     .AddCode("dotnet run --project ...")
///     .Build("output.pdf");
/// </code>
/// </summary>
public sealed class DocumentBuilder
{
    static DocumentBuilder()
    {
        // QuestPDF Community License — same justification as the audit
        // Reporter (ADR 0003 §2.1): free up to US$1M ARR, Lintty is pre-revenue.
        QuestPDF.Settings.License = LicenseType.Community;
        if (System.Environment.GetEnvironmentVariable("LINTTY_DOCS_DEBUG") == "1")
            QuestPDF.Settings.EnableDebugging = true;
    }

    private string? _title;
    private string? _subtitle;
    private string? _kicker;
    private string? _logoPath;
    private string? _docId;
    private string? _version;
    private string? _date;
    private string  _site = "lintty.com";

    private readonly List<ContentBlock> _body = new();

    public DocumentBuilder Cover(string title, string? subtitle = null, string? kicker = null)
    {
        _title = title;
        _subtitle = subtitle;
        _kicker = kicker;
        return this;
    }

    public DocumentBuilder WithLogo(string path)        { _logoPath = path; return this; }
    public DocumentBuilder WithDocumentId(string id)    { _docId = id; return this; }
    public DocumentBuilder WithVersion(string v)        { _version = v; return this; }
    public DocumentBuilder WithDate(string isoDate)     { _date = isoDate; return this; }
    public DocumentBuilder WithSite(string site)        { _site = site; return this; }

    public DocumentBuilder AddH1(string text)             { _body.Add(new ContentBlock.H1(text)); return this; }
    public DocumentBuilder AddH2(string text)             { _body.Add(new ContentBlock.H2(text)); return this; }
    public DocumentBuilder AddParagraph(string text)      { _body.Add(new ContentBlock.Paragraph(text)); return this; }
    public DocumentBuilder AddCode(string text)           { _body.Add(new ContentBlock.Code(text)); return this; }
    public DocumentBuilder AddBullets(params string[] items)
                                                          { _body.Add(new ContentBlock.Bullets(items)); return this; }
    public DocumentBuilder AddCallout(string text)        { _body.Add(new ContentBlock.Callout(text)); return this; }
    public DocumentBuilder AddSpacer(float height = 24)   { _body.Add(new ContentBlock.Spacer(height)); return this; }

    /// <summary>
    /// Renders the PDF to <paramref name="outputPath"/> and returns the sha256
    /// of the resulting file, useful as a content-integrity tag.
    /// </summary>
    public string Build(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(_title))
            throw new InvalidOperationException("DocumentBuilder requires a Cover(title, ...) before Build().");

        BrandFonts.EnsureRegistered();

        var cover = new CoverContent(
            Title: _title!,
            Subtitle: _subtitle,
            Kicker: _kicker,
            LogoPath: _logoPath,
            DocumentId: _docId,
            Version: _version,
            Date: _date,
            Site: _site);

        var doc = new BrandDocument(cover, _body);

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        doc.GeneratePdf(outputPath);

        return ComputeSha256(outputPath);
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
