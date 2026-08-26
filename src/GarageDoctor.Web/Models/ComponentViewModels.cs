using System.Globalization;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Web.Models;

public sealed record ComponentGroupRow(
    string Group,
    string Slug,
    int ComplaintCount,
    long TotalComplaints,
    int LeaderCount)
{
    public string ShareLabel => TotalComplaints <= 0
        ? "0.0"
        : (ComplaintCount * 100d / TotalComplaints).ToString("0.0", CultureInfo.InvariantCulture);

    public int WidthPercent => LeaderCount <= 0
        ? 0
        : (int)Math.Round(ComplaintCount * 100d / LeaderCount, MidpointRounding.AwayFromZero);
}

public sealed record ComponentsIndexViewModel
{
    public const int LeadingGroupCount = 5;

    public required IReadOnlyList<ComponentGroupRow> Groups { get; init; }

    public required long TotalComplaints { get; init; }

    public int GroupCount => Groups.Count;

    public string LeadingShareLabel
    {
        get
        {
            if (TotalComplaints <= 0)
            {
                return "0";
            }

            var leading = Groups.Take(LeadingGroupCount).Sum(group => (long)group.ComplaintCount);
            return Math.Round(leading * 100d / TotalComplaints, MidpointRounding.AwayFromZero)
                .ToString("F0", CultureInfo.InvariantCulture);
        }
    }
}

public sealed record ComponentMakeRow(
    string Make,
    string MakeSlug,
    long ComplaintCount,
    long LeaderCount,
    int GroupTotal)
{
    public int WidthPercent => LeaderCount <= 0
        ? 0
        : (int)Math.Round(ComplaintCount * 100d / LeaderCount, MidpointRounding.AwayFromZero);

    public string ShareLabel => GroupTotal <= 0
        ? "0.0"
        : (ComplaintCount * 100d / GroupTotal).ToString("0.0", CultureInfo.InvariantCulture);
}

public sealed record ComponentVehicleRow(
    string Make,
    string MakeSlug,
    string Model,
    string ModelSlug,
    int ModelYear,
    int ComplaintCount,
    int LeaderCount,
    int GroupTotal)
{
    public string DisplayName => string.Create(CultureInfo.InvariantCulture, $"{ModelYear} {Make} {Model}");

    public int WidthPercent => LeaderCount <= 0
        ? 0
        : (int)Math.Round(ComplaintCount * 100d / LeaderCount, MidpointRounding.AwayFromZero);

    public string ShareLabel => GroupTotal <= 0
        ? "0.00"
        : (ComplaintCount * 100d / GroupTotal).ToString("0.00", CultureInfo.InvariantCulture);
}

public sealed record ComponentDetailViewModel
{
    public const int ThinSampleThreshold = 200;

    public const int HighMileageStart = 100_000;

    public required string Group { get; init; }

    public required string Slug { get; init; }

    public required int ComplaintCount { get; init; }

    public required IReadOnlyList<string> RawTopLevels { get; init; }

    public required IReadOnlyList<ComponentMakeRow> Makes { get; init; }

    public required IReadOnlyList<ComponentVehicleRow> Vehicles { get; init; }

    public required IReadOnlyList<MileageBucketViewModel> Buckets { get; init; }

    public required int WithMileage { get; init; }

    public required string PeakBucketLabel { get; init; }

    public required double EarlyShare { get; init; }

    public required double HighMileageShare { get; init; }

    public bool HasRawTopLevels => RawTopLevels.Count > 0;

    public bool IsMerged => RawTopLevels.Count > 1;

    public bool HasMakes => Makes.Count > 0;

    public bool HasVehicles => Vehicles.Count > 0;

    public bool HasSample => WithMileage > 0;

    public bool HasThinSample => WithMileage is > 0 and < ThinSampleThreshold;

    public string SearchPath => $"/search?componentGroup={Uri.EscapeDataString(Group)}";

    public string SampleLine => string.Create(
        CultureInfo.InvariantCulture,
        $"Based on {WithMileage:N0} of {ComplaintCount:N0} reports");

    public string MileageCoverageLabel => ComplaintCount <= 0
        ? "0"
        : Math.Round(WithMileage * 100d / ComplaintCount, MidpointRounding.AwayFromZero)
            .ToString("F0", CultureInfo.InvariantCulture);

