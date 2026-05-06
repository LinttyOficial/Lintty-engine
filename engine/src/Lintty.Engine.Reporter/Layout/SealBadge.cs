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
/// Three states (Canon §Selo, refined post-Sprint-1):
///   • Grade A or B (<c>SealEligible == true</c>) — solid teal (BrandAccent)
///     tinted hexagon with "SELO DE APROVAÇÃO". Reads as a granted certification.
///   • Grade C or D (<c>SealEligible == false</c>, no hard lock) — desaturated
///     gray hexagon with "SELO DE QUALIDADE NÃO EMITIDO". Reads as "did not
///     reach the certification bar" (debt acknowledged, but no canon trauma).
///   • Grade F (<c>SealEligible == false</c>, hard lock or open Critical) —
///     red hatched hexagon with "APROVAÇÃO NEGADA". Reads as a refused seal.
///
/// Both non-emitted variants stay unmistakable as "NOT approved" — neither
/// shows a check mark, both swap the seal title for an explicit negation.
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

    private enum SealVariant { Approved, NotEmitted, Denied }

    private readonly record struct SealStyle(
        string Svg,
        string TitleColor,
        string SubtitleColor,
        string Line1,
        string Line2,
        float Line1Size);

    public static void Compose(IContainer container, ReportView report)
    {
        var style = ResolveStyle(ResolveVariant(report));

        container.Width(130).Height(130).Layers(layers =>
        {
            layers.PrimaryLayer().Svg(style.Svg);
            layers.Layer().AlignCenter().AlignMiddle().Element(c =>
                ComposeOverlay(c, style, report.CanonVersion));
        });
    }

    private static SealStyle ResolveStyle(SealVariant variant) => variant switch
    {
        SealVariant.Approved => new SealStyle(
            ApprovedSvg(),
            LinttyColors.BrandNavy,
            LinttyColors.BrandAccent,
            "SELO DE",
            "APROVAÇÃO",
            10f),
        SealVariant.NotEmitted => new SealStyle(
            NotEmittedSvg(),
            LinttyColors.TextSecondary,
            LinttyColors.TextSecondary,
            "SELO DE QUALIDADE",
            "NÃO EMITIDO",
            // Line 1 carries more characters in this variant; downsize so it
            // still fits inside the hex's safe inner zone without wrapping.
            8f),
        _ => new SealStyle(
            DeniedSvg(),
            LinttyColors.HardLockRed,
            LinttyColors.TextSecondary,
            "APROVAÇÃO",
            "NEGADA",
            10f),
    };

    private static void ComposeOverlay(IContainer container, SealStyle style, string canonVersion)
    {
        container.Column(col =>
        {
            col.Spacing(0);

            col.Item().AlignCenter().Text(style.Line1)
                .FontFamily(EmbeddedFonts.Sans).FontSize(style.Line1Size).Bold()
                .FontColor(style.TitleColor).LetterSpacing(0.10f);

            col.Item().AlignCenter().Text(style.Line2)
                .FontFamily(EmbeddedFonts.Sans).FontSize(10f).Bold()
                .FontColor(style.TitleColor).LetterSpacing(0.10f);

            col.Item().PaddingTop(6).AlignCenter().Text("LINTTY CANON")
                .FontFamily(EmbeddedFonts.Sans).FontSize(7).SemiBold()
                .FontColor(style.SubtitleColor).LetterSpacing(0.22f);

            col.Item().AlignCenter().Text("v" + canonVersion)
                .FontFamily(EmbeddedFonts.Mono).FontSize(7)
                .FontColor(style.SubtitleColor);
        });
    }

    /// <summary>
    /// Maps the report's grade and seal eligibility to a visual variant. Grade
    /// is the trigger because a hard lock or open Critical collapses grade to
    /// F (Scorer §Travas absolutas), so it's the simplest-and-correct
    /// proxy for "denied". Grades C/D are the in-between: seal is not eligible
    /// (Sprint 1 rule: only A/B grant the seal) but the codebase didn't trip
    /// any canon trauma, so the visual stays neutral gray rather than
    /// red-hatched.
    /// </summary>
    private static SealVariant ResolveVariant(ReportView report)
    {
        if (report.SealEligible) return SealVariant.Approved;
        if (string.Equals(report.Grade, "F", System.StringComparison.Ordinal))
            return SealVariant.Denied;
        return SealVariant.NotEmitted;
    }

    private static string ApprovedSvg() => string.Format(CultureInfo.InvariantCulture, @"
<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' preserveAspectRatio='xMidYMid meet'>
  <polygon points='{0}' fill='{1}' stroke='{2}' stroke-width='1.4'/>
  <polygon points='{3}' fill='none' stroke='{2}' stroke-width='0.5' stroke-dasharray='1.5,1.5' opacity='0.55'/>
</svg>",
        HexOuter,
        "#E5F0F4", // BrandAccentSoft tint — same family as logo, very subtle
        LinttyColors.BrandAccent,
        HexInner);

    /// <summary>
    /// C/D state: desaturated gray hexagon. No hatching, no red — the
    /// codebase didn't fail catastrophically, it just didn't reach the
    /// approval bar. Stroke and inner ring still echo the approved geometry
    /// so the seal still reads as "this was an audit that produced a real
    /// verdict", not as a missing/broken element.
    /// </summary>
    private static string NotEmittedSvg() => string.Format(CultureInfo.InvariantCulture, @"
<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' preserveAspectRatio='xMidYMid meet'>
  <polygon points='{0}' fill='{1}' stroke='{2}' stroke-width='1.4'/>
  <polygon points='{3}' fill='none' stroke='{2}' stroke-width='0.5' stroke-dasharray='1.5,1.5' opacity='0.55'/>
</svg>",
        HexOuter,
        "#F3F4F6", // SnippetBackground — neutral, low-saturation gray
        LinttyColors.TextSecondary,
        HexInner);

    /// <summary>
    /// Grade-F state: red hatched hexagon. Reads as "approval denied" — used
    /// when a hard lock or open Critical is present (canon trauma, not just
    /// debt).
    /// </summary>
    private static string DeniedSvg() => string.Format(CultureInfo.InvariantCulture, @"
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
