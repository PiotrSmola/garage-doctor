using System.Globalization;
using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Web.Models;

public sealed record CascadeMake(string Slug, string Name);

public sealed record CascadeModel(string Slug, string Name, long ComplaintCount);

public sealed record CascadeYear(int ModelYear, long ComplaintCount);

public sealed record MakeRanking(string Slug, string Name, long ComplaintCount, long LeaderComplaintCount)
{
    public string BarWidth => LeaderComplaintCount <= 0
        ? "0%"
        : ((ComplaintCount * 100d) / LeaderComplaintCount).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}

public sealed record SearchCascade
{
    public required IReadOnlyList<CascadeMake> Makes { get; init; }

    public IReadOnlyList<CascadeMake> FrequentMakes { get; init; } = [];

    public IReadOnlyList<CascadeModel> Models { get; init; } = [];

    public IReadOnlyList<CascadeYear> Years { get; init; } = [];

    public string? SelectedMakeSlug { get; init; }

    public string? SelectedMakeName { get; init; }

    public string? SelectedModelSlug { get; init; }

    public string? SelectedModelName { get; init; }

    public string? UnresolvedMake { get; init; }

    public bool ModelStepReady => Models.Count > 0;

    public bool YearStepReady => Years.Count > 0;
}

public sealed record ComponentRanking(string Group, string Slug, int ComplaintCount, int LeaderComplaintCount, long DatasetComplaints)
{
    public string BarWidth => LeaderComplaintCount <= 0
        ? "0%"
        : ((ComplaintCount * 100d) / LeaderComplaintCount).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public string DatasetShare => DatasetComplaints <= 0
        ? "0%"
        : ((ComplaintCount * 100d) / DatasetComplaints).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}

public sealed record HomeIndexViewModel
{
    public required CatalogOverview Overview { get; init; }

    public required SearchCascade Cascade { get; init; }

    public required IReadOnlyList<MakeRanking> TopMakes { get; init; }

    public required IReadOnlyList<VehicleRanking> TopVehicles { get; init; }

    public required IReadOnlyList<ComponentRanking> TopComponents { get; init; }

    public required IReadOnlyList<ConsumerAdvisory> Advisories { get; init; }

    public string MileageShare => Overview.Complaints <= 0
        ? "0%"
        : ((Overview.ComplaintsWithMileage * 100d) / Overview.Complaints).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public string ModelYearSpan => Overview is { EarliestModelYear: { } earliest, LatestModelYear: { } latest }
        ? string.Create(CultureInfo.InvariantCulture, $"{earliest}–{latest}")
        : "not recorded";
}
