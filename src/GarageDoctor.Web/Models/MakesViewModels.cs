using System.Globalization;

namespace GarageDoctor.Web.Models;

public sealed record MakeListing(string Slug, string Name, long ComplaintCount, int ModelCount, long TotalComplaints)
{
    public string Share => TotalComplaints <= 0
        ? "0.0"
        : ((ComplaintCount * 100d) / TotalComplaints).ToString("0.0", CultureInfo.InvariantCulture);
}

public sealed record MakeLink(string Slug, string Name);

public sealed record MakesIndexViewModel
{
    public required IReadOnlyList<MakeListing> Leaders { get; init; }

    public required IReadOnlyList<MakeLink> Tail { get; init; }

    public required long LeaderThreshold { get; init; }

    public required int TotalMakes { get; init; }

    public required long TotalComplaints { get; init; }

    public required int SingleFigureMakes { get; init; }

    public long LeaderComplaints => Leaders.Sum(leader => leader.ComplaintCount);

    public string LeaderShare => TotalComplaints <= 0
        ? "0.0"
        : ((LeaderComplaints * 100d) / TotalComplaints).ToString("0.0", CultureInfo.InvariantCulture);
}

public sealed record ModelListing(
    string Slug,
    string Name,
    long ComplaintCount,
    int EarliestModelYear,
    int LatestModelYear,
    long MakeComplaintCount)
{
    public string Share => MakeComplaintCount <= 0
        ? "0.0"
        : ((ComplaintCount * 100d) / MakeComplaintCount).ToString("0.0", CultureInfo.InvariantCulture);

    public string YearSpan => EarliestModelYear == LatestModelYear
        ? EarliestModelYear.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{EarliestModelYear}–{LatestModelYear}");
}

public sealed record MakeDetailsViewModel
{
    public required string Make { get; init; }

    public required string MakeSlug { get; init; }

    public required long ComplaintCount { get; init; }

    public required IReadOnlyList<ModelListing> Models { get; init; }

    public required int EarliestModelYear { get; init; }

    public required int LatestModelYear { get; init; }

    public string YearSpan => EarliestModelYear == LatestModelYear
        ? EarliestModelYear.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{EarliestModelYear}–{LatestModelYear}");
}
