using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace Lintty.Engine.Core.Tagging;

/// <summary>
/// Minimal lintty.yml parser. Supports canon_version + layer_tagging mode +
/// convention_map + explicit_map. Anything not parsed is silently ignored
/// (forward-compat for richer config in later sprints).
/// </summary>
public sealed class LinttyConfig
{
    public string CanonVersion { get; init; } = "1.0.0";
    public string Mode { get; init; } = "convention";
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ConventionMap { get; init; }
        = DefaultConventionMap;
    public IReadOnlyDictionary<string, string> ExplicitMap { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultConventionMap { get; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["domain"] = new[] { "*.Domain", "*.Domain.*", "*.Core" },
            ["domain.abstractions"] = new[] { "*.Domain.Abstractions", "*.Abstractions" },
            ["application"] = new[] { "*.Application", "*.Application.*", "*.UseCases" },
            ["infrastructure"] = new[] { "*.Infrastructure", "*.Infrastructure.*", "*.Persistence", "*.Data" },
            ["presentation"] = new[] { "*.Api", "*.Web", "*.Controllers", "*.Presentation" },
        };

    public static LinttyConfig Default => new();

    public static LinttyConfig LoadOrDefault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Default;

        using var reader = new StreamReader(path);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0)
            return Default;

        var root = yaml.Documents[0].RootNode as YamlMappingNode;
        if (root is null)
            return Default;

        var canonVersion = TryGetScalar(root, "canon_version") ?? "1.0.0";
        var mode = "convention";
        var convention = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var explicitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Support both flat (canon_version: ...) and nested (lintty: { canon_version: ... }).
        var scope = root;
        if (root.Children.TryGetValue(new YamlScalarNode("lintty"), out var nestedRaw)
            && nestedRaw is YamlMappingNode nested)
        {
            scope = nested;
            canonVersion = TryGetScalar(nested, "canon_version") ?? canonVersion;
        }

        if (scope.Children.TryGetValue(new YamlScalarNode("layer_tagging"), out var ltRaw)
            && ltRaw is YamlMappingNode lt)
        {
            mode = TryGetScalar(lt, "mode") ?? mode;

            if (lt.Children.TryGetValue(new YamlScalarNode("convention_map"), out var cmRaw)
                && cmRaw is YamlMappingNode cm)
            {
                foreach (var entry in cm.Children)
                {
                    if (entry.Key is YamlScalarNode key
                        && entry.Value is YamlSequenceNode seq)
                    {
                        var patterns = new List<string>();
                        foreach (var item in seq.Children)
                            if (item is YamlScalarNode s && s.Value is not null)
                                patterns.Add(s.Value);
                        convention[key.Value ?? "domain"] = patterns;
                    }
                }
            }

            if (lt.Children.TryGetValue(new YamlScalarNode("explicit_map"), out var emRaw)
                && emRaw is YamlMappingNode em)
            {
                foreach (var entry in em.Children)
                {
                    if (entry.Key is YamlScalarNode k
                        && entry.Value is YamlScalarNode v
                        && k.Value is not null && v.Value is not null)
                    {
                        explicitMap[k.Value] = v.Value;
                    }
                }
            }
        }

        return new LinttyConfig
        {
            CanonVersion = canonVersion,
            Mode = mode,
            ConventionMap = convention.Count > 0 ? convention : DefaultConventionMap,
            ExplicitMap = explicitMap,
        };
    }

    private static string? TryGetScalar(YamlMappingNode node, string key)
    {
        if (node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            && value is YamlScalarNode scalar)
        {
            return scalar.Value;
        }
        return null;
    }
}
