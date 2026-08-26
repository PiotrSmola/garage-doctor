using System.Globalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Web.Models;

public enum CompareSideStatus
{
    Empty,
    Incomplete,
    Unknown,
    Missing,
    Resolved
}

public sealed record CompareQuery
{
    public string? A { get; init; }

    public string? B { get; init; }

    public string? AMake { get; init; }

    public string? AModel { get; init; }

    public int? AYear { get; init; }

    public string? BMake { get; init; }

    public string? BModel { get; init; }

    public int? BYear { get; init; }
}

public sealed record CompareSideRequest
{
    public string? MakeSlug { get; init; }

    public string? ModelSlug { get; init; }

    public int? ModelYear { get; init; }

    public string? RawKey { get; init; }

    public bool FoldsIntoKey { get; init; }

    public bool IsComplete => MakeSlug is not null && ModelSlug is not null && ModelYear is not null;

    public bool IsBlank => MakeSlug is null && ModelSlug is null && ModelYear is null && RawKey is null;

    public string? Key => IsComplete
        ? string.Create(CultureInfo.InvariantCulture, $"{MakeSlug}|{ModelSlug}|{ModelYear}")
        : null;

    public string? RequestedKey => Key ?? RawKey;

    public CompareSideStatus StatusBeforeLookup => this switch
    {
        { IsComplete: true } => CompareSideStatus.Resolved,
        { RawKey: not null } => CompareSideStatus.Unknown,
        { IsBlank: true } => CompareSideStatus.Empty,
        _ => CompareSideStatus.Incomplete
    };

    public static CompareSideRequest From(string? key, string? make, string? model, int? year)
    {
        var makeSlug = Slug(make);
        var modelSlug = Slug(model);

        if (makeSlug is not null && modelSlug is not null && year is > 0)
        {
            return new CompareSideRequest
            {
                MakeSlug = makeSlug,
                ModelSlug = modelSlug,
                ModelYear = year,
                FoldsIntoKey = true
            };
        }

        var trimmedKey = key?.Trim();

        if (!string.IsNullOrEmpty(trimmedKey))
        {
            return Parse(trimmedKey) ?? new CompareSideRequest { RawKey = trimmedKey };
        }

        return new CompareSideRequest
        {
            MakeSlug = makeSlug,
            ModelSlug = modelSlug,
            ModelYear = year > 0 ? year : null
        };
    }

    public IEnumerable<string> QueryParts(string side)
    {
        if (Key is { } key)
        {
            yield return $"{side}={Uri.EscapeDataString(key)}";
            yield break;
        }

        if (RawKey is { } raw)
        {
            yield return $"{side}={Uri.EscapeDataString(raw)}";
            yield break;
        }

        if (MakeSlug is { } makeSlug)
        {
            yield return $"{side}Make={Uri.EscapeDataString(makeSlug)}";
        }

        if (ModelSlug is { } modelSlug)
        {
            yield return $"{side}Model={Uri.EscapeDataString(modelSlug)}";
        }

        if (ModelYear is { } modelYear)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"{side}Year={modelYear}");
        }
    }

    private static CompareSideRequest? Parse(string key)
    {
        var segments = key.Split('|');

        if (segments.Length != 3)
        {
            return null;
        }

        var makeSlug = Slug(segments[0]);
        var modelSlug = Slug(segments[1]);
        var parsedYear = int.TryParse(segments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year);

        return makeSlug is null || modelSlug is null || !parsedYear || year <= 0
            ? null
            : new CompareSideRequest
            {
                MakeSlug = makeSlug,
                ModelSlug = modelSlug,
                ModelYear = year,
                RawKey = key
            };
    }

    private static string? Slug(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }
}

public static class ComparePaths
{
    public const string Root = "/compare";

    public static string Build(CompareSideRequest first, CompareSideRequest second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var parts = first.QueryParts("a").Concat(second.QueryParts("b")).ToList();

        return parts.Count == 0 ? Root : $"{Root}?{string.Join("&", parts)}";
    }

    public static string ForKeys(string firstKey, string secondKey) =>
        $"{Root}?a={Uri.EscapeDataString(firstKey)}&b={Uri.EscapeDataString(secondKey)}";

    public static string ForSingleKey(string side, string key) =>
        $"{Root}?{side}={Uri.EscapeDataString(key)}";
}

public sealed record CompareOption(string Value, string Label, bool Selected);

public sealed record CompareSidePicker
{
    public required string Side { get; init; }

