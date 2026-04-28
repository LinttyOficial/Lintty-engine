using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lintty.Engine.Core.Output;

/// <summary>
/// Deterministic JSON serializer for <see cref="ReportDto"/>. Property order
/// is enforced via JsonPropertyOrder attributes; here we lock the rest of
/// the format (snake_case, no indentation when emitted as machine output,
/// invariant culture).
/// </summary>
public static class JsonReport
{
    public static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null, // Names are explicit on every property.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(ReportDto report, bool indented)
    {
        return JsonSerializer.Serialize(report, indented ? PrettyOptions : CompactOptions);
    }

    public static void Write(TextWriter writer, ReportDto report, bool indented)
    {
        writer.Write(Serialize(report, indented));
    }
}
