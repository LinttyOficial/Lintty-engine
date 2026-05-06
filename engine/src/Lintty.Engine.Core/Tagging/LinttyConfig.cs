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

    /// <summary>
    /// Optional list of <c>.csproj</c> paths declaring the analysis scope when
    /// no <c>.sln</c> exists (ADR 0006 §4). Paths are relative to the
    /// <c>lintty.yml</c> directory; absolute paths, globs, and <c>..</c>
    /// escapes are rejected by <see cref="Workspace.TargetResolver"/>, NOT here
    /// — the parser reads the YAML literally so validation can stay close to
    /// the filesystem (§4.6).
    /// </summary>
    public IReadOnlyList<string> Projects { get; init; } = System.Array.Empty<string>();

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
        var root = ReadYamlOrEmpty(path);
        if (root is null) return Default;

        // Support both flat (canon_version: ...) and nested (lintty: { canon_version: ... }).
        var canonVersion = TryGetScalar(root, "canon_version") ?? "1.0.0";
        var scope = root;
        if (root.Children.TryGetValue(new YamlScalarNode("lintty"), out var nestedRaw)
            && nestedRaw is YamlMappingNode nested)
        {
            scope = nested;
            canonVersion = TryGetScalar(nested, "canon_version") ?? canonVersion;
        }

        var projects = ParseProjectsList(root, scope);
        var (mode, convention, explicitMap) = ParseLayerTagging(scope);

        return ApplyDefaults(canonVersion, mode, convention, explicitMap, projects);
    }

    /// <summary>
    /// Returns the root mapping node when the file exists and parses to a
    /// YAML mapping; <c>null</c> otherwise (caller falls back to defaults).
    /// </summary>
    private static YamlMappingNode? ReadYamlOrEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        using var reader = new StreamReader(path);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0) return null;
        return yaml.Documents[0].RootNode as YamlMappingNode;
    }

    /// <summary>
    /// ADR 0006 §4.6: read <c>projects:</c> from the root scope and from the
    /// nested <c>lintty:</c> scope when distinct. Validation against the
    /// filesystem happens later in <c>TargetResolver</c>.
    /// </summary>
    private static List<string> ParseProjectsList(YamlMappingNode root, YamlMappingNode scope)
    {
        var projects = new List<string>();
        ReadProjects(root, projects);
        if (!ReferenceEquals(scope, root))
            ReadProjects(scope, projects);
        return projects;
    }

    private static (string Mode,
                    Dictionary<string, IReadOnlyList<string>> ConventionMap,
                    Dictionary<string, string> ExplicitMap)
        ParseLayerTagging(YamlMappingNode scope)
    {
        var mode = "convention";
        var convention = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var explicitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (scope.Children.TryGetValue(new YamlScalarNode("layer_tagging"), out var ltRaw)
            && ltRaw is YamlMappingNode lt)
        {
            mode = TryGetScalar(lt, "mode") ?? mode;
            ParseConventionMap(lt, convention);
            ParseExplicitMap(lt, explicitMap);
        }
        return (mode, convention, explicitMap);
    }

    private static void ParseConventionMap(
        YamlMappingNode layerTagging,
        Dictionary<string, IReadOnlyList<string>> convention)
    {
        if (!layerTagging.Children.TryGetValue(new YamlScalarNode("convention_map"), out var cmRaw)
            || cmRaw is not YamlMappingNode cm)
            return;

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

    private static void ParseExplicitMap(
        YamlMappingNode layerTagging,
        Dictionary<string, string> explicitMap)
    {
        if (!layerTagging.Children.TryGetValue(new YamlScalarNode("explicit_map"), out var emRaw)
            || emRaw is not YamlMappingNode em)
            return;

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

    private static LinttyConfig ApplyDefaults(
        string canonVersion,
        string mode,
        Dictionary<string, IReadOnlyList<string>> convention,
        Dictionary<string, string> explicitMap,
        List<string> projects)
    {
        return new LinttyConfig
        {
            CanonVersion = canonVersion,
            Mode = mode,
            ConventionMap = convention.Count > 0 ? convention : DefaultConventionMap,
            ExplicitMap = explicitMap,
            Projects = projects,
        };
    }

    private static void ReadProjects(YamlMappingNode node, List<string> sink)
    {
        if (node.Children.TryGetValue(new YamlScalarNode("projects"), out var raw)
            && raw is YamlSequenceNode seq)
        {
            foreach (var item in seq.Children)
            {
                if (item is YamlScalarNode s && s.Value is not null)
                    sink.Add(s.Value);
            }
        }
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