    public required string Label { get; init; }

    public required IReadOnlyList<CompareOption> Makes { get; init; }

    public IReadOnlyList<CompareOption> Models { get; init; } = [];

    public IReadOnlyList<CompareOption> Years { get; init; } = [];

    public string MakeField => Side + "Make";

    public string ModelField => Side + "Model";

    public string YearField => Side + "Year";

    public string MakeId => Side + "-make";

    public string ModelId => Side + "-model";

    public string YearId => Side + "-year";

    public bool ModelStepReady => Models.Count > 0;

    public bool YearStepReady => Years.Count > 0;
}

public sealed record CompareRecallGroup(string Group, int CampaignCount);

public sealed record CompareRecallSummary
{
    public required int CampaignCount { get; init; }

    public required int DoNotDriveCount { get; init; }

    public required int ParkOutsideCount { get; init; }

    public required IReadOnlyList<string> UrgentCampaignNumbers { get; init; }

    public required IReadOnlyList<CompareRecallGroup> Groups { get; init; }

    public required int? EarliestYear { get; init; }

    public required int? LatestYear { get; init; }

    public bool HasCampaigns => CampaignCount > 0;

    public bool HasUrgent => DoNotDriveCount > 0 || ParkOutsideCount > 0;

    public string CampaignCountLabel => CampaignCount.ToString("N0");

    public string AdvisoryLabel => (DoNotDriveCount, ParkOutsideCount) switch
    {
        (0, 0) => "none",
        (> 0, > 0) => "do not drive, and park outside",
        (> 0, 0) => "do not drive",
        _ => "park outside"
    };

    public string GroupsLabel => Groups.Count == 0
        ? "none"
        : string.Join(", ", Groups.Select(group => group.Group));

    public string SpanLabel => (EarliestYear, LatestYear) switch
    {
        (null, _) or (_, null) => "not dated",
        var (earliest, latest) when earliest == latest =>
            string.Create(CultureInfo.InvariantCulture, $"all filed in {earliest}"),
        var (earliest, latest) =>
            string.Create(CultureInfo.InvariantCulture, $"{earliest} to {latest}")
    };

    public string UrgentCampaignLabel => string.Join(", ", UrgentCampaignNumbers);

    public string UrgentCampaignNoun => UrgentCampaignNumbers.Count == 1 ? "campaign" : "campaigns";

    public static CompareRecallSummary From(IReadOnlyList<RecallCampaign> campaigns)
    {
        ArgumentNullException.ThrowIfNull(campaigns);

        var distinct = campaigns
            .GroupBy(campaign => campaign.CampaignNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var years = distinct
            .Select(campaign => campaign.ReportReceivedDate)
            .OfType<DateOnly>()
            .Select(date => date.Year)
            .ToList();

        return new CompareRecallSummary
        {
            CampaignCount = distinct.Count,
            DoNotDriveCount = distinct.Count(campaign => campaign.DoNotDrive),
            ParkOutsideCount = distinct.Count(campaign => campaign.ParkOutside),
            UrgentCampaignNumbers = distinct
                .Where(campaign => campaign.DoNotDrive || campaign.ParkOutside)
                .Select(campaign => campaign.CampaignNumber)
                .OrderBy(number => number, StringComparer.Ordinal)
                .ToList(),
            Groups = distinct
                .GroupBy(campaign => campaign.ComponentGroup, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CompareRecallGroup(group.Key, group.Count()))
                .OrderByDescending(group => group.CampaignCount)
                .ThenBy(group => group.Group, StringComparer.Ordinal)
                .ToList(),
            EarliestYear = years.Count == 0 ? null : years.Min(),
            LatestYear = years.Count == 0 ? null : years.Max()
        };
    }
}

public sealed record CompareVehicleViewModel
{
    public required string Side { get; init; }

    public required VehicleIdentity Identity { get; init; }

    public required VehicleProfile Profile { get; init; }

    public required CompareRecallSummary Recalls { get; init; }

    public string DisplayName => $"{Identity.Make} {Identity.Model} {Identity.ModelYear}";

    public string ProfilePath => string.Create(
        CultureInfo.InvariantCulture,
        $"/vehicles/{Uri.EscapeDataString(Identity.MakeSlug)}/{Uri.EscapeDataString(Identity.ModelSlug)}/{Identity.ModelYear}");

    public int TotalComplaints => Profile.TotalComplaints;

