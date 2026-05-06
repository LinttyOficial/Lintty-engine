using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// Deterministic slug normalizer. Lowercase, ASCII-folded, collapsed
/// non-alphanumeric runs to <c>-</c>, trimmed of leading/trailing dashes,
/// truncated to 64 chars (matching <c>orgs.slug</c> column width per ADR
/// 0007 §3.2). Empty or punctuation-only inputs return <c>"org"</c>.
///
/// Uniqueness is the caller's responsibility — slug collisions are resolved
/// by appending <c>-2</c>, <c>-3</c>, ... until a free row is found
/// (see <see cref="Lintty.WebInspector.Endpoints.AuthEndpoints"/>).
/// </summary>
public static class SlugGenerator
{
    private const int MaxLength = 64;

    public static string Slugify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "org";

        // 1. Unicode normalize + strip diacritics (NFD then drop NonSpacingMark).
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        // 2. Lowercase.
        var ascii = sb.ToString().ToLowerInvariant();

        // 3. Replace any non-[a-z0-9] run with a single dash.
        var dashed = Regex.Replace(ascii, @"[^a-z0-9]+", "-");

        // 4. Trim leading/trailing dashes; truncate.
        var trimmed = dashed.Trim('-');
        if (trimmed.Length > MaxLength) trimmed = trimmed[..MaxLength].TrimEnd('-');

        return string.IsNullOrEmpty(trimmed) ? "org" : trimmed;
    }
}
