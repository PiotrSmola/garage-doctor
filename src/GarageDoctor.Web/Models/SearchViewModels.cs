using System.Globalization;
using System.Text;
using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Web.Models;

public sealed record SearchFilterOption(string Value, string Label);

public sealed record SearchExample(string Label, string? Term, string? MakeSlug, string? ComponentGroup);

public sealed record NarrativeSegment(string Text, bool IsMatch);

public sealed record NarrativeSnippet
{
    public required IReadOnlyList<NarrativeSegment> Segments { get; init; }

    public required bool ClippedStart { get; init; }

    public required bool ClippedEnd { get; init; }

    public required bool HasMatch { get; init; }

    public bool IsEmpty => Segments.Count == 0;
}

public static class NarrativeHighlighter
{
    private const int WindowLength = 380;
    private const int LeadIn = 90;
    private const int MinimumTokenLength = 2;
    private const int InflectionMinimum = 4;
    private const int InflectionTolerance = 3;

    public static NarrativeSnippet Build(string description, string? term, SearchMode mode)
    {
        var text = Collapse(description);
        var matches = FindMatches(text, term, mode);
        var (start, end) = Window(text, matches);

        return new NarrativeSnippet
        {
            Segments = BuildSegments(text, matches, start, end),
            ClippedStart = start > 0,
            ClippedEnd = end < text.Length,
            HasMatch = matches.Count > 0
        };
    }

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static List<(int Start, int Length)> FindMatches(string text, string? term, SearchMode mode)
    {
        var matches = new List<(int Start, int Length)>();
        var trimmed = term?.Trim();

        if (string.IsNullOrEmpty(trimmed) || text.Length == 0)
        {
            return matches;
        }

        if (mode == SearchMode.ScopedPhrase)
        {
            var index = text.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase);

            while (index >= 0)
            {
                matches.Add((index, trimmed.Length));
                index = text.IndexOf(trimmed, index + trimmed.Length, StringComparison.OrdinalIgnoreCase);
            }

            return matches;
        }

        if (mode != SearchMode.FullText)
        {
            return matches;
        }

        var tokens = Tokenize(trimmed);

        if (tokens.Count == 0)
        {
            return matches;
        }

        var position = 0;

        while (position < text.Length)
        {
            if (!IsWordCharacter(text[position]))
            {
                position++;
                continue;
            }

            var wordStart = position;

            while (position < text.Length && IsWordCharacter(text[position]))
            {
                position++;
            }

            foreach (var token in tokens)
            {
                if (WordMatches(text.AsSpan(wordStart, position - wordStart), token))
                {
                    matches.Add((wordStart, position - wordStart));
                    break;
                }
            }
        }