    public int WithMileage => Profile.WithMileage;

    public string TotalLabel => Profile.TotalComplaints.ToString("N0");

    public string WithMileageLabel => Profile.WithMileage.ToString("N0");

    public string MileageShareLabel => Profile.TotalComplaints == 0
        ? "n/a"
        : (Profile.WithMileage * 100d / Profile.TotalComplaints).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public string AverageMileageLabel => Profile.AverageMilesAtFailure is { } average
        ? average.ToString("N0") + " mi"
        : "not recorded";

    public string ComponentGroupsLabel => Profile.Components.Count.ToString("N0");

    public int? FirstReportYear => Profile.Timeline.Count == 0 ? null : Profile.Timeline[0].Year;

    public int? LastReportYear => Profile.Timeline.Count == 0 ? null : Profile.Timeline[^1].Year;

    public string ReportSpanLabel => (FirstReportYear, LastReportYear) switch
    {
        (null, _) or (_, null) => "not dated",
        var (first, last) when first == last =>
            string.Create(CultureInfo.InvariantCulture, $"{first} only"),
        var (first, last) =>
            string.Create(CultureInfo.InvariantCulture, $"{first} to {last}")
    };

    public bool HasSmallSample => Profile.TotalComplaints < ComparePageViewModel.SmallSampleThreshold;

    public bool HasThinMileageSample => Profile.WithMileage < ComparePageViewModel.ThinMileageThreshold;

    public string SampleLine => string.Create(
        CultureInfo.InvariantCulture,
        $"{Profile.TotalComplaints:N0} reports, {Profile.WithMileage:N0} with a mileage");
}

public sealed record CompareCell
{
    public required int Count { get; init; }

    public required int Sample { get; init; }

    public required double Share { get; init; }

    public required int WidthPercent { get; init; }

    public bool HasSample => Sample > 0;

    public string CountLabel => Count.ToString("N0");

    public string ShareLabel => HasSample
        ? Share.ToString("0.0", CultureInfo.InvariantCulture) + "%"
        : "n/a";
}

public sealed record CompareMetricRow
{
    public required string Label { get; init; }

    public required CompareCell First { get; init; }

    public required CompareCell Second { get; init; }

    public bool IsRemainder { get; init; }
}

public sealed record CompareSeverityCell
{
    public required int Count { get; init; }

    public required int Sample { get; init; }

    public string CountLabel => Count.ToString("N0");

    public string RateLabel => Sample == 0
        ? "n/a"
        : (Count * 100d / Sample).ToString("0.0", CultureInfo.InvariantCulture);
}

public sealed record CompareSeverityRow
{
    public required string Label { get; init; }

    public required CompareSeverityCell First { get; init; }

    public required CompareSeverityCell Second { get; init; }

    public bool Critical { get; init; }

    public bool Highlighted => Critical && (First.Count > 0 || Second.Count > 0);
}

public sealed record CompareFigureRow(string Label, string First, string Second);

public sealed record CompareResultViewModel
{
    private const int TopGroupsPerSide = 8;

    public required CompareVehicleViewModel First { get; init; }

    public required CompareVehicleViewModel Second { get; init; }

    public required IReadOnlyList<CompareFigureRow> Figures { get; init; }

    public required IReadOnlyList<CompareMetricRow> ComponentRows { get; init; }

    public required IReadOnlyList<CompareMetricRow> MileageRows { get; init; }

    public required IReadOnlyList<CompareSeverityRow> SeverityRows { get; init; }

    public string SwapPath => ComparePaths.ForKeys(Second.Identity.VehicleKey, First.Identity.VehicleKey);

    public CompareVehicleViewModel Smaller => First.TotalComplaints <= Second.TotalComplaints ? First : Second;

    public CompareVehicleViewModel Larger => First.TotalComplaints <= Second.TotalComplaints ? Second : First;

    public bool IsLopsided => Smaller.TotalComplaints > 0
        && Larger.TotalComplaints >= Smaller.TotalComplaints * ComparePageViewModel.LopsidedFactor;

    public string VolumeRatioLabel => Smaller.TotalComplaints == 0
        ? "many"
        : (Larger.TotalComplaints / (double)Smaller.TotalComplaints).ToString("0", CultureInfo.InvariantCulture);

    public bool EitherHasSmallSample => First.HasSmallSample || Second.HasSmallSample;

    public bool HasMileageSample => First.WithMileage > 0 || Second.WithMileage > 0;

