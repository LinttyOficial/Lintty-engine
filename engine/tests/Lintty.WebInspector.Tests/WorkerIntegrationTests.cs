using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Lintty.WebInspector.Jobs;
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
/// product invariant from <c>13-web-inspector.md</c> §10.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WorkerIntegrationTests
{
    [Fact]
    public async Task Saint_Runs_End_To_End_And_Pdf_Matches_Cli_Direct_Invocation()
    {
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
        await RunFixtureAsync(
            fixtureDir: TestPaths.SaintNoSlnFixture,
            slnFileName: "lintty.yml",
            expectedGrade: "A",
            expectedScore: 100,
            expectedHardLocks: 0);
    }

    [Fact]
    public async Task NoSln_NoYaml_Empty_Repo_Yields_NoTarget()
    {
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
        // ADR 0006 §5.4: single-csproj implicit fallback in WebInspector mode.
        // We mirror Saint.Domain (no project references, compiles cleanly on
        // its own) so the engine produces a real grade. The worker should
        // complete the job with a valid grade.
        using var scratch = TempFixture.New();
        var domainDir = Path.Combine(TestPaths.SaintFixture, "src", "Saint.Domain");
        var destDir = Path.Combine(scratch.Path, "MyLib");
        CopyTree(domainDir, destDir);
        // Rename csproj so the file's basename is unique. Keep extension.

        await using var factory = new WebInspectorFactory
        {
            DisableWorker = false,
            GitClientOverride = new Fakes.FixtureCopyGitClient(scratch.Path),
        };
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
        // Two csproj, no .sln, no lintty.yml -> ambiguous_target.
        using var scratch = TempFixture.New();
        WriteFakeCsproj(Path.Combine(scratch.Path, "src", "A"), "A");
        WriteFakeCsproj(Path.Combine(scratch.Path, "src", "B"), "B");

        var (errorCode, status) = await RunWorkerExpectingFailureAsync(scratch.Path);
        Assert.Equal("failed", status);
        Assert.Equal(JobErrorCode.AmbiguousTarget, errorCode);
    }

    private static async Task RunFixtureAsync(
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
        await using var factory = new WebInspectorFactory
        {
            DisableWorker = false,
            GitClientOverride = new FixtureCopyGitClient(fixtureDir),
        };
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

    private static (int ExitCode, string Stderr) RunEngineDirect(string slnPath, string pdfOut, string jsonOut)
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
    private static async Task<(string? ErrorCode, string? Status)> RunWorkerExpectingFailureAsync(string fixtureDir)
    {
        await using var factory = new WebInspectorFactory
        {
            DisableWorker = false,
            GitClientOverride = new Fakes.FixtureCopyGitClient(fixtureDir),
        };
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
