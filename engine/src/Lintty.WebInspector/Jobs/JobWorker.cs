using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lintty.Engine.Core.Workspace;
using Lintty.WebInspector.Configuration;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Single-job-at-a-time background service. Polls the SQLite jobs table every
/// <see cref="QueueOptions.PollIntervalMs"/>, claims one queued job, runs the
/// pipeline (clone → find sln → invoke engine CLI → move artifacts → cleanup),
/// updates the row, repeats.
///
/// The worker is the only place that mutates the filesystem outside of the
/// SQLite directory. Two roots are used:
///   * <c>/tmp/lintty-&lt;jobId&gt;/</c> — clone scratch space, deleted at the end of every job.
///   * <c>&lt;JobStorage.Root&gt;/jobs/&lt;jobId&gt;/</c> — persistent artifacts (PDF + JSON).
/// </summary>
public sealed class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly QueueOptions _queue;
    private readonly EngineOptions _engine;
    private readonly JobStorageOptions _storage;
    private readonly ILogger<JobWorker> _logger;

    public JobWorker(
        IServiceScopeFactory scopes,
        IOptions<QueueOptions> queue,
        IOptions<EngineOptions> engine,
        IOptions<JobStorageOptions> storage,
        ILogger<JobWorker> logger)
    {
        _scopes = scopes;
        _queue = queue.Value;
        _engine = engine.Value;
        _storage = storage.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobWorker started; poll interval {PollMs}ms", _queue.PollIntervalMs);
        var pollDelay = TimeSpan.FromMilliseconds(_queue.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
                var job = await store.ClaimNextQueuedAsync(stoppingToken).ConfigureAwait(false);
                if (job is null)
                {
                    await Task.Delay(pollDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }
                await RunJobAsync(scope.ServiceProvider, job, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Defense in depth: never let the worker loop die on a single bad job.
                _logger.LogError(ex, "JobWorker loop crashed; sleeping briefly before retry");
                try { await Task.Delay(pollDelay, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("JobWorker stopped");
    }

    private async Task RunJobAsync(IServiceProvider services, Job job, CancellationToken ct)
    {
        // Logging scope so every line carries the job_id automatically.
        using var _ = _logger.BeginScope(new System.Collections.Generic.Dictionary<string, object>
        {
            ["job_id"] = job.Id,
        });

        var store = services.GetRequiredService<IJobStore>();
        var git = services.GetRequiredService<IGitClient>();
        var engine = services.GetRequiredService<IEngineRunner>();

        // Sandbox roots (spec §6.2: only writeable paths are these two).
        var execContext = new JobExecutionContext(
            Job: job,
            Store: store,
            Git: git,
            Engine: engine,
            CloneRoot: Path.Combine(Path.GetTempPath(), $"lintty-{job.Id}"),
            RepoDir: Path.Combine(Path.GetTempPath(), $"lintty-{job.Id}", "repo"),
            ArtifactsRoot: ResolveArtifactsDir(job.Id));

        Directory.CreateDirectory(execContext.CloneRoot);
        Directory.CreateDirectory(execContext.ArtifactsRoot);

        try
        {
            if (!await CloneStageAsync(execContext, ct).ConfigureAwait(false)) return;

            var targetPath = await ResolveTargetStageAsync(execContext, ct).ConfigureAwait(false);
            if (targetPath is null) return;

            var runOutput = await RunEngineStageAsync(execContext, targetPath, ct).ConfigureAwait(false);
            if (runOutput is null) return;

            await MarkCompletedAsync(execContext, runOutput, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while running job");
            try { await FailAsync(store, job, JobErrorCode.InternalError, ex.Message, ct).ConfigureAwait(false); }
            catch { /* best effort */ }
        }
        finally
        {
            // ── 6. Always purge the clone scratch space ─────────────────────
            PurgeQuietly(execContext.CloneRoot);
            _logger.LogInformation("clone purged at {Time}", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// State the per-job pipeline carries through the stage methods. Lifetime
    /// is exactly one call to <see cref="RunJobAsync"/>; never shared.
    /// </summary>
    private sealed record JobExecutionContext(
        Job Job,
        IJobStore Store,
        IGitClient Git,
        IEngineRunner Engine,
        string CloneRoot,
        string RepoDir,
        string ArtifactsRoot);

    /// <summary>
    /// ── 1. Clone ──. Returns <c>true</c> on success, <c>false</c> when the
    /// job was failed and the pipeline must short-circuit.
    /// </summary>
    private async Task<bool> CloneStageAsync(JobExecutionContext c, CancellationToken ct)
    {
        c.Job.Stage = JobStage.Cloning;
        await c.Store.UpdateAsync(c.Job, ct).ConfigureAwait(false);

        var coordsParse = UrlValidator.TryParse(c.Job.GithubUrl, out var parseError);
        if (coordsParse is null)
        {
            await FailAsync(c.Store, c.Job, JobErrorCode.InternalError, parseError ?? "URL parse failed", ct).ConfigureAwait(false);
            return false;
        }

        var cloneResult = await c.Git.CloneAsync(coordsParse, c.Job.Ref, token: null, c.RepoDir, ct).ConfigureAwait(false);
        if (!cloneResult.Success)
        {
            await FailAsync(c.Store, c.Job, JobErrorCode.CloneFailed, cloneResult.ErrorMessage ?? "clone failed", ct).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    /// <summary>
    /// ── 2. Resolve target (ADR 0006) ──. Returns the resolved target path
    /// or <c>null</c> if the job was failed.
    /// </summary>
    private async Task<string?> ResolveTargetStageAsync(JobExecutionContext c, CancellationToken ct)
    {
        try
        {
            ResolvedTarget resolved;
            if (!string.IsNullOrEmpty(c.Job.SolutionPath))
            {
                // Caller supplied an explicit path inside the repo (e.g.
                // "src/Foo.sln" or "src/Foo.csproj"). Resolve through the
                // resolver so the path is validated against the same rules
                // as the implicit case.
                var explicitTarget = Path.GetFullPath(Path.Combine(c.RepoDir, c.Job.SolutionPath));
                resolved = TargetResolver.Resolve(
                    targetArg: explicitTarget,
                    cwd: c.RepoDir,
                    mode: TargetResolverMode.WebInspector);
            }
            else
            {
                resolved = TargetResolver.Resolve(
                    targetArg: null,
                    cwd: c.RepoDir,
                    mode: TargetResolverMode.WebInspector);
            }
            return resolved.SolutionPathForReporting;
        }
        catch (TargetResolutionException trex)
        {
            var (errorCode, isLegacyNoSln) = MapResolverError(trex, c.RepoDir);
            // Preserve the historical error_code for clones that legitimately
            // have zero .sln, no lintty.yml, and not exactly one .csproj —
            // see ADR 0006 §8.2.
            var finalCode = isLegacyNoSln ? JobErrorCode.NoSln : errorCode;
            await FailAsync(c.Store, c.Job, finalCode, trex.Message, ct).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// ── 3 + 4. Invoke engine and read artifacts ──. Returns the parsed
    /// summary + artifact paths/hashes, or <c>null</c> if the job was failed.
    /// </summary>
    private async Task<EngineRunOutput?> RunEngineStageAsync(
        JobExecutionContext c,
        string targetPath,
        CancellationToken ct)
    {
        c.Job.Stage = JobStage.Analyzing;
        await c.Store.UpdateAsync(c.Job, ct).ConfigureAwait(false);

        var jsonOut = Path.Combine(c.ArtifactsRoot, "report.json");
        var pdfOut = Path.Combine(c.ArtifactsRoot, "laudo.pdf");
        var run = await c.Engine.RunAsync(targetPath, jsonOut, pdfOut, ct).ConfigureAwait(false);

        // Capture engine stderr (no matter the exit code) — it carries the
        // PDF hash log line and any compile errors.
        if (!string.IsNullOrEmpty(run.Stderr))
            _logger.LogInformation("engine stderr:\n{Stderr}", run.Stderr.TrimEnd());

        if (run.ExitCode == 127)
        {
            await FailAsync(c.Store, c.Job, JobErrorCode.Timeout, run.Stderr.Trim(), ct).ConfigureAwait(false);
            return null;
        }
        if (run.ExitCode == 2)
        {
            var errorCode = ClassifyEngineError(run.Stderr);
            await FailAsync(c.Store, c.Job, errorCode, FirstLine(run.Stderr), ct).ConfigureAwait(false);
            return null;
        }
        if (run.ExitCode != 0 && run.ExitCode != 1)
        {
            await FailAsync(c.Store, c.Job, JobErrorCode.InternalError,
                $"engine returned exit code {run.ExitCode.ToString(CultureInfo.InvariantCulture)}: {FirstLine(run.Stderr)}", ct).ConfigureAwait(false);
            return null;
        }

        // ── 4. Read JSON to extract score/grade/etc. ────────────────────
        if (!File.Exists(jsonOut) || !File.Exists(pdfOut))
        {
            await FailAsync(c.Store, c.Job, JobErrorCode.InternalError,
                "engine completed but artifacts are missing.", ct).ConfigureAwait(false);
            return null;
        }

        var jsonBytes = await File.ReadAllBytesAsync(jsonOut, ct).ConfigureAwait(false);
        var jsonHash = Sha256(jsonBytes);
        var pdfBytes = await File.ReadAllBytesAsync(pdfOut, ct).ConfigureAwait(false);
        var pdfHash = Sha256(pdfBytes);

        _logger.LogInformation("artifacts: json sha256={JsonHash} pdf sha256={PdfHash} pdf bytes={PdfBytes}",
            jsonHash, pdfHash, pdfBytes.Length);

        var summary = ParseReportSummary(jsonBytes);
        return new EngineRunOutput(jsonOut, pdfOut, summary);
    }

    /// <summary>
    /// ── 5. Mark completed ──. Updates the job row with grade/score/paths.
    /// </summary>
    private async Task MarkCompletedAsync(JobExecutionContext c, EngineRunOutput output, CancellationToken ct)
    {
        var summary = output.Summary;
        c.Job.Status = JobStatus.Completed;
        c.Job.Stage = null;
        c.Job.CompletedAt = DateTime.UtcNow;
        c.Job.ExpiresAt = c.Job.CompletedAt.Value.AddHours(_engine.ArtifactTtlHours);
        c.Job.PdfPath = output.PdfPath;
        c.Job.JsonPath = output.JsonPath;
        c.Job.Score = summary.Score;
        c.Job.Grade = summary.Grade;
        c.Job.CanonVersion = summary.CanonVersion;
        c.Job.ViolationCount = summary.ViolationCount;
        c.Job.HardLocksOpen = summary.HardLocksOpen;
        await c.Store.UpdateAsync(c.Job, ct).ConfigureAwait(false);
        _logger.LogInformation("job completed: grade={Grade} score={Score} violations={V} hard_locks_open={H}",
            summary.Grade, summary.Score, summary.ViolationCount, summary.HardLocksOpen);
    }

    private sealed record EngineRunOutput(string JsonPath, string PdfPath, ReportSummary Summary);

    /// <summary>
    /// Maps an in-process <see cref="TargetResolutionException"/> to a
    /// <see cref="JobErrorCode"/>. The second tuple element is true when this
    /// is the legacy "no .sln, no lintty.yml, not exactly one .csproj" case
    /// that should still surface as <see cref="JobErrorCode.NoSln"/> for URL
    /// stability — see ADR 0006 §8.2.
    /// </summary>
    private static (string Code, bool IsLegacyNoSln) MapResolverError(
        TargetResolutionException ex, string repoDir)
    {
        return ex.ErrorCode switch
        {
            TargetResolutionErrorCode.NoTarget => IsLegacyNoSlnCase(repoDir)
                ? (JobErrorCode.NoTarget, true)
                : (JobErrorCode.NoTarget, false),
            TargetResolutionErrorCode.TargetNotFound => (JobErrorCode.TargetNotFound, false),
            TargetResolutionErrorCode.AmbiguousTargetMultipleSlns => (JobErrorCode.AmbiguousTarget, false),
            TargetResolutionErrorCode.AmbiguousTargetMultipleCsprojs => (JobErrorCode.AmbiguousTarget, false),
            TargetResolutionErrorCode.AmbiguousTargetSlnAndProjects => (JobErrorCode.AmbiguousTarget, false),
            TargetResolutionErrorCode.InvalidProjectsEntry => (JobErrorCode.InvalidConfig, false),
            _ => (JobErrorCode.InternalError, false),
        };
    }

    /// <summary>
    /// Detects the historical "no .sln in clone, no lintty.yml, no lone
    /// .csproj" condition. ADR 0006 §8.2 keeps emitting <c>no_sln</c> in this
    /// exact case so URLs documented for the early pilots stay valid.
    /// </summary>
    private static bool IsLegacyNoSlnCase(string repoDir)
    {
        try
        {
            if (!Directory.Exists(repoDir)) return false;
            var hasYaml = File.Exists(Path.Combine(repoDir, "lintty.yml"));
            if (hasYaml) return false;
            var slnCount = Directory.EnumerateFiles(repoDir, "*.sln", SearchOption.TopDirectoryOnly).Count();
            if (slnCount > 0) return false;
            var csprojCount = Directory
                .EnumerateFiles(repoDir, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !ContainsObjOrBin(p))
                .Count();
            return csprojCount != 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsObjOrBin(string path)
    {
        var n = path.Replace('\\', '/');
        return n.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || n.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassifyEngineError(string stderr)
    {
        if (string.IsNullOrEmpty(stderr)) return JobErrorCode.InternalError;
        var s = stderr.ToLowerInvariant();
        // ADR 0006 §8.1: the engine stderr now carries the resolver error
        // codes verbatim when the CLI itself runs the resolver. The worker
        // already runs the resolver in-process before the subprocess, so most
        // of these are belt-and-suspenders, but we map them anyway in case
        // a future code path reaches the CLI without pre-resolution.
        if (s.Contains("ambiguous_target")) return JobErrorCode.AmbiguousTarget;
        if (s.Contains("target_not_found")) return JobErrorCode.TargetNotFound;
        if (s.Contains("invalid_projects_entry")) return JobErrorCode.InvalidConfig;
        if (s.Contains("no_target")) return JobErrorCode.NoTarget;
        if (s.Contains("layertaggingerror") || s.Contains("layer tagging")) return JobErrorCode.LayerTaggingError;
        if (s.Contains("compile") || s.Contains("dotnet build") || s.Contains("msbuild")) return JobErrorCode.CompileFailed;
        return JobErrorCode.InternalError;
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var idx = s.IndexOf('\n');
        return (idx >= 0 ? s[..idx] : s).Trim();
    }

    private async Task FailAsync(IJobStore store, Job job, string errorCode, string errorMessage, CancellationToken ct)
    {
        job.Status = JobStatus.Failed;
        job.Stage = null;
        job.CompletedAt = DateTime.UtcNow;
        job.ExpiresAt = job.CompletedAt.Value.AddHours(_engine.ArtifactTtlHours);
        job.ErrorCode = errorCode;
        job.ErrorMessage = errorMessage;
        await store.UpdateAsync(job, ct).ConfigureAwait(false);
        _logger.LogWarning("job failed: {Code} {Msg}", errorCode, errorMessage);
    }

    private string ResolveArtifactsDir(string jobId)
    {
        var root = Path.IsPathRooted(_storage.Root)
            ? _storage.Root
            : Path.Combine(Directory.GetCurrentDirectory(), _storage.Root);
        return Path.Combine(root, _storage.JobsDir, jobId);
    }

    private static void PurgeQuietly(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort — see spec §6 LGPD: log but never throw on cleanup */ }
    }

    private static string Sha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ReportSummary ParseReportSummary(byte[] jsonBytes)
    {
        // Schema is locked at 1.0 (CLAUDE.md "Things to leave alone"). We read
        // only the fields we need with JsonDocument and tolerate missing ones.
        using var doc = JsonDocument.Parse(jsonBytes);
        var root = doc.RootElement;
        var score = TryGetInt(root, "score") ?? 0;
        var grade = TryGetString(root, "grade") ?? "F";
        var canon = TryGetString(root, "canon_version") ?? "1.0.0";
        var violations = root.TryGetProperty("violations", out var vEl) && vEl.ValueKind == JsonValueKind.Array
            ? vEl.GetArrayLength()
            : 0;

        // hard_locks_open = count of DISTINCT hard-lock rules tripped, not the
        // number of violation instances. The engine canonicalizes that into
        // hard_locks_hit (an array of rule_ids); we just take its length to
        // stay aligned with the canon.
        var hardLocksOpen = 0;
        if (root.TryGetProperty("hard_locks_hit", out var hlEl) && hlEl.ValueKind == JsonValueKind.Array)
            hardLocksOpen = hlEl.GetArrayLength();

        return new ReportSummary(score, grade, canon, violations, hardLocksOpen);
    }

    private static int? TryGetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static string? TryGetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private sealed record ReportSummary(int Score, string Grade, string CanonVersion, int ViolationCount, int HardLocksOpen);
}
