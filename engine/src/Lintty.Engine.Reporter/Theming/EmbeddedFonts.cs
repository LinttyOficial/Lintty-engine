using System;
using System.IO;
using System.Reflection;
using QuestPDF.Drawing;

namespace Lintty.Engine.Reporter.Theming;

/// <summary>
/// Loads Inter and JetBrains Mono from embedded resources and registers them
/// in QuestPDF's font manager. ADR 0003 §4.2: no system-font fallback —
/// failure to load aborts with an explicit exception so the determinism
/// guarantee is never silently broken.
/// </summary>
internal static class EmbeddedFonts
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

            RegisterStream("Lintty.Engine.Reporter.Resources.Inter-Regular.ttf");
            RegisterStream("Lintty.Engine.Reporter.Resources.Inter-SemiBold.ttf");
            RegisterStream("Lintty.Engine.Reporter.Resources.Inter-Bold.ttf");
            RegisterStream("Lintty.Engine.Reporter.Resources.JetBrainsMono-Regular.ttf");
            RegisterStream("Lintty.Engine.Reporter.Resources.JetBrainsMono-Bold.ttf");

            _registered = true;
        }
    }

    private static void RegisterStream(string resourceName)
    {
        var asm = typeof(EmbeddedFonts).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Lintty PDF reporter: embedded font resource '{resourceName}' not found in assembly " +
                $"'{asm.FullName}'. The PDF cannot be generated without bundled fonts (ADR 0003 §4.2 forbids " +
                "system-font fallback). Available resources: " +
                string.Join(", ", asm.GetManifestResourceNames()));
        }

        // Copy to memory so QuestPDF can read it freely; FontManager keeps a reference internally.
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        FontManager.RegisterFont(buffer);
    }
}
