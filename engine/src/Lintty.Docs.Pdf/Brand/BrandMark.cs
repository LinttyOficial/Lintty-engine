using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Docs.Pdf.Brand;

/// <summary>
/// The Lintty mark. Two surfaces:
///   • <see cref="ComposeSvg"/> — vectorized hex+L, native SVG inlined into
///     QuestPDF. Scales down crisply (header thumbnails 16-22 pt). Visually
///     simplified from the master logo: outer hex with the gradient stroke,
///     inner "L" alluding to the circuit motif. Same color family.
///   • <see cref="ComposePng"/> — the master PNG asset (logo + wordmark +
///     tagline). Used on the cover where size and detail justify it.
///
/// Master file: <c>Resources/lintty-logo.png</c>, embedded via csproj.
/// </summary>
internal static class BrandMark
{
    private const string ResourceName = "Lintty.Docs.Pdf.Resources.lintty-logo.png";

    /// <summary>
    /// Vectorized icon for in-line use. Hexagon outline with gradient
    /// (navy → teal) and a stylized "L" inside. No tagline, no wordmark
    /// — those are composed separately as text so the type stays crisp
    /// at any size.
    /// </summary>
    public const string SvgIconPayload =
        "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'>" +
            "<defs>" +
                "<linearGradient id='g' x1='0' y1='0' x2='1' y2='1'>" +
                    "<stop offset='0%' stop-color='#1F4060'/>" +
                    "<stop offset='100%' stop-color='#3FA0A8'/>" +
                "</linearGradient>" +
            "</defs>" +
            // Outer hexagon — pointy-top, centered.
            "<polygon points='32,4 56,18 56,46 32,60 8,46 8,18' " +
                     "fill='none' stroke='url(#g)' stroke-width='3.2' stroke-linejoin='round'/>" +
            // Inner secondary hexagon — gives the woven look without the full lattice.
            "<polygon points='32,16 47,24.5 47,39.5 32,48 17,39.5 17,24.5' " +
                     "fill='none' stroke='url(#g)' stroke-width='1.6' stroke-linejoin='round' opacity='0.55'/>" +
            // Stylized "L" with a circuit-style end node.
            "<path d='M27 22 L27 41 L41 41' " +
                  "fill='none' stroke='#2D7A8C' stroke-width='3.2' " +
                  "stroke-linecap='round' stroke-linejoin='round'/>" +
            "<circle cx='41' cy='41' r='2.4' fill='#5FB8C9'/>" +
            "<circle cx='27' cy='22' r='1.6' fill='#5FB8C9'/>" +
        "</svg>";

    /// <summary>
    /// Composes the vector icon into <paramref name="container"/>. Caller
    /// controls box size; SVG scales to the container.
    /// </summary>
    public static void ComposeSvg(IContainer container)
        => container.Svg(SvgIconPayload);

    /// <summary>
    /// Composes the master PNG (logo + wordmark + tagline) into the
    /// container. Use this on the cover; preserves the original artwork.
    /// </summary>
    public static void ComposePng(IContainer container)
    {
        var asm = typeof(BrandMark).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded logo '{ResourceName}' not found. Available: " +
                string.Join(", ", asm.GetManifestResourceNames()));
        }
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        container.Image(ms.ToArray()).FitArea();
    }
}