        return matches;
    }

    private static List<string> Tokenize(string term)
    {
        var tokens = new List<string>();

        foreach (var piece in term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (piece.StartsWith('-'))
            {
                continue;
            }

            var start = -1;

            for (var index = 0; index <= piece.Length; index++)
            {
                var isWord = index < piece.Length && IsWordCharacter(piece[index]);

                if (isWord && start < 0)
                {
                    start = index;
                }
                else if (!isWord && start >= 0)
                {
                    if (index - start >= MinimumTokenLength)
                    {
                        tokens.Add(piece[start..index]);
                    }

                    start = -1;
                }
            }
        }

        return tokens;
    }

    private static bool WordMatches(ReadOnlySpan<char> word, string token)
    {
        if (word.Equals(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var shorter = word.Length <= token.Length ? word : token.AsSpan();
        var longer = word.Length <= token.Length ? token.AsSpan() : word;

        return shorter.Length >= InflectionMinimum
            && longer.Length - shorter.Length <= InflectionTolerance
            && longer[..shorter.Length].Equals(shorter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value);

    private static (int Start, int End) Window(string text, List<(int Start, int Length)> matches)
    {
        if (text.Length <= WindowLength)
        {
            return (0, text.Length);
        }

        var anchor = matches.Count > 0 ? matches[0].Start : 0;
        var floor = matches.Count > 0 ? matches[0].Start + matches[0].Length : 1;
        var start = Math.Max(0, anchor - LeadIn);
        var end = Math.Min(text.Length, Math.Max(start + WindowLength, floor));

        start = SnapForward(text, start, anchor);
        end = SnapBackward(text, end, Math.Min(floor, end));

        return (start, end);
    }

    private static int SnapForward(string text, int start, int limit)
    {
        while (start > 0 && start < limit && text[start - 1] != ' ')
        {
            start++;
        }

        return start;
    }

    private static int SnapBackward(string text, int end, int floor)
    {
        while (end > floor && end < text.Length && text[end] != ' ')
        {
            end--;
        }

        return end;
    }

    private static List<NarrativeSegment> BuildSegments(
        string text,
        List<(int Start, int Length)> matches,
        int start,
        int end)
    {
        var segments = new List<NarrativeSegment>();
        var cursor = start;

        foreach (var match in matches)
        {
            if (match.Start < cursor || match.Start + match.Length > end)
            {
                continue;
            }

            if (match.Start > cursor)
            {
                segments.Add(new NarrativeSegment(text[cursor..match.Start], false));
            }

            cursor = match.Start + match.Length;
            segments.Add(new NarrativeSegment(text[match.Start..cursor], true));
        }

        if (cursor < end)
        {
            segments.Add(new NarrativeSegment(text[cursor..end], false));
        }

        return segments;
    }
}

public sealed record SearchFormViewModel
{
    public string? Term { get; init; }

    public string? MakeSlug { get; init; }

    public string? ComponentGroup { get; init; }

    public int? YearFrom { get; init; }

    public int? YearTo { get; init; }

    public bool MileageOnly { get; init; }

    public bool HasTerm => !string.IsNullOrEmpty(Term);

    public bool HasFilters =>
        !string.IsNullOrEmpty(MakeSlug)
        || !string.IsNullOrEmpty(ComponentGroup)
        || YearFrom is not null
        || YearTo is not null
        || MileageOnly;

    public bool IsEmpty => !HasTerm && !HasFilters;

    public string? TermParam => HasTerm ? Term : null;

    public string? MakeSlugParam => string.IsNullOrEmpty(MakeSlug) ? null : MakeSlug;

    public string? ComponentGroupParam => string.IsNullOrEmpty(ComponentGroup) ? null : ComponentGroup;

    public string? YearFromParam => YearFrom?.ToString(CultureInfo.InvariantCulture);

    public string? YearToParam => YearTo?.ToString(CultureInfo.InvariantCulture);

    public string? MileageOnlyParam => MileageOnly ? "true" : null;
}

public sealed record SearchHitViewModel
{
    public required int Id { get; init; }

    public required string Make { get; init; }

    public required string Model { get; init; }

    public required string MakeSlug { get; init; }

    public required string ModelSlug { get; init; }

    public required int? ModelYear { get; init; }

    public required DateOnly ReceivedDate { get; init; }

    public required int? MilesAtFailure { get; init; }

    public required string ComponentGroup { get; init; }

    public required NarrativeSnippet Snippet { get; init; }

    public required IReadOnlyList<string> Outcomes { get; init; }

    public bool HasVehicleLink => ModelYear is not null
        && !string.IsNullOrEmpty(MakeSlug)
        && !string.IsNullOrEmpty(ModelSlug);

    public string VehicleLabel => ModelYear is { } year
        ? string.Create(CultureInfo.InvariantCulture, $"{year} {Make} {Model}")
        : string.Create(CultureInfo.InvariantCulture, $"{Make} {Model}");

    public string ReceivedLabel => ReceivedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string MileageLabel => MilesAtFailure is { } miles
        ? string.Create(CultureInfo.InvariantCulture, $"{miles:N0} miles")
        : "mileage not recorded";

    public static SearchHitViewModel From(ComplaintSearchHit hit, string? term, SearchMode mode)
    {
        ArgumentNullException.ThrowIfNull(hit);

        var outcomes = new List<string>();

        if (hit.Crash)
        {
            outcomes.Add("crash reported");
        }

        if (hit.Fire)
        {
            outcomes.Add("fire reported");
        }

        if (hit.Injured > 0)
        {
            outcomes.Add(string.Create(CultureInfo.InvariantCulture, $"{hit.Injured:N0} injured"));
        }

        if (hit.Deaths > 0)
        {
            outcomes.Add(string.Create(CultureInfo.InvariantCulture, $"{hit.Deaths:N0} killed"));
        }

        return new SearchHitViewModel
        {
            Id = hit.Id,
            Make = hit.Make,
            Model = hit.Model,
            MakeSlug = hit.MakeSlug,
            ModelSlug = hit.ModelSlug,
            ModelYear = hit.ModelYear,
            ReceivedDate = hit.ReceivedDate,
            MilesAtFailure = hit.MilesAtFailure,
            ComponentGroup = hit.ComponentGroup,
            Snippet = NarrativeHighlighter.Build(hit.Description, term, mode),
            Outcomes = outcomes
        };
    }
}

public sealed record SearchPageViewModel
{
    public required SearchFormViewModel Form { get; init; }

    public required IReadOnlyList<SearchFilterOption> Makes { get; init; }

    public required IReadOnlyList<SearchFilterOption> FrequentMakes { get; init; }

    public required IReadOnlyList<SearchFilterOption> ComponentGroups { get; init; }

    public required IReadOnlyList<SearchExample> Examples { get; init; }

    public required IReadOnlyList<SearchHitViewModel> Hits { get; init; }

    public required SearchMode Mode { get; init; }

    public required long MatchCount { get; init; }

    public required bool MatchCountIsExact { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required int LastPage { get; init; }

    public required bool HasPreviousPage { get; init; }

    public required bool HasNextPage { get; init; }

    public required long ElapsedMilliseconds { get; init; }

    public required string? MakeLabel { get; init; }

    public required string? UnresolvedMake { get; init; }

    public required string? UnresolvedComponentGroup { get; init; }

    public bool Searched => !Form.IsEmpty;

    public bool HasHits => Hits.Count > 0;

    public bool PastTheEnd => Searched && !HasHits && Page > 1;

    public long FirstResultNumber => ((long)(Page - 1) * PageSize) + 1;

    public long LastResultNumber => ((long)(Page - 1) * PageSize) + Hits.Count;

    public string MatchCountLabel => MatchCountIsExact
        ? MatchCount == 1
            ? "1 match"
            : string.Create(CultureInfo.InvariantCulture, $"{MatchCount:N0} matches")
        : string.Create(CultureInfo.InvariantCulture, $"{MatchCount:N0}+ matches");

    public string ResultsHeadline => HasHits
        ? PositionLabel
        : PastTheEnd
            ? "Past the last page"
            : "No matches";

    public string PositionLabel => string.Create(
        CultureInfo.InvariantCulture,
        $"Showing {FirstResultNumber:N0} to {LastResultNumber:N0} of {MatchCountLabel}");

    public string PageLabel => MatchCountIsExact
        ? string.Create(CultureInfo.InvariantCulture, $"Page {Page:N0} of {LastPage:N0}")
        : string.Create(CultureInfo.InvariantCulture, $"Page {Page:N0} of at least {LastPage:N0}");

    public string TimingLabel => string.Create(
        CultureInfo.InvariantCulture,
        $"Query returned in {ElapsedMilliseconds:N0} ms");

    public string ModeHeadline => Mode switch
    {
        SearchMode.FullText => "Whole word search across every narrative",
        SearchMode.ScopedPhrase => "Literal substring search inside the filtered set",
        _ => "Filtered listing, no search term"
    };

    public string ModeExplanation => Mode switch
    {
        SearchMode.FullText =>
            "Whole words anywhere in a narrative, stemmed, so brakes also finds brake. These are matches in "
            + "database order, not ranked by relevance.",
        SearchMode.ScopedPhrase =>
            "Inside the filtered set the term is matched as a literal, case insensitive substring, so it also "
            + "hits inside longer words. These are matches in database order, not ranked by relevance.",
        _ =>
            "Every complaint that matches the filters, in database order. Add a term to search the narratives "
            + "themselves."
    };

    public string? PerformanceNote => Mode switch
    {
        SearchMode.FullText =>
            "Relevance ranking is left out on purpose. The text index runs to 745 MB over 2.2 GB of narratives, "
            + "so returning a page of matches takes tens of milliseconds, while sorting the same query by text "
            + "score takes 11 to 44 seconds.",
        SearchMode.ScopedPhrase =>
            "A filtered search runs through the vehicle and component indexes rather than the text index, which "
            + "is what makes an exact count affordable here.",
        _ => null
    };

    public string? CountCaveat => MatchCountIsExact
        ? null
        : "Counting stops at 5,000, so 5,000+ is a floor and not the total. Add a filter to get an exact count.";

    public string? FilterSentence
    {
        get
        {
            var parts = new List<string>();

            if (MakeLabel is not null)
            {
                parts.Add(MakeLabel);
            }

            if (!string.IsNullOrEmpty(Form.ComponentGroup))
            {
                parts.Add(Form.ComponentGroup + " complaints");
            }

            if (Form.YearFrom is { } from && Form.YearTo is { } to)
            {
                parts.Add(from == to
                    ? string.Create(CultureInfo.InvariantCulture, $"model year {from}")
                    : string.Create(CultureInfo.InvariantCulture, $"model years {from} to {to}"));
            }
            else if (Form.YearFrom is { } lower)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"model year {lower} and later"));
            }
            else if (Form.YearTo is { } upper)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"model year {upper} and earlier"));
            }

            if (Form.MileageOnly)
            {
                parts.Add("only reports that carry a mileage reading");
            }

            return parts.Count == 0 ? null : "Filtered to " + Sentence(parts) + ".";
        }
    }

    private static string Sentence(IReadOnlyList<string> parts) => parts.Count switch
    {
        1 => parts[0],
        2 => parts[0] + " and " + parts[1],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]
    };
}
