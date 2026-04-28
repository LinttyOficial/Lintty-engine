namespace Lintty.Docs.Pdf.Brand;

/// <summary>
/// Lintty institutional palette for white-label PDF documents
/// (specs in <c>docs/brand/pdf-identity.md</c>). Distinct from the audit
/// reporter palette (<c>Lintty.Engine.Reporter.Theming.LinttyColors</c>),
/// which is task-specific (grade red/green/amber). Brand documents only
/// use neutrals + the navy / teal pair pulled from the official mark.
/// </summary>
internal static class BrandColors
{
    // ── Brand core (sampled from the official logo) ───────────────────
    /// <summary>Navy used in the wordmark and tagline. Primary brand color.</summary>
    public const string Navy         = "#1B3A5F";
    /// <summary>Teal accent — middle stop of the hex gradient. Replaces the
    /// previous saint-green accent. Used in rules, bullets, callouts.</summary>
    public const string Accent       = "#2D7A8C";
    /// <summary>Soft accent wash for callout backgrounds.</summary>
    public const string AccentSoft   = "#E5F0F4";
    /// <summary>Lighter cyan from the inner circuitry. Reserved for subtle
    /// highlights and gradients — not for standalone fills.</summary>
    public const string AccentLight  = "#5FB8C9";

    // ── Neutrals ──────────────────────────────────────────────────────
    /// <summary>Body copy; deliberately near-black for legibility.
    /// Brand elements (wordmark, H1, kicker) use <see cref="Navy"/> instead.</summary>
    public const string Ink          = "#0A0A0A";
    public const string Paper        = "#FFFFFF";
    public const string TextSecondary = "#4B5563";
    public const string TextMuted     = "#6B7280";
    public const string LineFaint     = "#E5E7EB";
    public const string Surface       = "#F8F8F7";
}
