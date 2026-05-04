using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace Lintty.Engine.Reporter.Tests;

/// <summary>
/// Bit-for-bit determinism gate (ADR 0003 §5.3). The Sales Cut pitch leans on
/// "same input JSON → same PDF binary, sha256 stable". If this test ever
/// turns yellow, Sprint 1 doesn't ship.
/// </summary>
public sealed class DeterminismTests
{
    [Fact]
    public void Pdf_Generation_Is_Deterministic_Bit_For_Bit_Sinner()
        => AssertDeterministic(FixturePaths.SinnerExpectedJson);

    [Fact]
    public void Pdf_Generation_Is_Deterministic_Bit_For_Bit_Saint()
        => AssertDeterministic(FixturePaths.SaintExpectedJson);

    [Fact]
    public void Pdf_Generation_Is_Deterministic_Bit_For_Bit_Ninja01()
        => AssertDeterministic(FixturePaths.Ninja01ExpectedJson);

    [Fact]
    public void Pdf_Generation_Is_Deterministic_Bit_For_Bit_SaintNoSln()
        => AssertDeterministic(FixturePaths.SaintNoSlnExpectedJson);

    private static void AssertDeterministic(string expectedJsonPath)
    {
        Assert.True(File.Exists(expectedJsonPath), $"Fixture missing: {expectedJsonPath}");
        var json = File.ReadAllText(expectedJsonPath);

        var path1 = Path.Combine(Path.GetTempPath(), $"lintty-determ-1-{Guid.NewGuid():N}.pdf");
        var path2 = Path.Combine(Path.GetTempPath(), $"lintty-determ-2-{Guid.NewGuid():N}.pdf");

        try
        {
            var hash1 = new PdfReporter().GeneratePdf(json, path1);
            var hash2 = new PdfReporter().GeneratePdf(json, path2);

            // Same input JSON → identical hash_content.
            Assert.Equal(hash1, hash2);

            var sha1 = ComputeSha256(path1);
            var sha2 = ComputeSha256(path2);

            // The argument of sale: same input → same PDF binary, byte for byte.
            Assert.Equal(sha1, sha2);

            // Confirm the bytes themselves match length-wise too.
            Assert.Equal(new FileInfo(path1).Length, new FileInfo(path2).Length);
        }
        finally
        {
            TryDelete(path1);
            TryDelete(path2);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
