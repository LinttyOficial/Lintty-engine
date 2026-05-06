using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Lintty.Engine.Core.Model;

namespace Lintty.Engine.Core.Tagging;

/// <summary>
/// Classifies projects into Domain / Application / Infrastructure / Presentation
/// using lintty.yml explicit_map (precedence) then convention_map. Symbols are
/// classified via their containing assembly name lookup against the same map.
///
/// PERMISSIVE policy (2026-05-06 Web Inspector UX fix): a project that matches
/// no pattern AND has no explicit override is classified as <see cref="Layer.Unknown"/>.
/// The engine no longer aborts the analysis — instead, layer-aware rules
/// (LNTY-001 Domain Layer Isolation, LNTY-008 Ports at Boundaries) skip Unknown
/// projects, while layer-agnostic rules (LNTY-007 Dependency Cycles, LNTY-009
/// Method Size) still run on them. The laudo's layer summary surfaces the
/// "Unknown" row with a call-to-action so the user knows what is missing.
/// Rationale: prospects on the public Web Inspector typically don't ship a
/// <c>lintty.yml</c>; demanding one upfront is hostile to the default flow.
/// Strict mode (fail-fast) lives only as the still-present
/// <see cref="LayerTaggingError"/> exception type, available for future opt-in.
/// </summary>
public sealed class LayerTagger
{
    private readonly LinttyConfig _config;
    private readonly Dictionary<string, Layer> _projectCache = new(StringComparer.OrdinalIgnoreCase);

    public LayerTagger(LinttyConfig config)
    {
        _config = config;
    }

    public Layer Classify(string projectName, string? csprojFileName = null)
    {
        projectName ??= string.Empty;
        var key = projectName;
        if (_projectCache.TryGetValue(key, out var cached))
            return cached;

        // 1. explicit_map (csproj filename match — canon spec key is the file name)
        if (csprojFileName is not null
            && _config.ExplicitMap.TryGetValue(Path.GetFileName(csprojFileName), out var explicitTag))
        {
            var lExplicit = ParseLayer(explicitTag);
            _projectCache[key] = lExplicit;
            return lExplicit;
        }

        // 2. convention_map (glob over assembly/project name)
        foreach (var (tag, patterns) in _config.ConventionMap)
        {
            foreach (var pattern in patterns)
            {
                if (GlobMatch(projectName, pattern))
                {
                    var l = ParseLayer(tag);
                    _projectCache[key] = l;
                    return l;
                }
            }
        }

        _projectCache[key] = Layer.Unknown;
        return Layer.Unknown;
    }

    private static Layer ParseLayer(string tag) => tag.ToLowerInvariant() switch
    {
        "domain" => Layer.Domain,
        "domain.abstractions" => Layer.DomainAbstractions,
        "application" => Layer.Application,
        "infrastructure" => Layer.Infrastructure,
        "presentation" => Layer.Presentation,
        _ => Layer.Unknown,
    };

    /// <summary>
    /// Minimal glob: supports leading/trailing/internal '*' which becomes '.*'
    /// and matches case-insensitively.
    /// </summary>
    private static bool GlobMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern))
            return false;
        var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(input, rx, RegexOptions.IgnoreCase);
    }
}
