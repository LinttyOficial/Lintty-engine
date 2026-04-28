using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lintty.Docs.Pdf.Brand;

/// <summary>
/// Renders the Lintty mark in PDF. Three modes:
///   1. <see cref="Variant.Auto"/> — picks Png for cover-sized boxes (≥56pt
///      side) and Svg for headers. Default.
///   2. <see cref="Variant.IconSvg"/> — vector hex+L only. Use when you want
///      the icon without the wordmark/tagline (page headers, badges).
///   3. <see cref="Variant.LogoPng"/> — full master PNG (logo + wordmark +
///      tagline). Use on cover.
/// A custom <c>logoPath</c> overrides everything (PNG/JPG file on disk).
/// </summary>
internal static class BrandLogo
{
    public enum Variant { Auto, IconSvg, LogoPng }

    public static void Compose(IContainer container, string? logoPath, Variant variant = Variant.Auto)
    {
        if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
        {
            container.Image(logoPath).FitArea();
            return;
        }

        switch (variant)
        {
            case Variant.LogoPng:
                BrandMark.ComposePng(container);
                break;
            case Variant.IconSvg:
            case Variant.Auto:
            default:
                BrandMark.ComposeSvg(container);
                break;
        }
    }
}
