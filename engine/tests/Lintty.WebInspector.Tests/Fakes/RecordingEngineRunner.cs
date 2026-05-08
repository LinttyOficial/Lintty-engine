using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Jobs;

namespace Lintty.WebInspector.Tests.Fakes;

/// <summary>
/// Decorator over an inner <see cref="IEngineRunner"/> that records every
/// invocation's arguments. Used by the canon-snapshot regression in
/// <c>WorkerIntegrationTests.DashboardScan_PinnedCanon_Survives_NewCanon</c>
/// to assert that the worker invoked the CLI with the snapshotted
/// <c>--canon-version</c>, not the host's "current canon" at completion time
/// (ADR 0007 §3.7).
///
/// <para>
/// <b>Why a decorator and not a stub.</b> The cross-determinism gate
/// (<c>DashboardScan_Saint_Pdf_Equals_CliDirect</c>) is the parallel test in
/// the same suite — it needs the engine to actually run. Decorating keeps
/// the real <see cref="EngineSubprocessRunner"/> behaviour while letting us
/// verify <i>what was passed</i>.
/// </para>
///
/// <para>
/// Thread-safe: <see cref="JobWorker"/> processes one scan at a time, but
/// the polling loop and the test thread can hit this object concurrently.
/// We lock on a private object to keep <see cref="Invocations"/> consistent
/// when the test reads it after waiting for completion.
/// </para>
/// </summary>
public sealed class RecordingEngineRunner : IEngineRunner
{
    private readonly IEngineRunner _inner;
    private readonly object _lock = new();
    private readonly List<EngineInvocation> _invocations = new();

    public RecordingEngineRunner(IEngineRunner inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// All invocations the worker has made through this decorator, in
    /// chronological order. Snapshot semantics — the returned array does
    /// not reflect later calls.
    /// </summary>
    public IReadOnlyList<EngineInvocation> Invocations
    {
        get
        {
            lock (_lock)
            {
                return _invocations.ToArray();
            }
        }
    }

    public Task<EngineRunResult> RunAsync(
        string targetPath,
        string jsonOutputPath,
        string pdfOutputPath,
        string? canonVersion,
        CancellationToken ct)
    {
        lock (_lock)
        {
            _invocations.Add(new EngineInvocation(
                TargetPath: targetPath,
                JsonOutputPath: jsonOutputPath,
                PdfOutputPath: pdfOutputPath,
                CanonVersion: canonVersion,
                AtUtc: DateTime.UtcNow));
        }
        return _inner.RunAsync(targetPath, jsonOutputPath, pdfOutputPath, canonVersion, ct);
    }
}

/// <summary>
/// Snapshot of a single <see cref="IEngineRunner.RunAsync"/> call. Mirrors
/// the parameter list verbatim so tests can assert on any of the four.
/// </summary>
public sealed record EngineInvocation(
    string TargetPath,
    string JsonOutputPath,
    string PdfOutputPath,
    string? CanonVersion,
    DateTime AtUtc);
