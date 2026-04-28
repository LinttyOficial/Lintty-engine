using System.Globalization;
using Lintty.Engine.Reporter.Theming;

namespace Lintty.Engine.Reporter.Layout;

/// <summary>
/// Pure string helpers shared by layout composers. All formatting goes
/// through <see cref="CultureInfo.InvariantCulture"/> so PDF output is
/// independent of the host locale (ADR 0003 §5.1).
/// </summary>
internal static class TextHelpers
{
    public static string GradeColor(string grade) => grade switch
    {
        "A" => LinttyColors.GradeAGreen,
        "F" => LinttyColors.GradeFRed,
        _ => LinttyColors.GradeMidGray,
    };

    public static string SeverityLabel(string severity) => severity.ToUpperInvariant() switch
    {
        "CRITICAL" => "Crítica",
        "HIGH" => "Alta",
        "MEDIUM" => "Média",
        "LOW" => "Baixa",
        _ => severity,
    };

    public static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);

    public static string FormatFileLine(string file, int line)
        => $"{file}:{line.ToString(CultureInfo.InvariantCulture)}";
}
