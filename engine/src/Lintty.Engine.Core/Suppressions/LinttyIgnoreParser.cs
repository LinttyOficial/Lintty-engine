using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Lintty.Engine.Core.Suppressions;

/// <summary>
/// Extracts <c>// @lintty-ignore: LNTY-XXX reason="..."</c> directives from
/// raw source text. The motor only emits factual records; the policy (hard
/// lock unsuppressible, ≥30-char justification) is applied here at parse time
/// per the canon, but suppressed violations are NEVER mutated — they appear
/// in the report's <c>exceptions[]</c> list with <c>valid</c> + reason.
/// </summary>
public static class LinttyIgnoreParser
{
    public sealed record Suppression(
        string File,
        int Line,
        string RuleId,
        string Justification,
        bool Valid,
        string? InvalidReason);

    private static readonly Regex Directive = new(
        "@lintty-ignore\\s*:\\s*(?<rule>LNTY-\\d{3})\\s+reason\\s*=\\s*\"(?<reason>[^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<Suppression> Parse(string filePath, string sourceText)
    {
        var results = new List<Suppression>();
        if (string.IsNullOrEmpty(sourceText))
            return results;

        var lines = sourceText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var match = Directive.Match(lines[i]);
            if (!match.Success) continue;

            var rule = match.Groups["rule"].Value.ToUpperInvariant();
            var reason = match.Groups["reason"].Value;

            string? invalid = null;
            if (Model.RuleCatalog.IsHardLock(rule))
                invalid = "hard_lock_unsuppressible";
            else if (!Model.RuleCatalog.All.ContainsKey(rule))
                invalid = "unknown_rule";
            else if (reason.Length < 30)
                invalid = "too_short";

            results.Add(new Suppression(
                File: filePath,
                Line: i + 1,
                RuleId: rule,
                Justification: reason,
                Valid: invalid is null,
                InvalidReason: invalid));
        }
        return results;
    }
}
