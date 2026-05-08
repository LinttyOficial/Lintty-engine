namespace Lintty.WebInspector.Canon;

/// <summary>
/// Source of truth for the canon version string snapshotted onto every
/// org-bound <see cref="Lintty.WebInspector.Persistence.Entities.Scan"/> at
/// trigger time. ADR 0007 §3.7 — the determinism invariant requires that a
/// scan queued today against canon <c>1.0.0</c> still runs with <c>1.0.0</c>
/// even if the canon advances before the worker claims it.
///
/// <para>
/// <b>Why an interface and not a constant.</b> Sprint 3 PR 4 only needs a
/// fixed string (<see cref="DefaultCanonVersionProvider"/> returns
/// <c>"1.0.0"</c>, matching <c>LinttyConfig.CanonVersion</c> and
/// <c>ReportSchema.CanonVersion</c> in <c>Lintty.Engine.Core</c>). Wrapping
/// it in a service costs nothing and keeps the trigger-time read in one
/// place — when V1.1 lets canon advance per-org via configuration or per-repo
/// via <c>repos.scan_config</c>, the swap is one DI line, not a hunt across
/// every <c>ScanService</c> caller.
/// </para>
///
/// <para>
/// <b>Why not read <c>LinttyConfig</c> directly.</b> <c>LinttyConfig</c> is
/// scoped to a single <c>lintty.yml</c> in <c>Lintty.Engine.Core</c> — it's
/// a per-analysis snapshot, not the host's idea of "current canon".
/// Coupling the WebInspector to it would invert the dependency (host →
/// per-scan config) and require materializing a yml file at trigger time
/// that the engine subprocess will re-read anyway.
/// </para>
///
/// <para>
/// <b>Pendency for PR 5/V1.1.</b> Once <c>Lintty.Engine.Core</c> exposes a
/// canonical "what version is the engine?" value (today there is none — the
/// number lives in <c>ReportSchema.CanonVersion</c> as an init default and
/// in <c>LinttyConfig</c> as a per-yml default; neither is a host-level
/// knob), this provider should read from there instead of hardcoding. PR 4
/// punts on the refactor: the hardcoded string matches the Core defaults
/// byte-for-byte, so the cross-determinism gate (PR 5
/// <c>DashboardScan_Saint_Pdf_Equals_CliDirect</c>) holds without any
/// engine-side change.
/// </para>
/// </summary>
public interface ICanonVersionProvider
{
    /// <summary>
    /// Returns the canon version to snapshot onto a freshly-triggered scan.
    /// Pure: the same call within a single host lifetime returns the same
    /// string. Determinism property of §3.7 — no time, no per-request input.
    /// </summary>
    string GetCurrent();
}

/// <summary>
/// Default implementation. Returns the V0/V1.0 canon version hardcoded
/// (<c>"1.0.0"</c>) — same value <c>Lintty.Engine.Core.Output.ReportSchema.CanonVersion</c>
/// initializes to and the <c>lintty.yml</c> parser falls back to. See the
/// interface XML doc for the V1.1 follow-up.
/// </summary>
public sealed class DefaultCanonVersionProvider : ICanonVersionProvider
{
    /// <summary>
    /// V0/V1.0 canon. Must match <c>LinttyConfig.CanonVersion</c> and
    /// <c>ReportSchema.CanonVersion</c> defaults in <c>Lintty.Engine.Core</c>;
    /// drift breaks cross-determinism with the CLI (PR 5 gate).
    /// </summary>
    public const string Version = "1.0.0";

    public string GetCurrent() => Version;
}