    public string EarlyShareLabel => EarlyShare.ToString("F0", CultureInfo.InvariantCulture);

    public string HighMileageShareLabel => HighMileageShare.ToString("F0", CultureInfo.InvariantCulture);

    public string ShapeLine => (EarlyShare, HighMileageShare) switch
    {
        ( >= 30, _) => $"The weight sits at the near end. {EarlyShareLabel}% of the reports that carry an odometer "
            + "reading arrive before 25,000 miles, which is the shape of a part that fails while the car is still "
            + "close to new rather than one that wears out on the road.",
        (_, >= 30) => $"The weight sits at the far end. {HighMileageShareLabel}% of the reports that carry an "
            + "odometer reading arrive after 100,000 miles, which is the shape of a part that wears out rather "
            + "than one that arrives broken.",
        _ => $"The distribution is broad, peaking at {PeakBucketLabel}, with {EarlyShareLabel}% of reports before "
            + $"25,000 miles and {HighMileageShareLabel}% past 100,000. Nothing in the shape points clearly at "
            + "either a warranty-period defect or a part that simply wears out."
    };

    public static ComponentDetailViewModel From(ComponentDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var makeLeader = detail.TopMakes.Count == 0 ? 0 : detail.TopMakes.Max(make => make.ComplaintCount);
        var vehicleLeader = detail.TopVehicles.Count == 0
            ? 0
            : detail.TopVehicles.Max(vehicle => vehicle.TotalComplaints);

        var counts = detail.MileageHistogram.ToDictionary(bucket => bucket.From, bucket => bucket.Count);
        var scale = counts.Count == 0 ? 0 : counts.Values.Max();

        var buckets = MileageBuckets.Empty()
            .Select(bucket => BuildBucket(bucket.From, bucket.To, counts.GetValueOrDefault(bucket.From), scale, detail.WithMileage))
            .ToList();

        var early = detail.WithMileage == 0
            ? 0d
            : counts.GetValueOrDefault(0) * 100d / detail.WithMileage;

        var high = detail.WithMileage == 0
            ? 0d
            : detail.MileageHistogram
                .Where(bucket => bucket.From >= HighMileageStart)
                .Sum(bucket => bucket.Count) * 100d / detail.WithMileage;

        var peak = buckets.Count == 0 ? string.Empty : buckets.MaxBy(bucket => bucket.Count)!.Label;

        return new ComponentDetailViewModel
        {
            Group = detail.Group,
            Slug = detail.Slug,
            ComplaintCount = detail.ComplaintCount,
            RawTopLevels = detail.RawTopLevels.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            Makes = detail.TopMakes
                .Select(make => new ComponentMakeRow(
                    make.Make,
                    make.MakeSlug,
                    make.ComplaintCount,
                    makeLeader,
                    detail.ComplaintCount))
                .ToList(),
            Vehicles = detail.TopVehicles
                .Select(vehicle => new ComponentVehicleRow(
                    vehicle.Make,
                    vehicle.MakeSlug,
                    vehicle.Model,
                    vehicle.ModelSlug,
                    vehicle.ModelYear,
                    vehicle.TotalComplaints,
                    vehicleLeader,
                    detail.ComplaintCount))
                .ToList(),
            Buckets = buckets,
            WithMileage = detail.WithMileage,
            PeakBucketLabel = peak,
            EarlyShare = early,
            HighMileageShare = high
        };
    }

    private static MileageBucketViewModel BuildBucket(int from, int? to, int count, int scale, int sample) =>
        new()
        {
            Label = BucketLabel(from, to),
            Count = count,
            WidthPercent = scale == 0
                ? 0
                : (int)Math.Round(count * 100d / scale, MidpointRounding.AwayFromZero),
            ShareLabel = sample == 0
                ? "0%"
                : $"{Math.Round(count * 100d / sample, MidpointRounding.AwayFromZero):F0}%"
        };

    private static string BucketLabel(int from, int? to) => to is { } upper
        ? string.Create(CultureInfo.InvariantCulture, $"{from / 1000:N0}–{upper / 1000:N0}k")
        : string.Create(CultureInfo.InvariantCulture, $"{from / 1000:N0}k+");
}
