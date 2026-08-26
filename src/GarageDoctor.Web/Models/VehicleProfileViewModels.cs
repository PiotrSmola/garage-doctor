using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Web.Models;

public sealed record VehicleProfilePageViewModel
{
    public const int SmallSampleThreshold = 25;

    public required VehicleIdentity Vehicle { get; init; }

    public required VehicleProfile Profile { get; init; }

    public required IReadOnlyList<RecentComplaintViewModel> RecentComplaints { get; init; }

    public string DisplayName => $"{Vehicle.Make} {Vehicle.Model} {Vehicle.ModelYear}";

    public int? FirstReportYear => Profile.Timeline.Count == 0 ? null : Profile.Timeline[0].Year;

    public int? LastReportYear => Profile.Timeline.Count == 0 ? null : Profile.Timeline[^1].Year;

    public bool HasSmallSample => Profile.TotalComplaints < SmallSampleThreshold;

    public int ComponentGroupCount => Profile.Components.Count;

    public string ReportSpan => (FirstReportYear, LastReportYear) switch
    {
        (null, _) or (_, null) => "no report carries a usable date",
        var (first, last) when first == last => $"all received in {first}",
        var (first, last) => $"received between {first} and {last}"
    };

    public string AverageMileageLabel => Profile.AverageMilesAtFailure is { } average
        ? average.ToString("N0")
        : "n/a";
}

public sealed record RecentComplaintViewModel
{
    public const int NarrativeLimit = 460;

    public required DateOnly ReceivedDate { get; init; }

    public required int? MilesAtFailure { get; init; }

    public required string ComponentGroup { get; init; }

    public required string ComponentDetail { get; init; }

    public required string Narrative { get; init; }

    public required bool Shortened { get; init; }

    public required IReadOnlyList<string> Outcomes { get; init; }

    public required bool Severe { get; init; }

    public required string DetailLine { get; init; }

    public string MileageLabel => MilesAtFailure is { } miles
        ? $"{miles:N0} mi"
        : "mileage not reported";

    public bool HasDetailLine => DetailLine.Length > 0;

    public static RecentComplaintViewModel From(ComplaintSummary complaint)
    {
        ArgumentNullException.ThrowIfNull(complaint);

        var (narrative, shortened) = Shorten(complaint.Description);
        var detail = complaint.ComponentRaw.Replace(":", " / ", StringComparison.Ordinal);

        return new RecentComplaintViewModel
        {
            ReceivedDate = complaint.ReceivedDate,
            MilesAtFailure = complaint.MilesAtFailure,
            ComponentGroup = complaint.ComponentGroup,
            ComponentDetail = detail,
            Narrative = narrative,
            Shortened = shortened,
            Outcomes = DescribeOutcomes(complaint),
            Severe = complaint.Fire || complaint.Deaths > 0,
            DetailLine = BuildDetailLine(detail, complaint.ComponentGroup, shortened)
        };
    }

    private static string BuildDetailLine(string detail, string group, bool shortened)
    {
        var parts = new List<string>(2);

        if (!string.Equals(detail, group, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(detail);
        }

        if (shortened)
        {
            parts.Add("narrative shortened for this page");
        }

        return string.Join(" · ", parts);
    }

    private static IReadOnlyList<string> DescribeOutcomes(ComplaintSummary complaint)
    {
        var outcomes = new List<string>(4);

        if (complaint.Crash)
        {
            outcomes.Add("crash reported");
        }

        if (complaint.Fire)
        {
            outcomes.Add("fire reported");
        }

        if (complaint.Injured > 0)
        {
            outcomes.Add($"{complaint.Injured} injured");
        }

        if (complaint.Deaths > 0)
        {
            outcomes.Add($"{complaint.Deaths} killed");
        }

        return outcomes;
    }

    private static (string Text, bool Shortened) Shorten(string description)
    {
        var collapsed = string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length <= NarrativeLimit)
        {
            return (collapsed, false);
        }

        var cut = collapsed.LastIndexOf(' ', NarrativeLimit);
        var keep = cut > NarrativeLimit / 2 ? cut : NarrativeLimit;

        return (string.Concat(collapsed.AsSpan(0, keep), "…"), true);
    }
}

public sealed record SeveritySignalViewModel
{
    public required string Label { get; init; }

    public required int Count { get; init; }

