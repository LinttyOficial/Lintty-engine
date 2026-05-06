using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lintty.WebInspector.Configuration;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Real <see cref="IGitClient"/> backed by spawning <c>git</c> as a subprocess.
/// We do NOT pull libgit2sharp — it's a heavy native dep and the only feature
/// we need is shallow clone, which the system <c>git</c> binary handles.
///
/// The PAT, when present, is composed into the URL only inside this method
/// and zeroed by going out of scope when the call returns. We never write it
/// to log.
/// </summary>
public sealed class GitCliClient : IGitClient
{
    private readonly EngineOptions _options;
    private readonly ILogger<GitCliClient> _logger;

    public GitCliClient(IOptions<EngineOptions> options, ILogger<GitCliClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GitCloneResult> CloneAsync(
        GitHubRepoCoordinates coords,
        string? reference,
        string? token,
        string destination,
        CancellationToken ct)
    {
        // Make sure parent dir exists; let git complain if the destination is non-empty.
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        var psi = BuildCloneProcessStartInfo(coords, reference, token, destination);
        _logger.LogInformation("git clone {Owner}/{Repo} ref={Ref} -> {Dest}",
            coords.Owner, coords.Repo, reference ?? "<default>", destination);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stderrBuf = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuf.AppendLine(e.Data); };

        var startResult = TryStartProcess(process);
        if (startResult is not null) return startResult;

        process.BeginErrorReadLine();
        // We don't care about stdout content for clone, but draining it avoids deadlock.
        _ = process.StandardOutput.ReadToEndAsync(ct);

        var timeoutResult = await WaitForCompletion(process, ct).ConfigureAwait(false);
        if (timeoutResult is not null) return timeoutResult;

        return MapCloneResult(process.ExitCode, stderrBuf.ToString(), token);
    }

    private static ProcessStartInfo BuildCloneProcessStartInfo(
        GitHubRepoCoordinates coords,
        string? reference,
        string? token,
        string destination)
    {
        var cloneUrl = coords.CloneUrl(token);

        var args = new StringBuilder();
        args.Append("clone --depth=1");
        if (!string.IsNullOrEmpty(reference))
            args.Append(" --branch ").Append(QuoteArg(reference));
        // We pass the URL last; subprocess argv list won't echo it to log.
        args.Append(' ').Append(QuoteArg(cloneUrl));
        args.Append(' ').Append(QuoteArg(destination));

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Force C locale so error strings are stable.
        psi.Environment["LANG"] = "C";
        psi.Environment["LC_ALL"] = "C";
        // Suppress any interactive credential prompt. We either have a PAT in the
        // URL or the repo is public; anything else must fail fast, not hang.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        return psi;
    }

    private static GitCloneResult? TryStartProcess(Process process)
    {
        try
        {
            if (!process.Start())
                return new GitCloneResult(false, "git failed to start (is git installed and in PATH?)");
        }
        catch (Exception ex)
        {
            return new GitCloneResult(false, $"git start failed: {ex.Message}");
        }
        return null;
    }

    private async Task<GitCloneResult?> WaitForCompletion(Process process, CancellationToken ct)
    {
        var cloneCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cloneCt.CancelAfter(TimeSpan.FromSeconds(_options.CloneTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cloneCt.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new GitCloneResult(false,
                ct.IsCancellationRequested
                    ? "clone cancelled"
                    : $"clone exceeded {_options.CloneTimeoutSeconds}s timeout");
        }
    }

    private static GitCloneResult MapCloneResult(int exitCode, string stderr, string? token)
    {
        if (exitCode == 0) return new GitCloneResult(true, null);

        // Scrub the token if it accidentally appears in stderr (some
        // git versions echo the URL on auth failure).
        if (!string.IsNullOrEmpty(token))
            stderr = stderr.Replace(token, "<redacted>", StringComparison.Ordinal);
        return new GitCloneResult(false, $"git exit {exitCode}: {stderr.Trim()}");
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }

    private static string QuoteArg(string s)
    {
        // Process.Start on Windows uses CommandLineToArgvW; on Linux it splits
        // by whitespace via the shell-less exec path. Double quotes work on both
        // for our purposes (no embedded quote/backslash in our inputs — we
        // already validate owner/repo and reference).
        return $"\"{s}\"";
    }
}
