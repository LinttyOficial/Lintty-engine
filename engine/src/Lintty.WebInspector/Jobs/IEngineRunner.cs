using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Result of running the Lintty engine CLI as a subprocess. Mirrors ADR 0001 §2:
/// 0 = grade better than --fail-on-grade gate, 1 = grade equal-or-worse,
/// 2 = execution error, 3 = usage error. We treat 0 and 1 as "ran cleanly":
/// a F-graded scan still produced JSON + PDF and is therefore success from
/// the worker's POV. The grade reading happens later, from the JSON.
/// </summary>
public sealed record EngineRunResult(int ExitCode, string Stderr);

public interface IEngineRunner
{
    /// <summary>
    /// Invokes the engine CLI as a subprocess.
    /// </summary>
    /// <param name="targetPath">
    /// Absolute path passed to the engine's target flag. ADR 0006: accepts a
    /// <c>.sln</c>, a <c>.csproj</c>, or a <c>lintty.yml</c> path. Resolution
    /// happens in-process (in <c>Lintty.Engine.Core.Workspace.TargetResolver</c>)
    /// before the worker calls this method, so the path is always one of those
    /// three concrete forms.
    /// </param>
    /// <param name="jsonOutputPath">Absolute path where the engine writes the report JSON.</param>
    /// <param name="pdfOutputPath">Absolute path where the engine writes the audit PDF.</param>
    /// <param name="ct">Cancellation token; the runner enforces its own timeout in addition.</param>
    Task<EngineRunResult> RunAsync(
        string targetPath,
        string jsonOutputPath,
        string pdfOutputPath,
        CancellationToken ct);
}
