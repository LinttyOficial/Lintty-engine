using System;
using System.IO;

namespace Lintty.Engine.Reporter.Theming;

/// <summary>
/// Loads the Lintty master logo (hexagon + wordmark + tagline) from embedded
/// resources. The PNG is linked cross-project from <c>Lintty.Docs.Pdf</c> so
/// that the brand asset lives in a single place (ADR 0004 §2.1 pattern, same
/// as the TTF fonts).
///
/// Fail-fast policy mirrors <see cref="EmbeddedFonts"/>: a missing asset is
/// not silently substituted — without the official mark we abort PDF
/// generation, otherwise the determinism guarantee and brand identity could
/// be broken in different ways across environments.
/// </summary>
internal static class EmbeddedLogo
{
    private const string ResourceName = "Lintty.Engine.Reporter.Resources.lintty-logo.png";

    /// <summary>
    /// Returns the PNG bytes of the master logo. Caller is responsible for
    /// passing them to <c>QuestPDF.Image(byte[])</c>; we do not cache because
    /// QuestPDF holds its own internal references after the page is composed.
    /// </summary>
    public static byte[] LoadPngBytes()
    {
        var asm = typeof(EmbeddedLogo).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Lintty PDF reporter: embedded logo resource '{ResourceName}' not found in assembly " +
                $"'{asm.FullName}'. The audit PDF cannot be generated without the official Lintty mark " +
                "(ADR 0003 brand-frame requirement). Available resources: " +
                string.Join(", ", asm.GetManifestResourceNames()));
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
