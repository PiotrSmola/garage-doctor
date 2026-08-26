using GarageDoctor.Domain.Models;

namespace GarageDoctor.Infrastructure.Queries;

public enum SearchMode
{
    None,
    FullText,
    ScopedPhrase
}

public sealed record ComplaintSearchRequest
{
    public string? Term { get; init; }

    public string? MakeSlug { get; init; }

    public string? ComponentGroup { get; init; }

    public int? YearFrom { get; init; }

    public int? YearTo { get; init; }

    public bool MileageOnly { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(MakeSlug)
        || !string.IsNullOrWhiteSpace(ComponentGroup)
        || YearFrom is not null
        || YearTo is not null
        || MileageOnly;
}

public sealed record ComplaintSearchHit(
    int Id,
    string Make,
    string MakeSlug,
    string Model,
    string ModelSlug,
    int? ModelYear,
    string VehicleKey,
    DateOnly ReceivedDate,
    int? MilesAtFailure,
    string ComponentGroup,
    string ComponentRaw,
    string Description,
    bool Crash,
    bool Fire,
    int Injured,
    int Deaths);

public sealed record ComplaintSearchResult(
    IReadOnlyList<ComplaintSearchHit> Hits,
    long MatchCount,
    bool MatchCountIsExact,
    SearchMode Mode,
    int Page,
    int PageSize)
{
    public int LastPage => PageSize <= 0 ? 1 : (int)Math.Max(1, Math.Ceiling(MatchCount / (double)PageSize));

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Hits.Count == PageSize;
}

public interface ISearchQueries
{
    Task<ComplaintSearchResult> SearchAsync(
        ComplaintSearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ComponentVehicleRanking(
    string VehicleKey,
    string Make,
    string MakeSlug,
    string Model,
    string ModelSlug,
    int ModelYear,
    int GroupComplaintCount,
    int TotalComplaints)
{
    public double ShareOfVehicleComplaints => TotalComplaints <= 0
        ? 0
        : GroupComplaintCount * 100d / TotalComplaints;
}

public sealed record ComponentDetail(
    string Group,
    string Slug,
    int ComplaintCount,
    IReadOnlyList<string> RawTopLevels,
    IReadOnlyList<MakeSummary> TopMakes,
    IReadOnlyList<ComponentVehicleRanking> TopVehicles,
    IReadOnlyList<MileageBucket> MileageHistogram,
    int WithMileage);

public interface IComponentQueries
{
    Task<IReadOnlyList<ComponentGroupSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ComponentDetail?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
