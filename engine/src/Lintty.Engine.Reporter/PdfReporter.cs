using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lintty.Engine.Reporter.Layout;
using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter;

/// <summary>
/// QuestPDF-based implementation of <see cref="IReporter"/>. Generates the
/// Lintty audit PDF from the engine's report JSON.
/// Determinism guarantees (ADR 0003 §5):
///   - Fixed CreationDate / ModifiedDate metadata.
///   - Embedded fonts, no system fallback.
///   - Invariant culture for all numeric formatting.
///   - Footer carries sha256 of the report JSON (option B), not of the PDF
///     binary, to avoid circular reference while still printing a verifiable
///     hash on every page.
/// </summary>
public sealed class PdfReporter : IReporter
{
    static PdfReporter()
    {
        // QuestPDF Community License is free for organizations with annual
        // gross revenue ≤ US$1M (ADR 0003 §2.1). Lintty is pre-revenue.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public string GeneratePdf(string reportJson, string outputPath)
    {
        if (reportJson is null) throw new ArgumentNullException(nameof(reportJson));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        EmbeddedFonts.EnsureRegistered();

        var report = ReportViewParser.Parse(reportJson);
        var hashContent = ComputeContentHash(reportJson);

        var doc = new LinttyDocument(report, hashContent);

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        doc.GeneratePdf(outputPath);

        return hashContent;
    }

    /// <summary>
    /// Computes sha256 of the canonicalized report JSON bytes (UTF-8, no BOM).
    /// Matches what is printed in the per-page footer.
    /// </summary>
    internal static string ComputeContentHash(string reportJson)
    {
        var bytes = Encoding.UTF8.GetBytes(reportJson);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
