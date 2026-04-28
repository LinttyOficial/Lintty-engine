using System.Globalization;
using Lintty.Engine.Reporter.Model;
using Lintty.Engine.Reporter.Theming;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Hexagonal architectural seal rendered as inline SVG. Echoes the master
/// logo geometry (hexagon = the very canon Lintty audits — Hexagonal/DDD)
/// and stays vector-only, so the PDF binary remains deterministic
/// (ADR 0003 §5.1: no external assets, no rasterization variance).
///
/// Two states:
///   • <c>SealEligible == true</c>  — solid teal (BrandAccent) hexagon with
///     "SELO EMITIDO" + "LINTTY CANON v{n}". Reads as a granted certification.
///   • <c>SealEligible == false</c> — outlined red hexagon with diagonal
///     hatching and "SELO NÃO EMITIDO". Reads as a refused/denied seal.
///
/// Sized at 130 × 130pt (canonical seal viewport). Colors come from the
/// existing palette only — no new tokens introduced.
/// </summary>
internal static class SealBadge
{
    // Single source of truth for hexagon geometry (flat-top, inscribed in a
    // 100×100 viewBox). Mirrors the master logo's flat-top hex orientation.
    private const string HexOuter = "50,3 91.6,26.5 91.6,73.5 50,97 8.4,73.5 8.4,26.5";
    private const string HexInner = "50,13 82.6,32 82.6,68 50,87 17.4,68 17.4,32";

    public static void Compose(IContainer container, ReportView report)
    {
        var emitted = report.SealEligible;
        var svg = emitted ? EmittedSvg() : NotEmittedSvg();
        var titleColor = emitted ? LinttyColors.BrandNavy : LinttyColors.HardLockRed;
        var subtitleColor = emitted ? LinttyColors.BrandAccent : LinttyColors.TextSecondary;
        // Positive: "SELO DE APROVAÇÃO". Negative: "APROVAÇÃO NEGADA" — drops
        // the "selo" prefix because the seal itself was not granted, and the
        // mirror reads more naturally than "SELO DE APROVAÇÃO NEGADA".
        var titleLine1 = emitted ? "SELO DE" : "APROVAÇÃO";
        var titleLine2 = emitted ? "APROVAÇÃO" : "NEGADA";

        container.Width(130).Height(130).Layers(layers =>
        {
            // Primary layer: hexagon shape (the "selo" itself)
            layers.PrimaryLayer().Svg(svg);

            // Overlay: title + canon version, vertically centered inside hex
            layers.Layer().AlignCenter().AlignMiddle().Column(col =>
            {
                col.Spacing(0);

                col.Item().AlignCenter().Text(titleLine1)
                    .FontFamily(EmbeddedFonts.Sans).FontSize(10).Bold()
                    .FontColor(titleColor).LetterSpacing(0.10f);

                col.Item().AlignCenter().Text(titleLine2)
                    .FontFamily(EmbeddedFonts.Sans).FontSize(10).Bold()
                    .FontColor(titleColor).LetterSpacing(0.10f);

                col.Item().PaddingTop(6).AlignCenter().Text("LINTTY CANON")
                    .FontFamily(EmbeddedFonts.Sans).FontSize(7).SemiBold()
                    .FontColor(subtitleColor).LetterSpacing(0.22f);

                col.Item().AlignCenter().Text("v" + report.CanonVersion)
                    .FontFamily(EmbeddedFonts.Mono).FontSize(7)
                    .FontColor(subtitleColor);
            });
        });
    }

    private static string EmittedSvg() => string.Format(CultureInfo.InvariantCulture, @"
<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' preserveAspectRatio='xMidYMid meet'>
  <polygon points='{0}' fill='{1}' stroke='{2}' stroke-width='1.4'/>
  <polygon points='{3}' fill='none' stroke='{2}' stroke-width='0.5' stroke-dasharray='1.5,1.5' opacity='0.55'/>
</svg>",
        HexOuter,
        "#E5F0F4", // BrandAccentSoft tint — same family as logo, very subtle
        LinttyColors.BrandAccent,
        HexInner);

    private static string NotEmittedSvg() => string.Format(CultureInfo.InvariantCulture, @"
<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' preserveAspectRatio='xMidYMid meet'>
  <defs>
    <pattern id='deny' patternUnits='userSpaceOnUse' width='4' height='4' patternTransform='rotate(45)'>
      <rect width='4' height='4' fill='#FFFFFF'/>
      <line x1='0' y1='0' x2='0' y2='4' stroke='{0}' stroke-width='0.6' opacity='0.35'/>
    </pattern>
  </defs>
  <polygon points='{1}' fill='url(#deny)' stroke='{0}' stroke-width='1.6'/>
  <polygon points='{2}' fill='none' stroke='{0}' stroke-width='0.5' stroke-dasharray='1.5,1.5' opacity='0.55'/>
</svg>",
        LinttyColors.HardLockRed,
        HexOuter,
        HexInner);
}
