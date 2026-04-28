using System.Collections.Generic;

namespace Lintty.Docs.Pdf.Layout;

/// <summary>
/// Discriminated union of body primitives for white-label documents. Kept
/// minimal on purpose — extend only when a real document needs something the
/// existing primitives can't express. Every block renders deterministically
/// (no auto-formatted dates, no locale-sensitive numbers).
/// </summary>
public abstract record ContentBlock
{
    public sealed record H1(string Text) : ContentBlock;
    public sealed record H2(string Text) : ContentBlock;
    public sealed record Paragraph(string Text) : ContentBlock;
    public sealed record Code(string Text) : ContentBlock;
    public sealed record Bullets(IReadOnlyList<string> Items) : ContentBlock;
    public sealed record Callout(string Text) : ContentBlock;
    public sealed record Spacer(float Height) : ContentBlock;
}