    public bool EitherHasThinMileageSample => First.HasThinMileageSample || Second.HasThinMileageSample;

    public bool HasUrgentRecall => First.Recalls.HasUrgent || Second.Recalls.HasUrgent;

    public bool HasAnyRecall => First.Recalls.HasCampaigns || Second.Recalls.HasCampaigns;

    public bool IsSameVehicle => string.Equals(
        First.Identity.VehicleKey,
        Second.Identity.VehicleKey,
        StringComparison.Ordinal);

    public static CompareResultViewModel Build(CompareVehicleViewModel first, CompareVehicleViewModel second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return new CompareResultViewModel
        {
            First = first,
            Second = second,
            Figures = BuildFigures(first, second),
            ComponentRows = BuildComponentRows(first.Profile, second.Profile),
            MileageRows = BuildMileageRows(first.Profile, second.Profile),
            SeverityRows = BuildSeverityRows(first.Profile, second.Profile)
        };
    }

    private static IReadOnlyList<CompareFigureRow> BuildFigures(
        CompareVehicleViewModel first,
        CompareVehicleViewModel second) =>
    [
        new("Complaints on record", first.TotalLabel, second.TotalLabel),
        new("Of those, with a mileage", first.WithMileageLabel, second.WithMileageLabel),
        new("Share carrying a mileage", first.MileageShareLabel, second.MileageShareLabel),
        new("Average miles at failure", first.AverageMileageLabel, second.AverageMileageLabel),
        new("Component groups touched", first.ComponentGroupsLabel, second.ComponentGroupsLabel),
        new("Reports received", first.ReportSpanLabel, second.ReportSpanLabel)
    ];

    private static IReadOnlyList<CompareMetricRow> BuildComponentRows(VehicleProfile first, VehicleProfile second)
    {
        var firstCounts = ComponentCounts(first);
        var secondCounts = ComponentCounts(second);

        var union = TopGroups(firstCounts)
            .Union(TopGroups(secondCounts), StringComparer.Ordinal)
            .Select(group => new
            {
                Group = group,
                FirstCount = firstCounts.GetValueOrDefault(group),
                SecondCount = secondCounts.GetValueOrDefault(group)
            })
            .OrderByDescending(entry =>
                Share(entry.FirstCount, first.TotalComplaints) + Share(entry.SecondCount, second.TotalComplaints))
            .ThenBy(entry => entry.Group, StringComparer.Ordinal)
            .ToList();

        var firstRemainder = firstCounts.Values.Sum() - union.Sum(entry => entry.FirstCount);
        var secondRemainder = secondCounts.Values.Sum() - union.Sum(entry => entry.SecondCount);

        var scale = union
            .SelectMany(entry => new[]
            {
                Share(entry.FirstCount, first.TotalComplaints),
                Share(entry.SecondCount, second.TotalComplaints)
            })
            .Append(Share(firstRemainder, first.TotalComplaints))
            .Append(Share(secondRemainder, second.TotalComplaints))
            .DefaultIfEmpty(0d)
            .Max();

        var rows = union
            .Select(entry => new CompareMetricRow
            {
                Label = entry.Group,
                First = Cell(entry.FirstCount, first.TotalComplaints, scale),
                Second = Cell(entry.SecondCount, second.TotalComplaints, scale)
            })
            .ToList();

        if (firstRemainder > 0 || secondRemainder > 0)
        {
            rows.Add(new CompareMetricRow
            {
                Label = "All other groups",
                First = Cell(firstRemainder, first.TotalComplaints, scale),
                Second = Cell(secondRemainder, second.TotalComplaints, scale),
                IsRemainder = true
            });
        }

        return rows;
    }

    private static IReadOnlyList<CompareMetricRow> BuildMileageRows(VehicleProfile first, VehicleProfile second)
    {
        var firstCounts = BucketCounts(first);
        var secondCounts = BucketCounts(second);
        var buckets = MileageBuckets.Empty();

        var scale = buckets
            .SelectMany(bucket => new[]
            {
                Share(firstCounts.GetValueOrDefault(bucket.From), first.WithMileage),
                Share(secondCounts.GetValueOrDefault(bucket.From), second.WithMileage)
            })
            .DefaultIfEmpty(0d)
            .Max();

        return buckets
            .Select(bucket => new CompareMetricRow
            {
                Label = BucketLabel(bucket),
                First = Cell(firstCounts.GetValueOrDefault(bucket.From), first.WithMileage, scale),
                Second = Cell(secondCounts.GetValueOrDefault(bucket.From), second.WithMileage, scale)
            })
            .ToList();
    }

