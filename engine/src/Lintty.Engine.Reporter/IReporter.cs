namespace Lintty.Engine.Reporter;

/// <summary>
/// Generates the audit PDF from the engine's report JSON.
/// Implementations MUST be deterministic: same input JSON → same PDF binary,
/// byte-for-byte. This is the contract that backs the Sales Cut pitch
/// "100% determinístico, zero alucinação, zero custo de inferência".
/// </summary>
public interface IReporter
{
    /// <summary>
    /// Generates the PDF for <paramref name="reportJson"/> at <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="reportJson">JSON conforming to <c>Lintty.Engine.Core.Output.ReportDto</c>.</param>
    /// <param name="outputPath">Absolute path of the .pdf file to write.</param>
    /// <returns>The <c>hash_content</c> printed in the footer (sha256 of the report JSON, full hex).</returns>
    string GeneratePdf(string reportJson, string outputPath);
}
