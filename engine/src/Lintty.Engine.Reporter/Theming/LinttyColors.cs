namespace Lintty.Engine.Reporter.Theming;

/// <summary>
/// Lintty PDF reporter color palette (ADR 0003 §4.3). Constants only — no
/// runtime state, no theming knobs in Sprint 1.
/// </summary>
internal static class LinttyColors
{
    // Neutrals
    public const string TextPrimary = "#1F2937";
    public const string TextSecondary = "#6B7280";
    public const string LineFaint = "#E5E7EB";
    public const string SnippetBackground = "#F3F4F6";
    public const string PageBackground = "#FFFFFF";

    // Grade colors
    public const string GradeFRed = "#B91C1C";
    public const string GradeAGreen = "#15803D";
    public const string GradeMidGray = "#6B7280";

    // Severity / hard lock highlights
    public const string HardLockRed = "#B91C1C";
    public const string WarnAmber = "#B45309";

    // Brand-frame tokens — sampled from the master logo (ADR 0004 §2.2).
    // STRICTLY for institutional framing: cover kicker, page header wordmark,
    // hairline accents around the logo. NEVER for grade letter, seal,
    // severity badges, snippets, or anything semantic. The laudo's reading
    // hierarchy stays severity-first; brand colors only chancel the document.
    public const string BrandNavy = "#1B3A5F";
    public const string BrandAccent = "#2D7A8C";
}
