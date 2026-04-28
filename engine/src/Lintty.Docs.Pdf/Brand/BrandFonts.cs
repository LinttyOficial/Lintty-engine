using System;
using System.IO;
using QuestPDF.Drawing;

namespace Lintty.Docs.Pdf.Brand;

/// <summary>
/// Registers Inter (sans) and JetBrains Mono in QuestPDF's font manager from
/// embedded resources. Mirrors the strict no-system-fallback policy used by
/// the audit Reporter — failure to load a TTF aborts immediately so output
/// stays predictable across machines.
/// </summary>
internal static class BrandFonts
{
    public const string Sans = "Inter";
    public const string Mono = "JetBrains Mono";

    private static readonly object _gate = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        lock (_gate)
        {
            if (_registered) return;

            Register("Lintty.Docs.Pdf.Resources.Inter-Regular.ttf");
            Register("Lintty.Docs.Pdf.Resources.Inter-SemiBold.ttf");
            Register("Lintty.Docs.Pdf.Resources.Inter-Bold.ttf");
            Register("Lintty.Docs.Pdf.Resources.JetBrainsMono-Regular.ttf");
            Register("Lintty.Docs.Pdf.Resources.JetBrainsMono-Bold.ttf");

            _registered = true;
        }
    }

    private static void Register(string resourceName)
    {
        var asm = typeof(BrandFonts).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Lintty white-label PDF: embedded font '{resourceName}' not found. " +
                "Available: " + string.Join(", ", asm.GetManifestResourceNames()));
        }

        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        FontManager.RegisterFont(buffer);
    }
}
