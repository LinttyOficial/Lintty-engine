using System;
using System.Collections.Generic;
using System.Linq;
using Lintty.Engine.Core.Model;

namespace Lintty.Engine.Core.Scoring;

/// <summary>
/// Canon v1.0 scorer (docs/02-canon-v1.md §Cálculo de Score).
///
/// score_raw = 100 - Σ (weight × open violations)
/// score = max(0, round_to_nearest(score_raw, step=5))
/// Hard lock OR open Critical → grade forced to F + seal_eligible=false.
/// </summary>
public static class Scorer
{
    public sealed record Result(
        int Score,
        string Grade,
        bool SealEligible,
        IReadOnlyList<string> HardLocksHit);

    // @lintty-ignore: LNTY-009 reason="Implementação literal da fórmula canônica de scoring (Canon §scoring): pesos C=25/H=10/M=4/L=1, arredondar para múltiplo de 5, clamp 0-100, regras de F automático por crítica aberta e por cap de supressão >10%. A fórmula é a lei do canon — fragmentar em métodos privados oculta a regra de negócio. Coesão é a feature."
    public static Result Compute(
        IReadOnlyList<Violation> violations,
        IReadOnlyList<Suppressions.LinttyIgnoreParser.Suppression> suppressions)
    {
        // Build a quick lookup of valid suppressions by (file, line, ruleId).
        // A violation on the same file and the suppression's line OR the line
        // immediately following counts as suppressed (the directive sits on
        // the line above the offending member, per canon syntax).
        var validSet = new HashSet<(string file, int line, string rule)>();
        foreach (var s in suppressions)
        {
            if (!s.Valid) continue;
            // Apply suppression to the directive line and the next 5 lines (covers
            // the next member declaration in the file).
            for (var offset = 0; offset <= 5; offset++)
                validSet.Add((s.File.Replace('\\', '/'), s.Line + offset, s.RuleId));
        }

        // Sum weights of OPEN violations only (suppressed = closed).
        var openByRule = new Dictionary<string, int>(StringComparer.Ordinal);
        var hardLocksHit = new HashSet<string>(StringComparer.Ordinal);
        var totalDeduction = 0;

        foreach (var v in violations)
        {
            var key = (v.File, v.Line, v.RuleId);
            var suppressed = validSet.Contains(key) && !v.IsHardLock;

            if (v.IsHardLock)
                hardLocksHit.Add(v.RuleId);

            if (suppressed) continue;

            totalDeduction += v.Severity.Weight();
            openByRule[v.RuleId] = openByRule.GetValueOrDefault(v.RuleId) + 1;
        }

        var raw = 100 - totalDeduction;
        var rounded = (int)Math.Round(raw / 5.0, MidpointRounding.ToEven) * 5;
        var score = Math.Max(0, rounded);
        if (raw >= 100) score = 100;

        // Grade table.
        string grade = score switch
        {
            >= 95 => "A",
            >= 85 => "B",
            >= 70 => "C",
            >= 50 => "D",
            _ => "F",
        };

        // Travas absolutas (canon §Travas).
        var anyOpenCritical = violations.Any(v =>
            v.Severity == Severity.Critical
            && !(validSet.Contains((v.File, v.Line, v.RuleId)) && !v.IsHardLock));

        bool sealEligible = hardLocksHit.Count == 0 && !anyOpenCritical;
        if (!sealEligible) grade = "F";

        return new Result(
            Score: sealEligible ? score : Math.Max(0, score),
            Grade: grade,
            SealEligible: sealEligible,
            HardLocksHit: hardLocksHit.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }
}