    public required bool Critical { get; init; }

    public bool Highlighted => Critical && Count > 0;
}

public sealed record SeveritySignalsViewModel
{
    public required IReadOnlyList<SeveritySignalViewModel> Signals { get; init; }

    public required int TotalComplaints { get; init; }

    public required int Deaths { get; init; }

    public required int Fires { get; init; }

    public required string SevereSummary { get; init; }

    public bool HasSevereOutcome => Deaths > 0 || Fires > 0;
}

public sealed record MileageBucketViewModel
{
    public required string Label { get; init; }

    public required int Count { get; init; }

    public required int WidthPercent { get; init; }

    public required string ShareLabel { get; init; }
}

public sealed record MileageHistogramViewModel
{
    public const int ThinSampleThreshold = 20;

    public required IReadOnlyList<MileageBucketViewModel> Buckets { get; init; }

    public required int WithMileage { get; init; }

    public required int TotalComplaints { get; init; }

    public required int? AverageMilesAtFailure { get; init; }

    public bool HasSample => WithMileage > 0;

    public bool HasThinSample => WithMileage is > 0 and < ThinSampleThreshold;

    public string SampleLine => AverageMilesAtFailure is { } average
        ? $"Based on {WithMileage:N0} of {TotalComplaints:N0} reports, average {average:N0} miles"
        : $"Based on {WithMileage:N0} of {TotalComplaints:N0} reports";
}

public sealed record ComponentShareViewModel
{
    public required string Group { get; init; }

    public required int Count { get; init; }

    public required int WidthPercent { get; init; }

    public required string ShareLabel { get; init; }
}

public sealed record ComponentBreakdownViewModel
{
    public required IReadOnlyList<ComponentShareViewModel> Groups { get; init; }

    public required int TotalComplaints { get; init; }

    public required int Classified { get; init; }

    public bool HasGroups => Groups.Count > 0;
}

public sealed record TimelinePointViewModel
{
    public required int Year { get; init; }

    public required int Count { get; init; }

    public required int WidthPercent { get; init; }
}

public sealed record TimelineChartPayload
{
    public required IReadOnlyList<int> Years { get; init; }

    public required IReadOnlyList<int> Counts { get; init; }
}

public sealed record ComplaintTimelineViewModel
{
    public required IReadOnlyList<TimelinePointViewModel> Points { get; init; }

    public required int TotalComplaints { get; init; }

    public required string ChartPayload { get; init; }

    public required int? PeakYear { get; init; }

    public required int? PeakCount { get; init; }

    public required bool LastYearIsPartial { get; init; }

    public bool HasPoints => Points.Count > 0;

    public string PeakLine => PeakYear is { } year && PeakCount is { } count
        ? $"{TotalComplaints:N0} reports, busiest year {year} with {count:N0}"
        : $"{TotalComplaints:N0} reports";
}

public sealed record RecallCampaignRowViewModel
{
    public required string CampaignNumber { get; init; }

    public required string ComponentGroup { get; init; }

    public required IReadOnlyList<string> ComponentNames { get; init; }

    public required string DefectDescription { get; init; }

    public required string Consequence { get; init; }

    public required string CorrectiveAction { get; init; }

    public required DateOnly? ReportReceivedDate { get; init; }

    public required int? PotentiallyAffected { get; init; }

    public required bool DoNotDrive { get; init; }

    public required bool ParkOutside { get; init; }

    public bool Urgent => DoNotDrive || ParkOutside;

    public string UrgencyLabel => (DoNotDrive, ParkOutside) switch
    {
        (true, true) => "Do not drive, and park away from buildings",
        (true, false) => "Do not drive this vehicle",
        (false, true) => "Park outside, away from buildings",
        _ => string.Empty
    };

    public string ReceivedLabel => ReportReceivedDate is { } received
        ? received.ToString("yyyy-MM-dd")
        : "date not reported";

    public string AffectedLabel => PotentiallyAffected is { } affected
        ? affected.ToString("N0")
        : "not reported";
}

public sealed record VehicleRecallsViewModel
{
    public required IReadOnlyList<RecallCampaignRowViewModel> Campaigns { get; init; }

    public required IReadOnlyList<RecallCampaignRowViewModel> UrgentCampaigns { get; init; }

    public bool HasCampaigns => Campaigns.Count > 0;
}