    private static IReadOnlyList<CompareSeverityRow> BuildSeverityRows(VehicleProfile first, VehicleProfile second) =>
    [
        SeverityRow("Crashes", first, second, signals => signals.Crashes, critical: false),
        SeverityRow("Fires", first, second, signals => signals.Fires, critical: true),
        SeverityRow("People injured", first, second, signals => signals.Injured, critical: true),
        SeverityRow("Deaths", first, second, signals => signals.Deaths, critical: true),
        SeverityRow("Vehicles towed", first, second, signals => signals.VehiclesTowed, critical: false),
        SeverityRow("Medical attention", first, second, signals => signals.MedicalAttention, critical: false),
        SeverityRow("Police reports", first, second, signals => signals.PoliceReports, critical: false)
    ];

    private static CompareSeverityRow SeverityRow(
        string label,
        VehicleProfile first,
        VehicleProfile second,
        Func<SeveritySignals, int> select,
        bool critical) =>
        new()
        {
            Label = label,
            First = new CompareSeverityCell { Count = select(first.Severity), Sample = first.TotalComplaints },
            Second = new CompareSeverityCell { Count = select(second.Severity), Sample = second.TotalComplaints },
            Critical = critical
        };

    private static Dictionary<string, int> ComponentCounts(VehicleProfile profile) =>
        profile.Components
            .GroupBy(component => component.Group, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(component => component.Count), StringComparer.Ordinal);

    private static Dictionary<int, int> BucketCounts(VehicleProfile profile)
    {
        var counts = new Dictionary<int, int>();

        foreach (var bucket in profile.MileageHistogram)
        {
            counts[bucket.From] = counts.GetValueOrDefault(bucket.From) + bucket.Count;
        }

        return counts;
    }

    private static IEnumerable<string> TopGroups(Dictionary<string, int> counts) =>
        counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(TopGroupsPerSide)
            .Select(entry => entry.Key);

    private static CompareCell Cell(int count, int sample, double scale) =>
        new()
        {
            Count = count,
            Sample = sample,
            Share = Share(count, sample),
            WidthPercent = scale <= 0
                ? 0
                : (int)Math.Round(Share(count, sample) * 100d / scale, MidpointRounding.AwayFromZero)
        };

    private static double Share(int count, int sample) => sample <= 0 ? 0d : count * 100d / sample;

    private static string BucketLabel(MileageBucket bucket) => bucket.To is { } upper
        ? string.Create(CultureInfo.InvariantCulture, $"{bucket.From / 1000:N0}–{upper / 1000:N0}k")
        : string.Create(CultureInfo.InvariantCulture, $"{bucket.From / 1000:N0}k+");
}

public sealed record CompareSideViewModel
{
    public required string Side { get; init; }

    public required string Label { get; init; }

    public required CompareSideStatus Status { get; init; }

    public required CompareSidePicker Picker { get; init; }

    public string? RequestedKey { get; init; }

    public CompareVehicleViewModel? Vehicle { get; init; }

    public bool IsResolved => Status == CompareSideStatus.Resolved;

    public bool HasProblem => Status is CompareSideStatus.Unknown or CompareSideStatus.Missing;

    public string RequestedKeyLabel => RequestedKey ?? "nothing";
}

public sealed record ComparePageViewModel
{
    public const int SmallSampleThreshold = 25;

    public const int ThinMileageThreshold = 20;

    public const int LopsidedFactor = 10;

    public required CompareSideViewModel First { get; init; }

    public required CompareSideViewModel Second { get; init; }

    public CompareResultViewModel? Result { get; init; }

    public bool HasResult => Result is not null;

    public CompareVehicleViewModel? LoneVehicle => Result is not null
        ? null
        : First.Vehicle ?? Second.Vehicle;

    public CompareSideViewModel PendingSide => First.Vehicle is null ? First : Second;

    public bool HasProblem => First.HasProblem || Second.HasProblem;

    public string Title => (First.Vehicle, Second.Vehicle) switch
    {
        ({ } first, { } second) => $"{first.DisplayName} and {second.DisplayName}",
        ({ } only, null) => $"{only.DisplayName}, one side still empty",
        (null, { } only) => $"{only.DisplayName}, one side still empty",
        _ => "Compare two vehicles"
    };
}
