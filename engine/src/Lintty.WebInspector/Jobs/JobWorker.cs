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
using Lintty.WebInspector.Artifacts;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Configuration;
using Lintty.WebInspector.Persistence;
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
                // ADR 0007 §3.4: drain BOTH queues per poll cycle —
                //   1. V0 anonymous jobs (jobs table, Dapper, IJobStore)
                //   2. org-bound scans (scans table, Dapper, IScanQueue)
                // Order is fixed (jobs first) for two reasons: (a) the V0
                // contract is the regression baseline so we never want a
                // dashboard scan to delay a paying-anon flow, (b) deterministic
                // ordering makes integration tests stable. Each iteration of
                // the loop processes at most ONE work item across both queues
                // so a long org-bound scan never starves the anon path the way
                // a single combined claim would.
                var workDone = false;
                using (var scope = _scopes.CreateScope())
                {
                    var jobStore = scope.ServiceProvider.GetRequiredService<IJobStore>();
                    var job = await jobStore.ClaimNextQueuedAsync(stoppingToken).ConfigureAwait(false);
                    if (job is not null)
                    {
                        await RunJobAsync(scope.ServiceProvider, job, stoppingToken).ConfigureAwait(false);
                        workDone = true;
                    }
                }
                if (!workDone)
                {
                    using var scope = _scopes.CreateScope();
                    var scanQueue = scope.ServiceProvider.GetRequiredService<IScanQueue>();
                    var claimed = await scanQueue.ClaimNextQueuedAsync(stoppingToken).ConfigureAwait(false);
                    if (claimed is not null)
                    {
                        await RunScanAsync(scope.ServiceProvider, claimed, stoppingToken).ConfigureAwait(false);
                        workDone = true;
                    }
                }

                if (!workDone)
                {
                    await Task.Delay(pollDelay, stoppingToken).ConfigureAwait(false);
                }
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

    // ─────────────────────────────────────────────────────────────────────
    // ADR 0007 Sprint 3 PR 4 — org-bound scan path
    //
    // Mirrors the V0 RunJobAsync pipeline (clone → resolve → engine →
    // mark) but drives the scans table via IScanQueue (Dapper) and writes
    // artifacts via IArtifactStore (path reserved BEFORE the engine call,
    // so the CLI writes directly to the canonical location — no copy step
    // that could break byte equality with the local CLI).
    //
    // V0 path (RunJobAsync above) is intentionally untouched: the V0
    // regression gate (WorkerIntegrationTests.{Saint,Sinner,SaintNoSln,
    // Foreigner}_*) compares the dashboard's PDF byte-for-byte to a CLI
    // direct invocation; any code change in that path risks breaking the
    // hash. Code duplication here is the right call until Sprint 5
    // unifies the two flows on top of IArtifactStore + IScanQueue.
    // ─────────────────────────────────────────────────────────────────────

    private async Task RunScanAsync(IServiceProvider services, ClaimedScan scan, CancellationToken ct)
    {
        using var _ = _logger.BeginScope(new System.Collections.Generic.Dictionary<string, object>
        {
            ["scan_public_id"] = scan.PublicId,
            ["scan_id"] = scan.ScanId,
            ["org_id"] = scan.OrgId,
        });

        var queue = services.GetRequiredService<IScanQueue>();
        var git = services.GetRequiredService<IGitClient>();
        var engine = services.GetRequiredService<IEngineRunner>();
        var artifacts = services.GetRequiredService<IArtifactStore>();
        var tokenStore = services.GetRequiredService<IGitHubUserTokenStore>();

        var cloneRoot = Path.Combine(Path.GetTempPath(), $"lintty-scan-{scan.PublicId:N}");
        var repoDir = Path.Combine(cloneRoot, "repo");
        Directory.CreateDirectory(cloneRoot);

        try
        {
            // ── 1. Clone ────────────────────────────────────────────────
            var coordsParse = UrlValidator.TryParse(scan.GithubUrl, out var parseError);
            if (coordsParse is null)
            {
                await queue.MarkFailedAsync(scan.ScanId,
                    SanitizeError(parseError ?? "URL parse failed"), ct).ConfigureAwait(false);
                return;
            }

            // ── 1a. Apêndice E §E.9 — fetch user token for private repos.
            // Public repos pass token=null (V0 anonymous clone). Private
            // repos look up the OAuth token of the user who added the repo
            // (repos.added_by_user_id). Token missing/revoked → fail fast
            // with GITHUB_TOKEN_REVOKED so the frontend can prompt the user
            // to re-connect GitHub.
            //
            // §E.9 #1 invariant: clone uses added_by_user_id, not
            // triggered_by_user_id. A second member of the same Lintty-org
            // who triggers a scan inherits the token of whoever first
            // imported the repo. If that user revoked, the scan fails —
            // the org owner has to readd the repo (no automatic fallback
            // by design, per §E.9 #1).
            string? cloneToken = null;
            if (scan.IsPrivate)
            {
                cloneToken = await tokenStore.GetActiveTokenAsync(scan.AddedByUserId, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(cloneToken))
                {
                    var msg = $"{JobErrorCode.GithubTokenRevoked}: o usuário que adicionou este repositório (id={scan.AddedByUserId.ToString(CultureInfo.InvariantCulture)}) não tem token GitHub ativo. Peça para reconectar em /api/auth/github/connect/start.";
                    _logger.LogWarning(
                        "Private clone aborted: token revoked for added_by_user_id={UserId}, scan_public_id={PublicId}",
                        scan.AddedByUserId, scan.PublicId);
                    await queue.MarkFailedAsync(scan.ScanId, msg, ct).ConfigureAwait(false);
                    return;
                }
            }

            // Ref preference: explicit override on the scan row → repo's
            // default branch (snapshotted at claim time) → null (let
            // git clone --depth 1 resolve HEAD itself, same as V0).
            var gitRef = !string.IsNullOrEmpty(scan.Ref)
                ? scan.Ref
                : (string.IsNullOrEmpty(scan.DefaultBranch) ? null : scan.DefaultBranch);
            var cloneResult = await git.CloneAsync(coordsParse, gitRef, cloneToken, repoDir, ct).ConfigureAwait(false);
            if (!cloneResult.Success)
            {
                await queue.MarkFailedAsync(scan.ScanId,
                    SanitizeError(cloneResult.ErrorMessage ?? "clone failed"), ct).ConfigureAwait(false);
                return;
            }

            // ── 2. Resolve target via in-process resolver (ADR 0006) ────
            // Sprint 3 PR S1 (restored in PR S3): honor user-curated
            // repos.scan_projects when the column is non-empty. Validation
            // happened at PUT time (IRepoPreflightService.SetScanProjectsAsync);
            // the worker trusts the column blindly EXCEPT for the path-
            // traversal re-check below, because the column was written
            // before the current sandbox existed and a malicious operator
            // with DB access shouldn't be able to escape via an absolute
            // path. Multi-csproj selections are merged into a transient
            // .lintty-runtime.yml here so the engine produces a single
            // combined PDF — one trigger, one job, one PDF.
            string targetPath;
            try
            {
                var targetArg = await ResolveScanTargetArgAsync(scan.ScanProjects, repoDir, ct).ConfigureAwait(false);
                var resolved = TargetResolver.Resolve(
                    targetArg: targetArg,
                    cwd: repoDir,
                    mode: TargetResolverMode.WebInspector);
                targetPath = resolved.SolutionPathForReporting;
            }
            catch (TargetResolutionException trex)
            {
                await queue.MarkFailedAsync(scan.ScanId,
                    SanitizeError(trex.Message), ct).ConfigureAwait(false);
                return;
            }

            // ── 3. Reserve artifact paths BEFORE invoking the engine ───
            // The CLI writes directly to these paths (--pdf, --output-file).
            // No copy/move step → no chance of byte drift on the artifact.
            var pdfPath = await artifacts.ReserveAsync(scan.PublicId, ArtifactKind.LaudoPdf, ct).ConfigureAwait(false);
            var jsonPath = await artifacts.ReserveAsync(scan.PublicId, ArtifactKind.ReportJson, ct).ConfigureAwait(false);

            // ── 4. Invoke the engine with the snapshotted canon ─────────
            // §3.7 invariant: --canon-version is ALWAYS passed for org-bound
            // scans, even when it equals the engine's default. The dashboard's
            // determinism contract requires the canon to be explicit on every
            // org-bound invocation so a future canon bump doesn't silently
            // re-grade a queued scan.
            var run = await engine.RunAsync(targetPath, jsonPath, pdfPath, scan.CanonVersion, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(run.Stderr))
                _logger.LogInformation("engine stderr:\n{Stderr}", run.Stderr.TrimEnd());

            if (run.ExitCode != 0 && run.ExitCode != 1)
            {
                // 0 = grade better than --fail-on-grade, 1 = grade equal-or-worse.
                // Both mean "engine produced JSON+PDF cleanly". 2 = exec error,
                // 127 = our timeout sentinel, anything else is unexpected.
                var msg = run.ExitCode switch
                {
                    127 => $"engine timeout: {FirstLine(run.Stderr)}",
                    2 => $"engine error: {FirstLine(run.Stderr)}",
                    _ => $"engine returned exit code {run.ExitCode.ToString(CultureInfo.InvariantCulture)}: {FirstLine(run.Stderr)}",
                };
                await queue.MarkFailedAsync(scan.ScanId, SanitizeError(msg), ct).ConfigureAwait(false);
                return;
            }

            if (!File.Exists(jsonPath) || !File.Exists(pdfPath))
            {
                await queue.MarkFailedAsync(scan.ScanId,
                    "engine completed but artifacts are missing.", ct).ConfigureAwait(false);
                return;
            }

            // ── 5. Mark completed; persist hash_content ─────────────────
            // hash_content is the SHA-256 of the JSON bytes written by the
            // engine — same value the engine embeds in the PDF footer (ADR
            // 0003 §5.2) and the cross-determinism witness in §3.7.
            var jsonBytes = await File.ReadAllBytesAsync(jsonPath, ct).ConfigureAwait(false);
            var hashContent = "sha256:" + Sha256(jsonBytes);
            await queue.MarkCompletedAsync(scan.ScanId, hashContent, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "scan completed: public_id={PublicId} canon={Canon} hash={Hash}",
                scan.PublicId, scan.CanonVersion, hashContent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown mid-scan; leave the row in 'running' for now —
            // PR 5 / Sprint 5 handles stuck-running cleanup.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while running scan");
            try
            {
                await queue.MarkFailedAsync(scan.ScanId, SanitizeError(ex.Message), ct).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }
        finally
        {
            // Always purge the clone scratch space — same LGPD invariant
            // as the V0 path. ScanId-based path so concurrent V0 jobs and
            // org-bound scans never collide.
            PurgeQuietly(cloneRoot);
            _logger.LogInformation(
                "scan clone purged at {Time}",
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Sprint 3 PR S1 (restored in PR S3) — turn the snapshotted
    /// <c>repos.scan_projects</c> list into a <c>targetArg</c> for
    /// <see cref="TargetResolver"/>.
    /// <list type="bullet">
    ///   <item><description><c>null</c> / empty → return <c>null</c>;
    ///         resolver runs auto-detect (current behaviour).</description></item>
    ///   <item><description>1 entry → resolve to an absolute path inside
    ///         <paramref name="repoDir"/>. Anti-traversal: the absolute path
    ///         must remain rooted in <paramref name="repoDir"/>; otherwise
    ///         we throw a <see cref="TargetResolutionException"/> with the
    ///         <c>InvalidProjectsEntry</c> code so the existing failure
    ///         path persists the right structured error.</description></item>
    ///   <item><description>2+ entries (assumed all <c>.csproj</c> by the
    ///         time they hit the column — the PUT endpoint validates the
    ///         combination) → write a transient <c>.lintty-runtime.yml</c>
    ///         in <paramref name="repoDir"/> declaring the project list,
    ///         then return that yaml path. The yaml is ephemeral; the
    ///         sandbox-purge in the <c>finally</c> block of
    ///         <see cref="RunScanAsync"/> wipes it along with the rest of
    ///         the clone. The engine aggregates the listed projects into
    ///         a single combined PDF.</description></item>
    /// </list>
    /// </summary>
    private static async Task<string?> ResolveScanTargetArgAsync(
        string[]? scanProjects,
        string repoDir,
        CancellationToken ct)
    {
        if (scanProjects is null || scanProjects.Length == 0) return null;

        var repoDirAbs = Path.GetFullPath(repoDir);
        var sep = Path.DirectorySeparatorChar;
        var repoDirAbsWithSep = repoDirAbs.TrimEnd(sep) + sep;

        if (scanProjects.Length == 1)
        {
            var single = scanProjects[0];
            var combined = Path.GetFullPath(Path.Combine(repoDirAbs, single));
            if (!(combined + sep).StartsWith(repoDirAbsWithSep, StringComparison.OrdinalIgnoreCase))
            {
                throw new TargetResolutionException(
                    TargetResolutionErrorCode.InvalidProjectsEntry,
                    $"scan_projects path '{single}' escapes the repo root.");
            }
            return combined;
        }

        // 2+ entries — write a throwaway yaml. The PUT endpoint already
        // refused mixed sln+csproj combos, so by the time we get here the
        // list is all-.csproj. Defensive validation: yaml writer just
        // forwards whatever paths it got, but each must stay inside the
        // repo (anti-traversal) and use forward slashes (the resolver's
        // yaml parser is path-separator agnostic but the file content
        // should be canonical POSIX for diffability).
        var validated = new System.Collections.Generic.List<string>(scanProjects.Length);
        foreach (var entry in scanProjects)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var combined = Path.GetFullPath(Path.Combine(repoDirAbs, entry));
            if (!(combined + sep).StartsWith(repoDirAbsWithSep, StringComparison.OrdinalIgnoreCase))
            {
                throw new TargetResolutionException(
                    TargetResolutionErrorCode.InvalidProjectsEntry,
                    $"scan_projects path '{entry}' escapes the repo root.");
            }
            validated.Add(entry.Replace('\\', '/'));
        }

        // Write the runtime yaml at <repoDir>/lintty.yml. We overwrite any
        // user-committed lintty.yml in the sandbox — the clone is a
        // throwaway copy so this never touches the user's repo on disk.
        // The TargetResolver checks the filename literally for "lintty.yml"
        // (Core/Workspace/TargetResolver.cs:74), so this is the only name
        // it accepts via --target. The original ".lintty-runtime.yml"
        // sentinel from PR S1 was never reachable because of that check.
        var yamlPath = Path.Combine(repoDirAbs, "lintty.yml");
        var sb = new System.Text.StringBuilder(64 + 32 * validated.Count);
        sb.AppendLine("# Generated by Lintty Web Inspector. Lifetime: this scan only.");
        sb.AppendLine("# Discarded with the rest of the sandbox after the worker finishes.");
        sb.AppendLine("canon_version: 1.0.0");
        sb.AppendLine("projects:");
        foreach (var p in validated)
        {
            // YAML safe — paths are repo-relative ASCII. Single-quote
            // anyway in case a path ever contains a colon (Windows drive
            // letter scenarios are blocked by the traversal check above
            // but defense in depth is cheap).
            sb.Append("  - '").Append(p.Replace("'", "''", StringComparison.Ordinal)).AppendLine("'");
        }
        await File.WriteAllTextAsync(yamlPath, sb.ToString(), ct).ConfigureAwait(false);
        return yamlPath;
    }

    /// <summary>
    /// Strips PII / secrets that might accidentally land in error messages.
    /// V0 only handles public clones (no token), so this is mostly belt-and-
    /// suspenders for git CLI output that occasionally embeds the URL with
    /// credentials encoded — we drop anything that looks like a userinfo
    /// portion (<c>scheme://user:pwd@host</c>).
    /// </summary>
    private static string SanitizeError(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        // Cap length so a multi-MB stack trace doesn't end up in the DB.
        var trimmed = raw.Length > 1024 ? raw[..1024] + "…" : raw;
        // Naive userinfo strip — enough for git/HTTP. Doesn't try to be
        // exhaustive; production secret scanning is the security team's
        // domain (see docs/futuro/).
        return System.Text.RegularExpressions.Regex.Replace(
            trimmed,
            @"(?<scheme>https?://)[^@\s/]+(:[^@\s/]+)?@",
            "${scheme}",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(50));
    }
}
