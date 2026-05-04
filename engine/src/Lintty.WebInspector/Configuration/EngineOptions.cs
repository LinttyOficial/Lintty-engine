namespace Lintty.WebInspector.Configuration;

/// <summary>
/// How the worker invokes the Lintty engine CLI as a subprocess.
/// Subprocess (vs library reference) is mandatory by spec §10 of
/// <c>docs/13-web-inspector.md</c>: the cross-determinism gate requires that
/// the Web Inspector and the local CLI be the exact same binary.
/// </summary>
public sealed class EngineOptions
{
    public const string SectionName = "Engine";

    /// <summary>
    /// Absolute path to the published <c>lintty-engine.dll</c>. When null, the
    /// worker falls back to a relative probe next to the WebInspector binary
    /// (build output of the sibling <c>Lintty.Engine.Cli</c> project).
    /// </summary>
    public string? CliDllPath { get; set; }

    /// <summary>
    /// Hard wall-clock timeout for the entire job (clone + build + analyze + render).
    /// Defaults to 15 minutes per spec §6.2.
    /// </summary>
    public int JobTimeoutSeconds { get; set; } = 900;

    /// <summary>
    /// Tighter timeout just for the <c>git clone</c> stage so a hung clone
    /// doesn't eat the whole job budget.
    /// </summary>
    public int CloneTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// TTL for the artifacts folder (PDF + JSON) returned via signed-by-obscurity
    /// URLs. Defaults to 24h per spec §6 LGPD.
    /// </summary>
    public int ArtifactTtlHours { get; set; } = 24;
}
