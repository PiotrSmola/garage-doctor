using System.Text.Json;

namespace GarageDoctor.Domain.Canonicalization;

public sealed class MakeCanonicalizer
{
    private static readonly string[] CorporateSuffixes =
    [
        "INC",
        "LLC",
        "LTD",
        "LP",
        "LLP",
        "CORP",
        "CORPORATION",
        "CO",
        "COMPANY",
        "GMBH",
        "PLC",
        "AG",
        "NV",
        "SA"
    ];

    private static readonly char[] SuffixSeparators = [' ', ','];

    private readonly Dictionary<string, CanonicalName> _aliases;

    private MakeCanonicalizer(Dictionary<string, CanonicalName> aliases) => _aliases = aliases;

    public static MakeCanonicalizer Load(string aliasesJsonPath)
    {
        using var stream = File.OpenRead(aliasesJsonPath);
        var aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Make alias map '{aliasesJsonPath}' is not a JSON object.");
        return FromAliases(aliases);
    }

    public static MakeCanonicalizer FromAliases(IReadOnlyDictionary<string, string> aliases)
    {
        var normalized = new Dictionary<string, string>(aliases.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, target) in aliases)
        {
            var key = RawText.Normalize(alias);
            var value = RawText.Normalize(target);
            if (key.Length > 0 && value.Length > 0)
            {
                normalized[key] = value;
            }
        }

        var resolved = new Dictionary<string, CanonicalName>(normalized.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var key in normalized.Keys)
        {
            resolved[key] = CanonicalName.From(ResolveTarget(normalized, key));
        }

        return new MakeCanonicalizer(resolved);
    }

    public CanonicalName Canonicalize(string rawMake)
    {
        var normalized = StripCorporateSuffixes(RawText.Normalize(rawMake));
        if (normalized.Length == 0)
        {
            return CanonicalName.Unknown;
        }

        return _aliases.TryGetValue(normalized, out var alias) ? alias : CanonicalName.From(normalized);
    }

    private static string ResolveTarget(Dictionary<string, string> aliases, string key)
    {
        var current = aliases[key];
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };
        while (visited.Add(current) && aliases.TryGetValue(current, out var next))
        {
            current = next;
        }

        return current;
    }

    private static string StripCorporateSuffixes(string normalized)
    {
        var remaining = normalized.AsSpan().TrimEnd(SuffixSeparators);
        while (true)
        {
            var separator = remaining.LastIndexOf(' ');
            if (separator < 0 || !IsCorporateSuffix(remaining[(separator + 1)..]))
            {
                break;
            }

            remaining = remaining[..separator].TrimEnd(SuffixSeparators);
        }

        return remaining.Length == normalized.Length ? normalized : remaining.ToString();
    }

    private static bool IsCorporateSuffix(ReadOnlySpan<char> token)
    {
        foreach (var suffix in CorporateSuffixes)
        {
            if (MatchesIgnoringDots(token, suffix))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesIgnoringDots(ReadOnlySpan<char> token, string suffix)
    {
        var index = 0;
        foreach (var character in token)
        {
            if (character == '.')
            {
                continue;
            }

            if (index == suffix.Length || char.ToUpperInvariant(character) != suffix[index])
            {
                return false;
            }

            index++;
        }

        return index == suffix.Length;
    }
}
