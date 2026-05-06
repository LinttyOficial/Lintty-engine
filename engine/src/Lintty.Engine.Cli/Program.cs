using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Lintty.Engine.Core;
using Lintty.Engine.Core.Output;
using Lintty.Engine.Core.Tagging;
using Lintty.Engine.Core.Workspace;
using Lintty.Engine.Reporter;

namespace Lintty.Engine.Cli;

/// <summary>
/// Lintty engine CLI entrypoint. Exit codes (per ADR 0001 §2):
///   0 = analysis ran, grade better than --fail-on-grade
///   1 = analysis ran, grade equal-or-worse than --fail-on-grade
///   2 = execution error (target missing, IO failure, PDF render failure, etc.)
///   3 = usage error (invalid arguments)
///
/// Sprint 1 (ADR 0003): --pdf &lt;path&gt; generates the audit PDF alongside the
/// JSON output. Backward compatible — absent flag preserves the Sprint 0
/// behaviour (JSON only).
///
/// ADR 0006: --target replaces --solution. The latter is kept as a deprecated
/// alias for one version (removed in v0.3.0). --target accepts a .sln, a
/// .csproj, a directory, or a lintty.yml path; resolution rules are in
/// <see cref="TargetResolver"/>.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var exitCode = 0;

        var options = new AnalyzeOptions();
        var analyze = new Command("analyze", "Run the engine over a target.")
        {
            options.Target,
            options.Solution,
            options.Canon,
            options.Output,
            options.OutputFile,
            options.FailOn,
            options.Pdf,
        };

        analyze.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            exitCode = await Commands.RunAnalyzeAsync(ctx, options).ConfigureAwait(false);
        });

        var root = new RootCommand("Lintty engine — Roslyn-based architecture oracle.")
        {
            analyze,
        };

        var parserResult = await root.InvokeAsync(args).ConfigureAwait(false);
        // If System.CommandLine reported a parse error (non-zero), surface it as
        // exit code 3 unless our handler already set a more specific code.
        if (parserResult != 0 && exitCode == 0)
            exitCode = 3;
        return exitCode;
    }

    /// <summary>
    /// Returns true when <paramref name="grade"/> is equal to or worse than
    /// <paramref name="threshold"/>. A is best, F is worst.
    /// </summary>
    public static bool ShouldFail(string grade, string threshold)
    {
        var rank = (string g) => g switch
        {
            "A" => 0, "B" => 1, "C" => 2, "D" => 3, "F" => 4,
            _ => 4,
        };
        return rank(grade) >= rank(threshold);
    }
}

/// <summary>
/// Strongly-typed bag of System.CommandLine option instances for the
/// <c>analyze</c> command. Built once and shared between the command builder
/// and the handler so both reference the same option objects (System.CommandLine
/// keys lookups by reference).
/// </summary>
internal sealed class AnalyzeOptions
{
    public Option<FileSystemInfo?> Target { get; } = new(
        name: "--target",
        description: "Path to a .sln, a .csproj, or a directory/lintty.yml declaring the analysis scope (ADR 0006).");

    public Option<FileSystemInfo?> Solution { get; } = new(
        name: "--solution",
        description: "Deprecated alias for --target. Will be removed in v0.3.0.");

    public Option<string?> Canon { get; } = new(
        name: "--canon-version",
        description: "Override the canon version (defaults to lintty.yml then 1.0.0).");

    public Option<string> Output { get; } = new(
        name: "--output",
        getDefaultValue: () => "json",
        description: "Output format: json or pretty.");

    public Option<FileInfo?> OutputFile { get; } = new(
        name: "--output-file",
        description: "Write report to this path instead of stdout.");

    public Option<string> FailOn { get; } = new(
        name: "--fail-on-grade",
        getDefaultValue: () => "D",
        description: "Exit 1 when the resulting grade is worse-than-or-equal to this value (A|B|C|D|F).");

    public Option<FileInfo?> Pdf { get; } = new(
        name: "--pdf",
        description: "Also generate a PDF audit report at this path (Sprint 1, ADR 0003).");
}

/// <summary>
/// Parsed and validated CLI arguments. Returned by <see cref="ArgsParser.TryParse"/>
/// when input is valid; otherwise the parser writes the error to stderr and
/// returns <c>null</c> with the appropriate exit code.
/// </summary>
internal sealed record ParsedArgs(
    string TargetArg,
    string? Canon,
    string Output,
    FileInfo? OutputFile,
    string FailOn,
    FileInfo? Pdf);

