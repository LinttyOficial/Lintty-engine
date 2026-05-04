using System;
using System.Security.Cryptography;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Minimal ULID generator (Crockford Base32, 26-char canonical form).
/// 48-bit timestamp + 80-bit cryptographic randomness. Lexicographically
/// sortable by creation time, which is convenient for SQLite indexes
/// without an extra DateTime column.
///
/// We implement inline (~50 LoC) instead of pulling a NuGet package: keeps
/// the package surface of the WebInspector small and the build fully offline.
/// </summary>
public static class Ulid
{
    // Crockford Base32 alphabet (no I, L, O, U).
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Returns a fresh 26-character ULID. Uses <see cref="DateTimeOffset.UtcNow"/>
    /// for the time portion and <see cref="RandomNumberGenerator"/> for the
    /// random portion (cryptographic, not <c>Random</c>).
    /// </summary>
    public static string New()
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Span<byte> randomness = stackalloc byte[10];
        RandomNumberGenerator.Fill(randomness);
        return Encode(unixMs, randomness);
    }

    /// <summary>
    /// Quick syntactic validation: 26 chars, all in the Crockford alphabet.
    /// Does NOT verify the timestamp or randomness portions.
    /// </summary>
    public static bool IsValid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 26) return false;
        foreach (var c in value)
        {
            if (Alphabet.IndexOf(c) < 0) return false;
        }
        return true;
    }

    private static string Encode(long unixMs, ReadOnlySpan<byte> randomness)
    {
        Span<char> buf = stackalloc char[26];

        // 48-bit timestamp → 10 chars (5 bits each).
        for (var i = 9; i >= 0; i--)
        {
            buf[i] = Alphabet[(int)(unixMs & 0x1F)];
            unixMs >>= 5;
        }

        // 80-bit randomness → 16 chars. We pack 10 bytes (80 bits) into 16
        // 5-bit groups by shifting the bit cursor across the byte array.
        ulong hi = 0;
        for (var i = 0; i < 5; i++) hi = (hi << 8) | randomness[i];
        ulong lo = 0;
        for (var i = 5; i < 10; i++) lo = (lo << 8) | randomness[i];

        for (var i = 7; i >= 0; i--)
        {
            buf[10 + i] = Alphabet[(int)(hi & 0x1F)];
            hi >>= 5;
        }
        for (var i = 7; i >= 0; i--)
        {
            buf[18 + i] = Alphabet[(int)(lo & 0x1F)];
            lo >>= 5;
        }

        return new string(buf);
    }
}
