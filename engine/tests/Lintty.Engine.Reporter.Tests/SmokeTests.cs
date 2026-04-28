using System;
using System.IO;
using System.Text;
using Xunit;

namespace Lintty.Engine.Reporter.Tests;

/// <summary>
/// Smoke tests (ADR 0003 §7.1): generate Saint/Sinner/Ninja PDFs and verify
/// they are non-empty, have a valid PDF header, and exceed the 5 KB lower
/// bound that catches "blank page" regressions.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void Saint_Pdf_Is_Generated_And_Valid()
        => AssertPdfValid(FixturePaths.SaintExpectedJson);

    [Fact]
    public void Sinner_Pdf_Is_Generated_And_Valid()
        => AssertPdfValid(FixturePaths.SinnerExpectedJson);

    [Fact]
    public void Ninja01_Pdf_Is_Generated_And_Valid()
        => AssertPdfValid(FixturePaths.Ninja01ExpectedJson);

    [Fact]
    public void GeneratePdf_Throws_On_Empty_Json()
    {
        var reporter = new PdfReporter();
        var output = Path.Combine(Path.GetTempPath(), $"lintty-empty-{Guid.NewGuid():N}.pdf");
        Assert.Throws<ArgumentException>(() => reporter.GeneratePdf("", output));
    }

    private static void AssertPdfValid(string expectedJsonPath)
    {
        Assert.True(File.Exists(expectedJsonPath), $"Fixture missing: {expectedJsonPath}");
        var json = File.ReadAllText(expectedJsonPath);

        var output = Path.Combine(Path.GetTempPath(), $"lintty-smoke-{Guid.NewGuid():N}.pdf");
        try
        {
            var hash = new PdfReporter().GeneratePdf(json, output);

            Assert.True(File.Exists(output), "PDF was not produced.");
            var size = new FileInfo(output).Length;
            Assert.True(size > 5_000, $"PDF too small ({size} bytes); expected >5 KB.");

            // Validate "%PDF-" magic header.
            var headerBytes = new byte[5];
            using (var fs = File.OpenRead(output))
            {
                var read = fs.Read(headerBytes, 0, headerBytes.Length);
                Assert.Equal(5, read);
            }
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(headerBytes));

            Assert.False(string.IsNullOrWhiteSpace(hash));
            Assert.Equal(64, hash.Length); // sha256 hex
        }
        finally
        {
            try { if (File.Exists(output)) File.Delete(output); } catch { /* best effort */ }
        }
    }
}