/// <summary>
/// Translates the System.CommandLine InvocationContext into a validated
/// <see cref="ParsedArgs"/>, applying the mutual-exclusion and enum-membership
/// checks that drive exit code 3 (usage error).
/// </summary>
internal static class ArgsParser
{
    public static (ParsedArgs? Args, int? ErrorExitCode) TryParse(
        System.CommandLine.Invocation.InvocationContext ctx,
        AnalyzeOptions opts)
    {
        var target = ctx.ParseResult.GetValueForOption(opts.Target);
        var solution = ctx.ParseResult.GetValueForOption(opts.Solution);
        var canon = ctx.ParseResult.GetValueForOption(opts.Canon);
        var output = (ctx.ParseResult.GetValueForOption(opts.Output) ?? "json").ToLowerInvariant();
        var outputFile = ctx.ParseResult.GetValueForOption(opts.OutputFile);
        var failOn = (ctx.ParseResult.GetValueForOption(opts.FailOn) ?? "D").ToUpperInvariant();
        var pdf = ctx.ParseResult.GetValueForOption(opts.Pdf);

        // ADR 0006 §3.2: --target and --solution are mutually exclusive.
        if (target is not null && solution is not null)
        {
            Console.Error.WriteLine("usage: use only one of --target or --solution.");
            return (null, 3);
        }

        string targetArg;
        if (target is not null)
        {
            targetArg = target.FullName;
        }
        else if (solution is not null)
        {
            Console.Error.WriteLine("warning: --solution is deprecated; use --target. Will be removed in v0.3.0.");
            targetArg = solution.FullName;
        }
        else
        {
            Console.Error.WriteLine("usage: --target is required.");
            return (null, 3);
        }

        if (output != "json" && output != "pretty")
        {
            Console.Error.WriteLine($"Unknown --output value '{output}' (expected json|pretty).");
            return (null, 3);
        }

        if (failOn != "A" && failOn != "B" && failOn != "C" && failOn != "D" && failOn != "F")
        {
            Console.Error.WriteLine($"Unknown --fail-on-grade value '{failOn}' (expected A|B|C|D|F).");
            return (null, 3);
        }

        return (new ParsedArgs(targetArg, canon, output, outputFile, failOn, pdf), null);
    }
}

/// <summary>
/// Top-level command implementations. Kept thin: parse → resolve → run engine
/// → write output → optional PDF → grade-based exit. Each side effect (IO,
/// stdout/stderr, PDF generation) is in this class so <see cref="Program.Main"/>
/// stays a wiring shell.
/// </summary>
internal static class Commands
{
    public static async Task<int> RunAnalyzeAsync(
        System.CommandLine.Invocation.InvocationContext ctx,
        AnalyzeOptions opts)
    {
        try
        {
            var (parsed, parseError) = ArgsParser.TryParse(ctx, opts);
            if (parsed is null) return parseError ?? 3;

            if (!TryResolveTarget(parsed.TargetArg, out var resolved, out var resolveExitCode))
                return resolveExitCode;

            var engine = new LinttyEngine();
            var report = await engine.AnalyzeAsync(resolved!, parsed.Canon).ConfigureAwait(false);
            var json = JsonReport.Serialize(report, indented: parsed.Output == "pretty");

            await WriteJsonOutputAsync(json, parsed.OutputFile).ConfigureAwait(false);

            if (parsed.Pdf is not null)
            {
                if (!TryGeneratePdf(report, parsed.Pdf.FullName, out var pdfExitCode))
                    return pdfExitCode;
            }

            return Program.ShouldFail(report.Grade, parsed.FailOn) ? 1 : 0;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (LayerTaggingError ex)
        {
            // Friendly fail-fast: the message itself teaches the user how
            // to fix their lintty.yml. No stack trace — that would bury
            // the actionable text in noise. Exit 2 = execution error.
            Console.Error.WriteLine("ERR " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Engine error: " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static bool TryResolveTarget(string targetArg, out ResolvedTarget? resolved, out int exitCode)
    {
        try
        {
            resolved = TargetResolver.Resolve(
                targetArg: targetArg,
                cwd: Directory.GetCurrentDirectory(),
                mode: TargetResolverMode.Cli);
            exitCode = 0;
            return true;
        }
        catch (TargetResolutionException ex)
        {
            Console.Error.WriteLine($"{ex.ErrorCode}: {ex.Message}");
            resolved = null;
            exitCode = 2;
            return false;
        }
    }

    private static async Task WriteJsonOutputAsync(string json, FileInfo? outputFile)
    {
        if (outputFile is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile.FullName)!);
            await File.WriteAllTextAsync(outputFile.FullName, json).ConfigureAwait(false);
        }
        else
        {
            Console.Out.Write(json);
        }
    }

    private static bool TryGeneratePdf(Lintty.Engine.Core.Output.ReportDto report, string pdfPath, out int exitCode)
    {
        // Use the COMPACT JSON for PDF input regardless of --output mode,
        // so hash_content is independent of pretty-print whitespace.
        var compactJson = JsonReport.Serialize(report, indented: false);
        try
        {
            var hash = new PdfReporter().GeneratePdf(compactJson, pdfPath);
            var hashShort = hash.Length >= 16 ? hash.Substring(0, 16) : hash;
            Console.Error.WriteLine($"PDF generated: {pdfPath} (hash_content: sha256:{hashShort})");
            exitCode = 0;
            return true;
        }
        catch (Exception pdfEx)
        {
            Console.Error.WriteLine("PDF generation failed: " + pdfEx.Message);
            exitCode = 2;
            return false;
        }
    }
}
