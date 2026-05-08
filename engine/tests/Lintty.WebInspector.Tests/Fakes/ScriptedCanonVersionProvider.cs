using Lintty.WebInspector.Canon;

namespace Lintty.WebInspector.Tests.Fakes;

/// <summary>
/// Mutable <see cref="ICanonVersionProvider"/> for the canon-snapshot
/// regression test (ADR 0007 §3.7). Lets the test simulate "the canon
/// advanced after a scan was queued" by flipping <see cref="Current"/>
/// between trigger and worker pickup.
///
/// <para>
/// The default implementation (<see cref="DefaultCanonVersionProvider"/>)
/// returns a const string and a unit test against it would prove nothing —
/// we need a knob to demonstrate that <see cref="Lintty.WebInspector.Scans.ScanService.TriggerAsync"/>
/// reads the value once and persists it onto the row, and that the worker
/// then replays the row's <c>canon_version</c> regardless of what the
/// provider says later.
/// </para>
///
/// <para>
/// Not thread-safe by design: the test controls when the value flips and
/// when the worker reads. If a later test needs concurrent flipping, swap
/// to a <c>volatile string</c> field.
/// </para>
/// </summary>
public sealed class ScriptedCanonVersionProvider : ICanonVersionProvider
{
    /// <summary>
    /// The string returned by the next <see cref="GetCurrent"/> call. The
    /// test mutates this between trigger and worker pickup to simulate a
    /// canon bump.
    /// </summary>
    public string Current { get; set; } = DefaultCanonVersionProvider.Version;

    public string GetCurrent() => Current;
}
