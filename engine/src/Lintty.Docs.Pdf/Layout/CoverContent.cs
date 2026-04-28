namespace Lintty.Docs.Pdf.Layout;

/// <summary>
/// Data passed to the cover-page composer. All fields except <see cref="Title"/>
/// are optional; the layout adapts to the items actually provided.
/// </summary>
public sealed record CoverContent(
    string Title,
    string? Subtitle = null,
    string? Kicker = null,
    string? LogoPath = null,
    string? DocumentId = null,
    string? Version = null,
    string? Date = null,
    string? Site = "lintty.com");
