using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Canon;
using Lintty.WebInspector.Github;
using Lintty.WebInspector.Jobs;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;
using Lintty.WebInspector.Scans;
using Lintty.WebInspector.Tests.Fakes;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// Full-pipeline integration test: enqueue a job, the worker (a) "clones"
/// the local fixture via <see cref="FixtureCopyGitClient"/>, (b) shells out
/// to the real engine CLI dll, (c) reads the JSON, (d) writes the artifacts.
///
/// The cross-determinism assertion is the heart of the test: the PDF the
/// Web Inspector generates must be byte-identical to the PDF the local CLI
/// generates against the same .sln on the same engine version. That is the
/// product invariant from <c>13-web-inspector.md</c> §10. Storage backend
/// changing from SQLite to Postgres (ADR 0007 Sprint 1) does not change
/// anything that flows into the PDF, so this gate must remain green
/// across the cut.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WorkerIntegrationTests : WebInspectorTestBase
{
    public WorkerIntegrationTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task Saint_Runs_End_To_End_And_Pdf_Matches_Cli_Direct_Invocation()
    {
        await ResetAsync();
        await RunFixtureAsync(
            fixtureDir: TestPaths.SaintFixture,
            slnFileName: "Saint.sln",
            expectedGrade: "A",
            expectedScore: 100,
            expectedHardLocks: 0);
    }

    [Fact]
    public async Task Sinner_Runs_End_To_End_With_F_Grade_And_Three_Hard_Locks()
    {
        await ResetAsync();
        await RunFixtureAsync(
            fixtureDir: TestPaths.SinnerFixture,
            slnFileName: "Sinner.sln",
            expectedGrade: "F",
            expectedScore: 0,
            expectedHardLocks: 3);
    }

    /// <summary>
    /// ADR 0006 §9.4 cross-determinism gate for the lintty.yml-driven path:
    /// the Web Inspector worker invokes the engine via the resolver. The PDF
    /// must be byte-identical to the one produced by the local CLI run with
    /// <c>--target lintty.yml</c>.
    /// </summary>
    [Fact]
    public async Task SaintNoSln_Runs_End_To_End_And_Pdf_Matches_Cli_Direct_Invocation()
    {
        await ResetAsync();
        await RunFixtureAsync(
            fixtureDir: TestPaths.SaintNoSlnFixture,
            slnFileName: "lintty.yml",
            expectedGrade: "A",
            expectedScore: 100,
            expectedHardLocks: 0);
    }

    /// <summary>
    /// Permissive layer-tagging cross-determinism gate (2026-05-06). The
    /// Foreigner fixture has three projects with names that match no
    /// convention pattern AND no <c>lintty.yml</c> — pre-fix, this would
    /// fail with <c>JobErrorCode.LayerTaggingError</c>; post-fix, the
    /// engine produces an A-grade laudo with an "Unknown" layer-summary
    /// row. The Web Inspector PDF must remain byte-identical to the local
    /// CLI run on the same input.
    /// </summary>
    [Fact]
    public async Task Foreigner_Runs_End_To_End_With_UnknownProjects_And_Pdf_Matches_Cli()
    {
        await ResetAsync();
        await RunFixtureAsync(
            fixtureDir: TestPaths.ForeignerFixture,
            slnFileName: "Foreigner.sln",
            expectedGrade: "A",
            expectedScore: 100,
            expectedHardLocks: 0);
    }

    [Fact]
    public async Task NoSln_NoYaml_Empty_Repo_Yields_NoTarget()
    {
        await ResetAsync();
        // Tiny scratch fixture: a directory with just a README. The worker
        // cloning this finds no .sln, no lintty.yml, no .csproj — resolver
        // surfaces no_target. Per ADR 0006 §8.2 this specific case (zero
        // .sln, zero .csproj, no lintty.yml) maps to the legacy NoSln code.
        using var scratch = TempFixture.New();
        File.WriteAllText(Path.Combine(scratch.Path, "README.md"), "empty repo\n");

        var (errorCode, status) = await RunWorkerExpectingFailureAsync(scratch.Path);
        Assert.Equal("failed", status);
        // The resolver returns no_target; the worker maps it to no_sln when
        // the legacy condition holds (see JobWorker.IsLegacyNoSlnCase).
        Assert.True(
            errorCode is JobErrorCode.NoSln or JobErrorCode.NoTarget,
            $"expected no_sln or no_target, got '{errorCode}'");
    }

    [Fact]
    public async Task SingleCsproj_NoSln_Resolves()
    {
        await ResetAsync();
        // ADR 0006 §5.4: single-csproj implicit fallback in WebInspector mode.
        // We mirror Saint.Domain (no project references, compiles cleanly on
        // its own) so the engine produces a real grade. The worker should
        // complete the job with a valid grade.
        using var scratch = TempFixture.New();
        var domainDir = Path.Combine(TestPaths.SaintFixture, "src", "Saint.Domain");
        var destDir = Path.Combine(scratch.Path, "MyLib");
        CopyTree(domainDir, destDir);
        // Rename csproj so the file's basename is unique. Keep extension.

        await using var factory = CreateFactory();
        factory.DisableWorker = false;
        factory.GitClientOverride = new Fakes.FixtureCopyGitClient(scratch.Path);
        var client = factory.CreateClient();

        var post = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/lintty-demo/test-single-csproj",
        });
        post.EnsureSuccessStatusCode();
        using var postDoc = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        var jobId = postDoc.RootElement.GetProperty("job_id").GetString()!;

        var deadline = DateTime.UtcNow.AddMinutes(5);
        string? finalStatus = null;
        JsonDocument? finalDoc = null;
        while (DateTime.UtcNow < deadline)
        {
            var poll = await client.GetAsync($"/api/jobs/{jobId}");
            poll.EnsureSuccessStatusCode();
            finalDoc?.Dispose();
            finalDoc = JsonDocument.Parse(await poll.Content.ReadAsStringAsync());
            finalStatus = finalDoc.RootElement.GetProperty("status").GetString();
            if (finalStatus is "completed" or "failed") break;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.NotNull(finalDoc);
        Assert.Equal("completed", finalStatus);
    }

    [Fact]
    public async Task MultipleCsprojs_NoSln_Yields_AmbiguousTarget()
    {
        await ResetAsync();
        // Two csproj, no .sln, no lintty.yml -> ambiguous_target.
        using var scratch = TempFixture.New();
        WriteFakeCsproj(Path.Combine(scratch.Path, "src", "A"), "A");
        WriteFakeCsproj(Path.Combine(scratch.Path, "src", "B"), "B");

        var (errorCode, status) = await RunWorkerExpectingFailureAsync(scratch.Path);
        Assert.Equal("failed", status);
        Assert.Equal(JobErrorCode.AmbiguousTarget, errorCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADR 0007 Sprint 3 / PR 5 — dashboard-bound integration suite.
    //
    // The V0 tests above (Saint_*, Sinner_*, SaintNoSln_*, Foreigner_*) prove
    // the worker's V0 anonymous path is byte-identical to a CLI direct call.
    // The two tests below prove the same invariant for the org-bound dashboard
    // path (POST /api/scans → JobWorker.RunScanAsync → IArtifactStore) and the
    // canon-snapshot regression from §3.7.
    //
    // Why same file. The cross-determinism gate is one product invariant with
    // two callers (anon /api/jobs and authed /api/scans) — keeping all four
    // assertions adjacent makes regressions easy to triage. Drift in one will
    // surface against the other in the same `dotnet test` run.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ADR 0007 §3.7 cross-determinism gate for the org-bound flow:
    /// <c>POST /api/scans</c> against the Saint fixture must produce a
    /// <c>laudo.pdf</c> byte-identical to <c>lintty-engine analyze</c>
    /// run locally on the same .sln. Same engine, same canon, same PDF
    /// — the entire dashboard pitch.
    ///
    /// <para>
    /// <b>Why this is the gate of Sprint 3.</b> The V0 anonymous flow
    /// already passes this gate (<see cref="Saint_Runs_End_To_End_And_Pdf_Matches_Cli_Direct_Invocation"/>);
    /// the org-bound flow could in principle diverge if anything in the
    /// new code path (canon snapshot, IArtifactStore reservation, the
    /// scans Dapper queue) accidentally changed what the engine sees. If
    /// this test ever goes red, do NOT relax the assertion — investigate
    /// whether a timestamp, request id, host metadata, or font fallback
    /// crept into the path.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DashboardScan_Saint_Pdf_Equals_CliDirect()
    {
        await ResetAsync();
        Assert.True(Directory.Exists(TestPaths.SaintFixture),
            $"Saint fixture missing: {TestPaths.SaintFixture}");
        Assert.True(File.Exists(TestPaths.EngineCliDll),
            $"Engine CLI dll missing at {TestPaths.EngineCliDll} — build Lintty.Engine.Cli first.");

        // ── 1. Spin up the host with the worker enabled and the git client
        //      pointed at the local Saint fixture. The default canon provider
        //      (DefaultCanonVersionProvider, "1.0.0") matches Saint/lintty.yml,
        //      so the snapshotted canon and the yml-derived canon agree —
        //      cross-determinism does not require an explicit override here.
        await using var factory = CreateFactory();
        factory.DisableWorker = false;
        factory.GitClientOverride = new FixtureCopyGitClient(TestPaths.SaintFixture);
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "scan-determinism@example.com", "Determinism Co");

        // ── 2. Register the repo and trigger a scan via the service. We use
        //      the service (not the HTTP endpoint) so the test focuses on the
        //      worker → engine path; the endpoint contract is exercised in
        //      ScansEndpointsTests.Post_Returns_201_And_Persists_Queued_*.
        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/lintty-demo/the-saint",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgId, userId, repoId, gitRef: null, CancellationToken.None);
            Assert.Equal(TriggerScanOutcome.Created, trigger.Outcome);
            publicId = trigger.Scan!.PublicId;
        }

        // ── 3. Wait for the worker to drain the queue. Poll the DB directly
        //      (cheaper than a cookie'd HTTP round-trip per tick, and the
        //      cookie path has its own coverage). Generous deadline because
        //      the engine CLI does a real MSBuild restore on first run.
        var completed = await WaitForScanStatusAsync(
            factory, publicId, ScanStatus.Completed, TimeSpan.FromMinutes(3));
        Assert.NotNull(completed);
        Assert.Equal(ScanStatus.Completed, completed!.Status);
        Assert.False(string.IsNullOrEmpty(completed.HashContent),
            "completed scan should have hash_content filled by the worker");

        // ── 4. Pull the dashboard PDF via the authed endpoint — exercises
        //      the same code path a real client would. Same bytes the worker
        //      asked the engine to write into IArtifactStore.
        var pdfResp = await client.GetAsync($"/api/scans/{publicId:D}/laudo.pdf");
        pdfResp.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", pdfResp.Content.Headers.ContentType?.MediaType);
        var dashboardPdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
        var dashboardPdfHash = Sha256Bytes(dashboardPdfBytes);

        // Pull the JSON too — its sha256 is what hash_content stores. We use
        // it as a sanity cross-check; the PDF byte equality is the headline.
        var jsonResp = await client.GetAsync($"/api/scans/{publicId:D}/report.json");
        jsonResp.EnsureSuccessStatusCode();
        var dashboardJsonBytes = await jsonResp.Content.ReadAsByteArrayAsync();
        var dashboardJsonHash = Sha256Bytes(dashboardJsonBytes);
        Assert.Equal($"sha256:{dashboardJsonHash}", completed.HashContent);

        // ── 5. Run the SAME CLI dll directly against the SAME .sln. We pass
        //      `--canon-version 1.0.0` here to mirror the dashboard path
        //      verbatim — the worker always passes it for org-bound scans
        //      (EngineSubprocessRunner §3.7), so CLI direct must too for
        //      the comparison to be apples-to-apples.
        var directDir = Path.Combine(Path.GetTempPath(), $"lintty-direct-dashboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directDir);
        try
        {
            var slnPath = Path.Combine(TestPaths.SaintFixture, "Saint.sln");
            var directPdf = Path.Combine(directDir, "laudo.pdf");
            var directJson = Path.Combine(directDir, "report.json");
            var (exitCode, stderr) = RunEngineDirect(
                slnPath, directPdf, directJson, canonVersion: "1.0.0");
            Assert.True(exitCode is 0 or 1,
                $"Direct CLI invocation failed with exit {exitCode}: {stderr}");
            Assert.True(File.Exists(directPdf), "Direct CLI did not produce a PDF.");

            var directPdfBytes = await File.ReadAllBytesAsync(directPdf);
            var directPdfHash = Sha256Bytes(directPdfBytes);
            var directJsonBytes = await File.ReadAllBytesAsync(directJson);
            var directJsonHash = Sha256Bytes(directJsonBytes);

            // ── 6. The gate. If this assert fails: read CLAUDE.md
            //      "Determinism is a product invariant" and trace what
            //      drifted. Do NOT relax the assertion.
            Assert.True(
                dashboardJsonHash == directJsonHash,
                "Cross-determinism gate FAILED: report.json hash differs between dashboard and CLI direct on identical input.\n" +
                $"  dashboard: {dashboardJsonHash}\n" +
                $"  cli-direct: {directJsonHash}");
            Assert.True(
                dashboardPdfHash == directPdfHash,
                "Cross-determinism gate FAILED (dashboard PDF differs from CLI direct on identical input).\n" +
                $"  dashboard: {dashboardPdfHash}\n" +
                $"  cli-direct: {directPdfHash}\n" +
                $"  dashboard size: {dashboardPdfBytes.Length} B\n" +
                $"  cli-direct size: {directPdfBytes.Length} B");
        }
        finally
        {
            try { Directory.Delete(directDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// ADR 0007 §3.7 canon-snapshot invariant: a scan queued today against
    /// canon <c>1.0.0</c> must run with <c>1.0.0</c> even if the host's
    /// "current canon" advances before the worker claims it. Otherwise a
    /// canon bump silently re-grades queued scans, which kills determinism.
    ///
    /// <para>
    /// <b>How this test simulates the bump.</b> A
    /// <see cref="Fakes.ScriptedCanonVersionProvider"/> replaces the default
    /// singleton. The test sets it to <c>1.0.0</c>, triggers the scan
    /// (snapshotted onto the row), then flips the provider to <c>1.0.1</c>
    /// before the worker picks the row up. The post-bump value never
    /// reaches the engine because <see cref="ScanService.TriggerAsync"/>
    /// reads the provider exactly once — at trigger time — and the worker
    /// reads the snapshotted column from <c>scans</c>, not the provider.
    /// </para>
    ///
    /// <para>
    /// <b>What we assert.</b>
    /// <list type="number">
    ///   <item><description>The scan row's <c>canon_version</c> stays
    ///         <c>1.0.0</c> after the worker completes.</description></item>
    ///   <item><description>The engine subprocess was invoked with
    ///         <c>--canon-version 1.0.0</c>, captured via
    ///         <see cref="Fakes.RecordingEngineRunner"/>.</description></item>
    ///   <item><description>The generated <c>report.json</c> carries
    ///         <c>canon_version: "1.0.0"</c> — the engine itself honored
    ///         the snapshot, not the post-bump value.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    [Fact]
    public async Task DashboardScan_PinnedCanon_Survives_NewCanon()
    {
        await ResetAsync();
        Assert.True(Directory.Exists(TestPaths.SaintFixture),
            $"Saint fixture missing: {TestPaths.SaintFixture}");

        var scriptedCanon = new ScriptedCanonVersionProvider { Current = "1.0.0" };
        // Out-parameter via a TaskCompletionSource-style holder. The decorator
        // factory runs once during DI graph construction (the IEngineRunner is
        // a singleton); we capture the constructed wrapper so the assertions
        // below can read its Invocations.
        RecordingEngineRunner? recordingRef = null;

        await using var factory = CreateFactory();
        factory.DisableWorker = false;
        factory.GitClientOverride = new FixtureCopyGitClient(TestPaths.SaintFixture);
        factory.CanonVersionProviderOverride = scriptedCanon;
        factory.EngineRunnerDecorator = inner =>
        {
            var wrapper = new RecordingEngineRunner(inner);
            recordingRef = wrapper;
            return wrapper;
        };

        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "canon-snapshot@example.com", "Snapshot Co");

        // Add a repo. FakeGitHubMetadataClient defaults are used — the URL
        // never gets resolved against real GitHub.
        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/lintty-demo/the-saint",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        // ── Trigger with provider returning 1.0.0; the row snapshots it.
        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(
                orgId, userId, repoId, gitRef: null, CancellationToken.None);
            Assert.Equal(TriggerScanOutcome.Created, trigger.Outcome);
            publicId = trigger.Scan!.PublicId;
            // Confirm the snapshot at trigger time is exactly what we set.
            Assert.Equal("1.0.0", trigger.Scan!.CanonVersion);
        }

        // ── Simulate "the canon advanced". From this moment on, ANY new
        //    trigger would snapshot 1.0.1 — but our row is already locked
        //    to 1.0.0. The worker may or may not have picked the scan up
        //    yet (poll interval is 100ms in tests); the flip is safe
        //    either way because the worker reads from the row, not the
        //    provider.
        scriptedCanon.Current = "1.0.1";

        // ── Wait for the worker to complete. Generous deadline — first
        //    MSBuild restore is slow.
        var completed = await WaitForScanStatusAsync(
            factory, publicId, ScanStatus.Completed, TimeSpan.FromMinutes(3));
        Assert.NotNull(completed);

        // ── Assertion 1: the row's canon_version is still 1.0.0 even
        //    though the provider is now 1.0.1.
        Assert.Equal("1.0.0", completed!.CanonVersion);

        // ── Assertion 2: the engine was invoked with --canon-version 1.0.0.
        Assert.NotNull(recordingRef);
        var invocations = recordingRef!.Invocations;
        Assert.NotEmpty(invocations);
        // The most recent invocation belongs to our scan (the only one in
        // this test). The org-bound path always passes a non-null canon —
        // §3.7 — so we assert exactness, not "either null or 1.0.0".
        var lastInvocation = invocations[^1];
        Assert.Equal("1.0.0", lastInvocation.CanonVersion);

        // ── Assertion 3: the report.json the engine produced carries
        //    canon_version: "1.0.0" — the snapshot reached all the way
        //    down. Pull via the authed endpoint same as a real client.
        var jsonResp = await client.GetAsync($"/api/scans/{publicId:D}/report.json");
        jsonResp.EnsureSuccessStatusCode();
        using var reportDoc = JsonDocument.Parse(await jsonResp.Content.ReadAsStringAsync());
        Assert.True(reportDoc.RootElement.TryGetProperty("canon_version", out var canonEl)
                    && canonEl.ValueKind == JsonValueKind.String,
            "report.json must contain a string canon_version field");
        Assert.Equal("1.0.0", canonEl.GetString());
    }

    /// <summary>
    /// ADR 0007 Apêndice E §E.9 #4 cross-check: private clone with a
    /// revoked token marks the scan as failed with
    /// <see cref="JobErrorCode.GithubTokenRevoked"/>. Exercises the worker's
    /// new pre-clone token lookup (PR 7) — when
    /// <c>tokenStore.GetActiveTokenAsync(scan.AddedByUserId)</c> returns
    /// <c>null</c>, the pipeline must short-circuit BEFORE invoking
    /// <c>git clone</c> (no shell-out, no transient filesystem footprint)
    /// and surface the structured error so the frontend can prompt the
    /// user to reconnect GitHub.
    ///
    /// <para>
    /// <b>Setup.</b> The test imports a private repo via
    /// <c>POST /api/repos/import</c> (the only path that sets
    /// <c>repos.is_private = true</c>; manual add can't), then revokes
    /// the token via <see cref="IGitHubUserTokenStore.RevokeAsync"/>, then
    /// triggers a scan. The worker should never reach
    /// <c>FixtureCopyGitClient.CloneAsync</c> — we use the real
    /// <c>GitCliClient</c> override-free precisely because if the clone
    /// did fire, it would error with a different message and the
    /// assertion below would surface that drift.
    /// </para>
    ///
    /// <para>
    /// <b>The assertion.</b> <c>Scan.Status == Failed</c> AND
    /// <c>Scan.Error.Contains("GITHUB_TOKEN_REVOKED")</c>. Per ADR 0007
    /// PR 1, the schema kept a single <c>error</c> column rather than
    /// splitting code/message; the worker prefixes the message with the
    /// literal code so callers / tests can grep for it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Worker_Private_Repo_With_Revoked_Token_Marks_Scan_Failed_With_GITHUB_TOKEN_REVOKED()
    {
        await ResetAsync();

        await using var factory = CreateFactory();
        factory.DisableWorker = false;
        // The git client is irrelevant — the worker should bail BEFORE the
        // clone call. Leave the default GitCliClient (which would fail
        // with a different message if it actually fired). If a future
        // refactor moves the token check to AFTER the clone, this test
        // will go red because the GitCliClient subprocess error will not
        // contain "GITHUB_TOKEN_REVOKED".

        // Stage 1: connect a fake GitHub identity + import a private repo.
        // This is the only flow that sets repos.is_private = true; manual
        // add can't.
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(91001, "acme", null),
        };
        factory.FakeGitHubOrgs.MetadataByFullName = new Dictionary<string, GitHubRepoDetails>
        {
            ["acme/private-saint"] = new(
                Id: 91101,
                Name: "private-saint",
                FullName: "acme/private-saint",
                IsPrivate: true,
                DefaultBranch: "main",
                CloneUrl: "https://github.com/acme/private-saint.git"),
        };

        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "revoked@example.com", "Revoked Co");

        // Seed an active token + populate the github_orgs cache.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IGitHubUserTokenStore>();
            await store.SaveAsync(userId, "ghs_BEFORE_REVOCATION", new[] { "repo", "read:org" }, CancellationToken.None);
        }
        (await client.GetAsync("/api/github/orgs")).EnsureSuccessStatusCode();

        // Import the private repo.
        var importResp = await client.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/private-saint",
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, importResp.StatusCode);
        using var importDoc = JsonDocument.Parse(await importResp.Content.ReadAsStringAsync());
        var repoId = importDoc.RootElement.GetProperty("id").GetInt64();
        Assert.True(importDoc.RootElement.GetProperty("isPrivate").GetBoolean(),
            "Import should have flagged this repo as private; the worker decision branches on this column.");

        // Stage 2: revoke the token. This is the §E.9 trigger condition —
        // user revoked locally (or upstream silently revoked). At this
        // point GetActiveTokenAsync returns null for this user.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IGitHubUserTokenStore>();
            var revoked = await store.RevokeAsync(userId, CancellationToken.None);
            Assert.True(revoked, "RevokeAsync should have flipped revoked_at on the seeded row.");
        }

        // Stage 3: trigger a scan. We go through the service rather than
        // the HTTP endpoint to focus on the worker path — ScansEndpointsTests
        // covers the trigger contract.
        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgId, userId, repoId, gitRef: null, CancellationToken.None);
            Assert.Equal(TriggerScanOutcome.Created, trigger.Outcome);
            publicId = trigger.Scan!.PublicId;
        }

        // Stage 4: wait for the worker to mark the scan failed. Short
        // deadline — the bail-out path is in-memory, no clone, no
        // subprocess.
        var failed = await WaitForScanStatusAsync(
            factory, publicId, ScanStatus.Failed, TimeSpan.FromSeconds(15));
        Assert.NotNull(failed);
        Assert.Equal(ScanStatus.Failed, failed!.Status);
        Assert.NotNull(failed.Error);
        Assert.Contains(JobErrorCode.GithubTokenRevoked, failed.Error!, StringComparison.Ordinal);
        // hash_content must NOT be set on a failed scan.
        Assert.Null(failed.HashContent);
    }

    /// <summary>
    /// ADR 0007 Sprint 3 / PR S1 cross-determinism gate for the
    /// <i>user-curated</i> scan-target path. The two existing gates
    /// (<see cref="Saint_Runs_End_To_End_And_Pdf_Matches_Cli_Direct_Invocation"/>
    /// and <see cref="DashboardScan_Saint_Pdf_Equals_CliDirect"/>) cover the
    /// auto-detect path; this test covers the path where the user picks
    /// a single .csproj from a multi-project repo with no .sln. The PDF the
    /// worker generates must remain byte-identical to a CLI direct call
    /// pointing <c>--target</c> at the same .csproj inside the same sandbox.
    ///
    /// <para>
    /// PR S3 model (post-S2-revert): a trigger creates exactly one scan
    /// row; the worker reads <c>repos.scan_projects</c> at claim time. With
    /// a single saved <c>.csproj</c>, the worker passes <c>--target</c>
    /// straight through to the engine — no runtime yaml synthesis needed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DashboardScan_With_Saved_ScanProjects_Pdf_Equals_CliDirect_Same_Target()
    {
        await ResetAsync();
        Assert.True(Directory.Exists(TestPaths.MultiCsprojFixture),
            $"multi-csproj fixture missing: {TestPaths.MultiCsprojFixture}");
        Assert.True(File.Exists(TestPaths.EngineCliDll),
            $"Engine CLI dll missing at {TestPaths.EngineCliDll} — build Lintty.Engine.Cli first.");

        // ── 1. Spin up the worker pointed at the multi-csproj fixture.
        await using var factory = CreateFactory();
        factory.DisableWorker = false;
        factory.GitClientOverride = new FixtureCopyGitClient(TestPaths.MultiCsprojFixture);
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "scan-curated@example.com", "Curated Co");

        // ── 2. Register a repo + persist the saved selection directly on
        //      the row (the PUT endpoint has its own coverage in
        //      RepoPreflightTests). The worker reads scan_projects at claim
        //      time and passes --target to the engine.
        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/lintty-demo/multi-csproj",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        const string targetCsprojRelative = "src/ProjA/ProjA.Domain.csproj";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var row = await db.Repos.FirstAsync(r => r.Id == repoId);
            row.ScanProjects = new[] { targetCsprojRelative };
            await db.SaveChangesAsync();
        }

        // ── 3. Trigger the scan via the service. PR S3: a single scan row
        //      regardless of how many entries the saved column has.
        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgId, userId, repoId, gitRef: null, CancellationToken.None);
            Assert.Equal(TriggerScanOutcome.Created, trigger.Outcome);
            publicId = trigger.Scan!.PublicId;
        }

        // ── 4. Wait for completion.
        var completed = await WaitForScanStatusAsync(
            factory, publicId, ScanStatus.Completed, TimeSpan.FromMinutes(3));
        Assert.NotNull(completed);
        Assert.Equal(ScanStatus.Completed, completed!.Status);

        // ── 5. Pull the dashboard artifacts.
        var pdfResp = await client.GetAsync($"/api/scans/{publicId:D}/laudo.pdf");
        pdfResp.EnsureSuccessStatusCode();
        var dashboardPdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
        var dashboardPdfHash = Sha256Bytes(dashboardPdfBytes);

        var jsonResp = await client.GetAsync($"/api/scans/{publicId:D}/report.json");
        jsonResp.EnsureSuccessStatusCode();
        var dashboardJsonBytes = await jsonResp.Content.ReadAsByteArrayAsync();
        var dashboardJsonHash = Sha256Bytes(dashboardJsonBytes);

        // ── 6. Run the SAME CLI dll directly against the SAME csproj in
        //      a fresh sandbox (mirrors the worker's CloneRoot — the path
        //      to the csproj must be identical to what the worker resolved
        //      against, byte-for-byte, because solution_path is rendered
        //      relative to a reporting base and any drift would change
        //      hash_content).
        //
        //      The worker's sandbox is a temp dir at
        //      Path.GetTempPath()/lintty-scan-<publicId>/repo. We replicate
        //      the same sandbox shape so the engine sees the same path
        //      depth (the engine renders paths relative to its own
        //      reporting base, so depth-equality is the requirement —
        //      not byte-for-byte path equality).
        var directDir = Path.Combine(Path.GetTempPath(), $"lintty-direct-curated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directDir);
        try
        {
            var directRepoDir = Path.Combine(directDir, "repo");
            CopyTree(TestPaths.MultiCsprojFixture, directRepoDir);

            var targetCsprojAbs = Path.Combine(directRepoDir, targetCsprojRelative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(targetCsprojAbs),
                $"Direct sandbox csproj missing: {targetCsprojAbs}");

            var directPdf = Path.Combine(directDir, "laudo.pdf");
            var directJson = Path.Combine(directDir, "report.json");
            var (exitCode, stderr) = RunEngineDirect(
                targetCsprojAbs, directPdf, directJson, canonVersion: "1.0.0");
            Assert.True(exitCode is 0 or 1,
                $"Direct CLI invocation failed with exit {exitCode}: {stderr}");
            Assert.True(File.Exists(directPdf), "Direct CLI did not produce a PDF.");

            var directPdfBytes = await File.ReadAllBytesAsync(directPdf);
            var directPdfHash = Sha256Bytes(directPdfBytes);
            var directJsonBytes = await File.ReadAllBytesAsync(directJson);
            var directJsonHash = Sha256Bytes(directJsonBytes);

            // ── 7. The gate. Mirrors the auto-detect gate above.
            Assert.True(
                dashboardJsonHash == directJsonHash,
                "Cross-determinism gate FAILED (curated path): report.json hash differs.\n" +
                $"  dashboard: {dashboardJsonHash}\n" +
                $"  cli-direct: {directJsonHash}");
            Assert.True(
                dashboardPdfHash == directPdfHash,
                "Cross-determinism gate FAILED (curated path): PDF differs.\n" +
                $"  dashboard: {dashboardPdfHash}\n" +
                $"  cli-direct: {directPdfHash}\n" +
                $"  dashboard size: {dashboardPdfBytes.Length} B\n" +
                $"  cli-direct size: {directPdfBytes.Length} B");
        }
        finally
        {
            try { Directory.Delete(directDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// PR S3 cross-determinism gate for the <b>multi-csproj combined</b>
    /// path. With <c>repos.scan_projects = [A.csproj, B.csproj]</c>, the
    /// worker writes a transient <c>.lintty-runtime.yml</c> with
    /// <c>projects:</c> declaring both, then runs the engine ONCE. The
    /// resulting PDF must be byte-identical to a CLI direct call against
    /// the same hand-written yaml in the same sandbox shape.
    ///
    /// <para>
    /// This gate is the headline of PR S3: it proves the runtime-yaml
    /// synthesis path produces the same bytes the user could produce by
    /// committing a hand-written <c>lintty.yml</c> with the same
    /// <c>projects:</c> list. If this test ever fails, the multi-csproj
    /// "single combined PDF" promise is broken — investigate before
    /// shipping.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DashboardScan_With_Multiple_Csprojs_Generates_Single_Combined_Pdf()
    {
        await ResetAsync();
        Assert.True(Directory.Exists(TestPaths.MultiCsprojFixture),
            $"multi-csproj fixture missing: {TestPaths.MultiCsprojFixture}");
        Assert.True(File.Exists(TestPaths.EngineCliDll),
            $"Engine CLI dll missing at {TestPaths.EngineCliDll} — build Lintty.Engine.Cli first.");

        // ── 1. Spin up the worker pointed at the multi-csproj fixture.
        await using var factory = CreateFactory();
        factory.DisableWorker = false;
        factory.GitClientOverride = new FixtureCopyGitClient(TestPaths.MultiCsprojFixture);
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "combined@example.com", "Combined Co");

        // ── 2. Register a repo + persist BOTH csprojs as the saved selection.
        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/lintty-demo/multi-csproj",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        var savedProjects = new[]
        {
            "src/ProjA/ProjA.Domain.csproj",
            "src/ProjB/ProjB.Domain.csproj",
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var row = await db.Repos.FirstAsync(r => r.Id == repoId);
            row.ScanProjects = savedProjects;
            await db.SaveChangesAsync();
        }

        // ── 3. Trigger the scan. ONE row, regardless of how many entries.
        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgId, userId, repoId, gitRef: null, CancellationToken.None);
            Assert.Equal(TriggerScanOutcome.Created, trigger.Outcome);
            publicId = trigger.Scan!.PublicId;
        }

        // Sanity: only one scan row was inserted (PR S3 contract).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var rowCount = await db.Scans
                .AsNoTracking()
                .CountAsync(s => s.OrgId == orgId && s.RepoId == repoId);
            Assert.Equal(1, rowCount);
        }

        // ── 4. Wait for completion.
        var completed = await WaitForScanStatusAsync(
            factory, publicId, ScanStatus.Completed, TimeSpan.FromMinutes(3));
        Assert.NotNull(completed);
        Assert.True(
            completed!.Status == ScanStatus.Completed,
            $"Scan did not complete; status={completed.Status}, error={completed.Error}");

        // ── 5. Pull the dashboard artifacts.
        var pdfResp = await client.GetAsync($"/api/scans/{publicId:D}/laudo.pdf");
        pdfResp.EnsureSuccessStatusCode();
        var dashboardPdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
        var dashboardPdfHash = Sha256Bytes(dashboardPdfBytes);

        var jsonResp = await client.GetAsync($"/api/scans/{publicId:D}/report.json");
        jsonResp.EnsureSuccessStatusCode();
        var dashboardJsonBytes = await jsonResp.Content.ReadAsByteArrayAsync();
        var dashboardJsonHash = Sha256Bytes(dashboardJsonBytes);

        // ── 6. Run the SAME CLI dll directly against a hand-written yaml
        //      with the same projects: list, in a sandbox shaped like the
        //      worker's (Path.GetTempPath()/lintty-direct-combined-X/repo).
        var directDir = Path.Combine(Path.GetTempPath(), $"lintty-direct-combined-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directDir);
        try
        {
            var directRepoDir = Path.Combine(directDir, "repo");
            CopyTree(TestPaths.MultiCsprojFixture, directRepoDir);

            // Hand-write a yaml at the SAME path the worker writes its
            // runtime yaml (lintty.yml at the repo root — the resolver
            // only accepts that filename literally). The engine reads
            // from the path passed to --target; both the dashboard and
            // the CLI-direct invocations pass the same sandbox-relative
            // shape so the resolver receives byte-identical input.
            var directYaml = Path.Combine(directRepoDir, "lintty.yml");
            var sb = new StringBuilder();
            sb.AppendLine("# Generated by Lintty Web Inspector. Lifetime: this scan only.");
            sb.AppendLine("# Discarded with the rest of the sandbox after the worker finishes.");
            sb.AppendLine("canon_version: 1.0.0");
            sb.AppendLine("projects:");
            foreach (var p in savedProjects)
            {
                sb.Append("  - '").Append(p).AppendLine("'");
            }
            await File.WriteAllTextAsync(directYaml, sb.ToString());

            var directPdf = Path.Combine(directDir, "laudo.pdf");
            var directJson = Path.Combine(directDir, "report.json");
            var (exitCode, stderr) = RunEngineDirect(
                directYaml, directPdf, directJson, canonVersion: "1.0.0");
            Assert.True(exitCode is 0 or 1,
                $"Direct CLI invocation failed with exit {exitCode}: {stderr}");
            Assert.True(File.Exists(directPdf), "Direct CLI did not produce a PDF.");

            var directPdfBytes = await File.ReadAllBytesAsync(directPdf);
            var directPdfHash = Sha256Bytes(directPdfBytes);
            var directJsonBytes = await File.ReadAllBytesAsync(directJson);
            var directJsonHash = Sha256Bytes(directJsonBytes);

            // ── 7. The gate. The multi-csproj combined path must produce
            //      the same bytes the user could produce by hand.
            Assert.True(
                dashboardJsonHash == directJsonHash,
                "Cross-determinism gate FAILED (combined multi-csproj): report.json hash differs.\n" +
                $"  dashboard: {dashboardJsonHash}\n" +
                $"  cli-direct: {directJsonHash}");
            Assert.True(
                dashboardPdfHash == directPdfHash,
                "Cross-determinism gate FAILED (combined multi-csproj): PDF differs.\n" +
                $"  dashboard: {dashboardPdfHash}\n" +
                $"  cli-direct: {directPdfHash}\n" +
                $"  dashboard size: {dashboardPdfBytes.Length} B\n" +
                $"  cli-direct size: {directPdfBytes.Length} B");

            // Sanity: the combined report should have visited both projects.
            // We don't assert violation counts here (the engine's contract is
            // tested in its own suite); we just verify the JSON is well-formed
            // and carries our canon snapshot.
            using var reportDoc = JsonDocument.Parse(dashboardJsonBytes);
            Assert.True(reportDoc.RootElement.TryGetProperty("canon_version", out var canonEl));
            Assert.Equal("1.0.0", canonEl.GetString());
        }
        finally
        {
            try { Directory.Delete(directDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Polls the <c>scans</c> table directly (no auth round-trip) until the
    /// row reaches <paramref name="terminalStatus"/> or <paramref name="deadline"/>
    /// elapses. Returns the EF row on success, <c>null</c> on timeout.
    /// </summary>
    private static async Task<Scan?> WaitForScanStatusAsync(
        WebInspectorFactory factory,
        Guid publicId,
        string terminalStatus,
        TimeSpan deadline)
    {
        var stop = DateTime.UtcNow + deadline;
        while (DateTime.UtcNow < stop)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var row = await db.Scans
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.PublicId == publicId);
            if (row is not null
                && (row.Status == terminalStatus
                    || row.Status == ScanStatus.Failed
                    || row.Status == ScanStatus.Completed))
            {
                if (row.Status != terminalStatus)
                    return row; // surface failed rows so the test can report
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return null;
    }

    private static string Sha256Bytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private async Task RunFixtureAsync(
        string fixtureDir,
        string slnFileName,
        string expectedGrade,
        int expectedScore,
        int expectedHardLocks)
    {
        Assert.True(Directory.Exists(fixtureDir), $"Fixture missing: {fixtureDir}");
        Assert.True(File.Exists(TestPaths.EngineCliDll),
            $"Engine CLI dll missing at {TestPaths.EngineCliDll} — build Lintty.Engine.Cli first.");

        // ── 1. Spin up the host with the fake git client and the worker enabled.
        await using var factory = CreateFactory();
        factory.DisableWorker = false;
        factory.GitClientOverride = new FixtureCopyGitClient(fixtureDir);
        var client = factory.CreateClient();

        // ── 2. Enqueue a job.
        var post = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/lintty-demo/test",
        });
        post.EnsureSuccessStatusCode();
        using var postDoc = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        var jobId = postDoc.RootElement.GetProperty("job_id").GetString()!;

        // ── 3. Poll until done. Generous deadline because the engine CLI does
        //      a real MSBuild restore + compile on first run.
        var deadline = DateTime.UtcNow.AddMinutes(5);
        string? finalStatus = null;
        JsonDocument? finalDoc = null;
        while (DateTime.UtcNow < deadline)
        {
            var poll = await client.GetAsync($"/api/jobs/{jobId}");
            poll.EnsureSuccessStatusCode();
            finalDoc?.Dispose();
            finalDoc = JsonDocument.Parse(await poll.Content.ReadAsStringAsync());
            finalStatus = finalDoc.RootElement.GetProperty("status").GetString();
            if (finalStatus is "completed" or "failed") break;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.NotNull(finalDoc);
        Assert.Equal("completed", finalStatus);
        Assert.Equal(expectedScore, finalDoc!.RootElement.GetProperty("score").GetInt32());
        Assert.Equal(expectedGrade, finalDoc.RootElement.GetProperty("grade").GetString());
        Assert.Equal(expectedHardLocks, finalDoc.RootElement.GetProperty("hard_locks_open").GetInt32());

        // ── 4. Locate the artifacts via the store (we know where they land).
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var job = await store.GetAsync(jobId, default);
        Assert.NotNull(job);
        Assert.NotNull(job!.PdfPath);
        Assert.NotNull(job.JsonPath);
        Assert.True(File.Exists(job.PdfPath!), $"PDF missing at {job.PdfPath}");
        Assert.True(File.Exists(job.JsonPath!), $"JSON missing at {job.JsonPath}");

        var webPdfHash = Sha256(job.PdfPath!);

        // ── 5. Cross-determinism gate: run the SAME CLI dll directly against
        //      the SAME .sln and confirm the PDF is byte-identical. This is
        //      the product invariant from §10 of the spec — if it ever breaks,
        //      the Web Inspector is no longer "the same engine".
        var directDir = Path.Combine(Path.GetTempPath(), $"lintty-direct-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directDir);
        try
        {
            var slnPath = Path.Combine(fixtureDir, slnFileName);
            var directPdf = Path.Combine(directDir, "laudo.pdf");
            var directJson = Path.Combine(directDir, "report.json");
            var (exitCode, stderr) = RunEngineDirect(slnPath, directPdf, directJson);
            Assert.True(exitCode is 0 or 1,
                $"Direct CLI invocation failed with exit {exitCode}: {stderr}");
            Assert.True(File.Exists(directPdf), "Direct CLI did not produce a PDF.");

            var directHash = Sha256(directPdf);
            Assert.Equal(directHash, webPdfHash);
        }
        finally
        {
            try { Directory.Delete(directDir, recursive: true); }
            catch { /* best effort */ }
        }

        // ── 6. Cleanup invariant: clone scratch space must be gone (spec §6 LGPD).
        var clonePath = Path.Combine(Path.GetTempPath(), $"lintty-{jobId}");
        Assert.False(Directory.Exists(clonePath),
            $"Clone scratch directory should be purged after job completion: {clonePath}");
    }

    private static (int ExitCode, string Stderr) RunEngineDirect(
        string slnPath, string pdfOut, string jsonOut, string? canonVersion = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(slnPath) ?? Directory.GetCurrentDirectory(),
        };
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(TestPaths.EngineCliDll);
        psi.ArgumentList.Add("analyze");
        // ADR 0006: --target accepts .sln, .csproj, or lintty.yml.
        psi.ArgumentList.Add("--target");
        psi.ArgumentList.Add(slnPath);
        psi.ArgumentList.Add("--pdf");
        psi.ArgumentList.Add(pdfOut);
        psi.ArgumentList.Add("--output-file");
        psi.ArgumentList.Add(jsonOut);
        psi.ArgumentList.Add("--fail-on-grade");
        psi.ArgumentList.Add("F");
        // ADR 0007 §3.7: org-bound dashboard scans always pass --canon-version
        // explicitly. The PR 5 cross-determinism gate replays the same flag
        // here so the comparison is apples-to-apples; the V0 anonymous gate
        // (above) never sets it and the engine falls back to lintty.yml.
        if (!string.IsNullOrEmpty(canonVersion))
        {
            psi.ArgumentList.Add("--canon-version");
            psi.ArgumentList.Add(canonVersion);
        }
        psi.Environment["LANG"] = "C";
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        var sb = new StringBuilder();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        process.Start();
        process.BeginErrorReadLine();
        _ = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 5 * 60 * 1000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* */ }
            return (-1, "direct CLI invocation timed out");
        }
        // Make sure async readers drain.
        process.WaitForExit();
        return (process.ExitCode, sb.ToString());
    }

    private static string Sha256(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    /// <summary>
    /// Boots the Web Inspector with a <see cref="Fakes.FixtureCopyGitClient"/>
    /// pointing at <paramref name="fixtureDir"/>, enqueues one job, polls
    /// until the job leaves <c>queued/running</c>, and returns the final
    /// (error_code, status). Used by the negative-path tests in §9.3.
    /// </summary>
    private async Task<(string? ErrorCode, string? Status)> RunWorkerExpectingFailureAsync(string fixtureDir)
    {
        await using var factory = CreateFactory();
        factory.DisableWorker = false;
        factory.GitClientOverride = new Fakes.FixtureCopyGitClient(fixtureDir);
        var client = factory.CreateClient();

        var post = await client.PostAsJsonAsync("/api/jobs", new
        {
            github_url = "https://github.com/lintty-demo/test-target-resolution",
        });
        post.EnsureSuccessStatusCode();
        using var postDoc = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        var jobId = postDoc.RootElement.GetProperty("job_id").GetString()!;

        var deadline = DateTime.UtcNow.AddMinutes(2);
        string? status = null;
        string? errorCode = null;
        while (DateTime.UtcNow < deadline)
        {
            var poll = await client.GetAsync($"/api/jobs/{jobId}");
            poll.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await poll.Content.ReadAsStringAsync());
            status = doc.RootElement.GetProperty("status").GetString();
            if (doc.RootElement.TryGetProperty("error_code", out var ec)
                && ec.ValueKind == JsonValueKind.String)
                errorCode = ec.GetString();
            if (status is "completed" or "failed") break;
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
        return (errorCode, status);
    }

    private static void WriteFakeCsproj(string projDir, string name)
    {
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, name + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>\n" +
            "</Project>\n");
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(dir);
            if (name is "bin" or "obj" or ".git") continue;
            CopyTree(dir, Path.Combine(dst, name));
        }
        foreach (var f in Directory.EnumerateFiles(src))
        {
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        }
    }

    private sealed class TempFixture : IDisposable
    {
        public string Path { get; }
        private TempFixture(string path) { Path = path; }

        public static TempFixture New()
        {
            var p = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "lintty-worker-fix-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(p);
            return new TempFixture(p);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
