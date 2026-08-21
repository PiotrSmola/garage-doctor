using System.Collections.Concurrent;
using System.Text.Json;
using GarageDoctor.Domain.Models;

namespace GarageDoctor.Domain.Canonicalization;

public sealed class ComponentCanonicalizer
{
    public const string FallbackGroup = "OTHER";
    public const string UnknownGroup = "UNKNOWN";

    private static readonly IReadOnlyList<string> NoLevels = [];

    private readonly Dictionary<string, string> _groups;
    private readonly ConcurrentDictionary<string, int> _unmappedTopLevels = new(StringComparer.OrdinalIgnoreCase);

    private ComponentCanonicalizer(Dictionary<string, string> groups) => _groups = groups;

    public IReadOnlyDictionary<string, int> UnmappedTopLevels => _unmappedTopLevels;

    public static ComponentCanonicalizer Load(string groupsJsonPath)
    {
        using var stream = File.OpenRead(groupsJsonPath);
        var groups = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Component group map '{groupsJsonPath}' is not a JSON object.");
        return FromGroups(groups);
    }

    public static ComponentCanonicalizer FromGroups(IReadOnlyDictionary<string, string> groups)
    {
        var normalized = new Dictionary<string, string>(groups.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (category, group) in groups)
        {
            var key = category.Trim();
            var value = group.Trim();
            if (key.Length > 0 && value.Length > 0)
            {
                normalized[key] = value;
            }
        }

        return new ComponentCanonicalizer(normalized);
    }

    public ComponentPath Canonicalize(string rawComponentDescription)
    {
        if (string.IsNullOrWhiteSpace(rawComponentDescription))
        {
            return new ComponentPath { Raw = string.Empty, Group = UnknownGroup, Levels = NoLevels };
        }

        var raw = rawComponentDescription.Trim();
        var levels = SplitLevels(raw);
        if (levels.Count == 0)
        {
            return new ComponentPath { Raw = raw, Group = UnknownGroup, Levels = NoLevels };
        }

        if (_groups.TryGetValue(levels[0], out var group))
        {
            return new ComponentPath { Raw = raw, Group = group, Levels = levels };
        }

        _unmappedTopLevels.AddOrUpdate(levels[0], 1, static (_, count) => count + 1);
        return new ComponentPath { Raw = raw, Group = FallbackGroup, Levels = levels };
    }

    private static List<string> SplitLevels(string raw)
    {
        var levels = new List<string>(4);
        var start = 0;

        for (var index = 0; index <= raw.Length; index++)
        {
            if (index < raw.Length && raw[index] != ':')
            {
                continue;
            }

            var level = raw.AsSpan(start, index - start).Trim();
            if (!level.IsEmpty)
            {
                levels.Add(level.ToString());
            }

            start = index + 1;
        }

        return levels;
    }
}
