using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Lintty.Engine.Core;
using Lintty.Engine.Core.Output;
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

        var targetOption = new Option<FileSystemInfo?>(
            name: "--target",
            description: "Path to a .sln, a .csproj, or a directory/lintty.yml declaring the analysis scope (ADR 0006).");

        var solutionOption = new Option<FileSystemInfo?>(
            name: "--solution",
            description: "Deprecated alias for --target. Will be removed in v0.3.0.");

        var canonOption = new Option<string?>(
            name: "--canon-version",
            description: "Override the canon version (defaults to lintty.yml then 1.0.0).");

        var outputOption = new Option<string>(
            name: "--output",
            getDefaultValue: () => "json",
            description: "Output format: json or pretty.");

        var outputFileOption = new Option<FileInfo?>(
            name: "--output-file",
            description: "Write report to this path instead of stdout.");

        var failOnOption = new Option<string>(
            name: "--fail-on-grade",
            getDefaultValue: () => "D",
            description: "Exit 1 when the resulting grade is worse-than-or-equal to this value (A|B|C|D|F).");

        var pdfOption = new Option<FileInfo?>(
            name: "--pdf",
            description: "Also generate a PDF audit report at this path (Sprint 1, ADR 0003).");

        var analyze = new Command("analyze", "Run the engine over a target.")
        {
            targetOption,
            solutionOption,
            canonOption,
            outputOption,
            outputFileOption,
            failOnOption,
            pdfOption,
        };

        analyze.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var target = ctx.ParseResult.GetValueForOption(targetOption);
            var solution = ctx.ParseResult.GetValueForOption(solutionOption);
            var canon = ctx.ParseResult.GetValueForOption(canonOption);
            var output = ctx.ParseResult.GetValueForOption(outputOption) ?? "json";
            var outputFile = ctx.ParseResult.GetValueForOption(outputFileOption);
            var failOn = ctx.ParseResult.GetValueForOption(failOnOption) ?? "D";
            var pdfPath = ctx.ParseResult.GetValueForOption(pdfOption);

            try
            {
                // ADR 0006 §3.2: --target and --solution are mutually exclusive.
                if (target is not null && solution is not null)
                {
                    Console.Error.WriteLine("usage: use only one of --target or --solution.");
                    exitCode = 3;
                    return;
                }

                string? targetArg;
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
                    exitCode = 3;
                    return;
                }

                output = output.ToLowerInvariant();
                if (output != "json" && output != "pretty")
                {
                    Console.Error.WriteLine($"Unknown --output value '{output}' (expected json|pretty).");
                    exitCode = 3;
                    return;
                }

                failOn = failOn.ToUpperInvariant();
                if (failOn != "A" && failOn != "B" && failOn != "C" && failOn != "D" && failOn != "F")
                {
                    Console.Error.WriteLine($"Unknown --fail-on-grade value '{failOn}' (expected A|B|C|D|F).");
                    exitCode = 3;
                    return;
                }

                ResolvedTarget resolved;
                try
                {
                    resolved = TargetResolver.Resolve(
                        targetArg: targetArg,
                        cwd: Directory.GetCurrentDirectory(),
                        mode: TargetResolverMode.Cli);
                }
                catch (TargetResolutionException ex)
                {
                    Console.Error.WriteLine($"{ex.ErrorCode}: {ex.Message}");
                    exitCode = 2;
                    return;
                }

                var engine = new LinttyEngine();
                var report = await engine.AnalyzeAsync(resolved, canon).ConfigureAwait(false);
                var json = JsonReport.Serialize(report, indented: output == "pretty");

                if (outputFile is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputFile.FullName)!);
                    await File.WriteAllTextAsync(outputFile.FullName, json).ConfigureAwait(false);
                }
                else
                {
                    Console.Out.Write(json);
                }

                if (pdfPath is not null)
                {
                    // Use the COMPACT JSON for PDF input regardless of --output mode,
                    // so hash_content is independent of pretty-print whitespace.
                    var compactJson = JsonReport.Serialize(report, indented: false);
                    try
                    {
                        var hash = new PdfReporter().GeneratePdf(compactJson, pdfPath.FullName);
                        var hashShort = hash.Length >= 16 ? hash.Substring(0, 16) : hash;
                        Console.Error.WriteLine($"PDF generated: {pdfPath.FullName} (hash_content: sha256:{hashShort})");
                    }
                    catch (Exception pdfEx)
                    {
                        Console.Error.WriteLine("PDF generation failed: " + pdfEx.Message);
                        exitCode = 2;
                        return;
                    }
                }

                exitCode = ShouldFail(report.Grade, failOn) ? 1 : 0;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                exitCode = 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Engine error: " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                exitCode = 2;
            }
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
