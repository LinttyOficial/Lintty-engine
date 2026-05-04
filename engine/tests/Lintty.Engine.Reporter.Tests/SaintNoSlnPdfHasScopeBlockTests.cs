using System.IO;
using Lintty.Engine.Reporter.Layout;
using Lintty.Engine.Reporter.Model;
using Xunit;

namespace Lintty.Engine.Reporter.Tests;

/// <summary>
/// ADR 0006 §7.1: PDFs rendered from a lintty.yml-driven report carry the
/// "Escopo da análise" block in the executive summary. The block text is
/// hardcoded in PT-BR and derived from <c>solution_path</c> +
/// <c>metrics.projects_analyzed</c> — no schema change required.
///
/// This test goes through the public PDF pipeline (proves it doesn't crash
/// and the bytes are produced) AND drills into the internal formatter
/// (<see cref="ExecutiveSummary.BuildScopeLines"/>) to confirm the exact
/// text would be rendered.
/// </summary>
public sealed class SaintNoSlnPdfHasScopeBlockTests
{
    [Fact]
    public void Saint_No_Sln_Pdf_Renders_And_Scope_Block_Says_LintyYml()
    {
        var json = File.ReadAllText(FixturePaths.SaintNoSlnExpectedJson);

        // 1. PDF path: produce + confirm > 0 bytes (sanity).
        var pdfPath = Path.Combine(Path.GetTempPath(), "saint-no-sln-scope-test.pdf");
        try
        {
            var hash = new PdfReporter().GeneratePdf(json, pdfPath);
            Assert.False(string.IsNullOrEmpty(hash));
            Assert.True(File.Exists(pdfPath));
            Assert.True(new FileInfo(pdfPath).Length > 1000);
        }
        finally
        {
            try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch { /* */ }
        }

        // 2. Formatter path: same JSON parsed -> view -> scope lines.
        var view = ReportViewParser.Parse(json);
        var (line1, line2) = ExecutiveSummary.BuildScopeLines(view);

        // ADR 0006 §7.1 lintty.yml variant.
        Assert.Equal("4 projetos declarados via lintty.yml.", line1);
        Assert.NotNull(line2);
        Assert.Contains("LNTY-007", line2!);
        Assert.Contains("subgrafo declarado", line2);
    }

    [Fact]
    public void Saint_Sln_Mode_Has_No_Lnty007_Note()
    {
        // Sanity check the .sln branch: no LNTY-007 note (full graph).
        var json = File.ReadAllText(FixturePaths.SaintExpectedJson);
        var view = ReportViewParser.Parse(json);
        var (line1, line2) = ExecutiveSummary.BuildScopeLines(view);

        Assert.Equal("4 projetos carregados via Saint.sln.", line1);
        Assert.Null(line2);
    }
}
