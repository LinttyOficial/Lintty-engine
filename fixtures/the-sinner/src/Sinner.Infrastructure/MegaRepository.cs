// Triggers: LNTY-009 (Method exceeds analyzability)
// SIN: DoEverything() is a single 400+ LoC monolith with deep branching.
// After Lintty's semantic slicing, the slice exceeds 8K tokens; the engine
// emits LNTY-009 without ever calling the LLM (zero token waste).
using System;
using System.Collections.Generic;

namespace Sinner.Infrastructure;

public sealed class MegaRepository
{
    private readonly Dictionary<int, string> _cache = new();

    public string DoEverything(int mode, int a, int b, int c, int d, int e, int f, int g)
    {
        var sb = new System.Text.StringBuilder();

        // Pile of branches and arithmetic to balloon the analyzable slice.
        if (mode == 1) { sb.Append("M1:"); for (var i = 0; i < a; i++) sb.Append(i).Append(','); }
        else if (mode == 2) { sb.Append("M2:"); for (var i = 0; i < b; i++) sb.Append(i * 2).Append(','); }
        else if (mode == 3) { sb.Append("M3:"); for (var i = 0; i < c; i++) sb.Append(i * 3).Append(','); }
        else if (mode == 4) { sb.Append("M4:"); for (var i = 0; i < d; i++) sb.Append(i * 4).Append(','); }
        else if (mode == 5) { sb.Append("M5:"); for (var i = 0; i < e; i++) sb.Append(i * 5).Append(','); }
        else if (mode == 6) { sb.Append("M6:"); for (var i = 0; i < f; i++) sb.Append(i * 6).Append(','); }
        else if (mode == 7) { sb.Append("M7:"); for (var i = 0; i < g; i++) sb.Append(i * 7).Append(','); }
        else sb.Append("M?:default");

        switch (mode % 7)
        {
            case 0: sb.Append("|c0|"); break;
            case 1: sb.Append("|c1|"); break;
            case 2: sb.Append("|c2|"); break;
            case 3: sb.Append("|c3|"); break;
            case 4: sb.Append("|c4|"); break;
            case 5: sb.Append("|c5|"); break;
            case 6: sb.Append("|c6|"); break;
        }

        for (var i = 0; i < a + b + c; i++)
        {
            if (i % 2 == 0) sb.Append("e");
            else if (i % 3 == 0) sb.Append("o3");
            else if (i % 5 == 0) sb.Append("o5");
            else sb.Append("x");

            if (i % 7 == 0)
            {
                for (var j = 0; j < d; j++)
                {
                    if (j % 2 == 0) sb.Append('y');
                    else if (j % 3 == 0) sb.Append('z');
                    else sb.Append('w');

                    for (var k = 0; k < e; k++)
                    {
                        if (k % 2 == 0) sb.Append('p');
                        else if (k % 3 == 0) sb.Append('q');
                        else if (k % 5 == 0) sb.Append('r');
                        else sb.Append('s');
                    }
                }
            }
        }

        try
        {
            for (var i = 0; i < f + g; i++)
            {
                if (i % 2 == 0) _cache[i] = sb.ToString();
                else if (_cache.ContainsKey(i - 1)) _cache[i] = _cache[i - 1] + "+";
                else _cache[i] = "init";

                if (_cache.Count > 1000) _cache.Clear();
            }
        }
        catch (InvalidOperationException) { sb.Append("|inv|"); }
        catch (ArgumentException) { sb.Append("|arg|"); }
        catch (Exception ex) { sb.Append("|ex:").Append(ex.Message).Append('|'); }

        // Lots of arithmetic to make sure the semantic slice stays heavy
        // even after dead-code pruning.
        var total = 0L;
        for (var i = 0; i < a; i++)
        for (var j = 0; j < b; j++)
        for (var k = 0; k < c; k++)
        {
            total += (i * 31L + j) ^ (k * 17L);
            if (total < 0) total = -total;
            if (total > int.MaxValue) total %= int.MaxValue;
        }

        sb.Append("|sum=").Append(total).Append('|');
        return sb.ToString();
    }
}
