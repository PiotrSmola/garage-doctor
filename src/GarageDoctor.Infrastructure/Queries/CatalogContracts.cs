using GarageDoctor.Domain.Models;

namespace GarageDoctor.Infrastructure.Queries;

public sealed record CatalogOverview(
    long Complaints,
    long Recalls,
    long VehicleCombinations,
    long ComplaintsInCatalogue,
    int Makes,
    int CanonicalMakes,
    long ComplaintsWithMileage,
    int? EarliestModelYear,
    int? LatestModelYear);

public sealed record MakeSummary(string Make, string MakeSlug, long ComplaintCount, int ModelCount);

public sealed record ModelSummary(
    string Model,
    string ModelSlug,
    long ComplaintCount,
    int EarliestModelYear,
    int LatestModelYear);

public sealed record MakeDetail(string Make, string MakeSlug, long ComplaintCount, IReadOnlyList<ModelSummary> Models);

public sealed record ModelYearSummary(int ModelYear, string VehicleKey, long ComplaintCount);

public sealed record VehicleIdentity(
    string VehicleKey,
    string Make,
    string MakeSlug,
    string Model,
    string ModelSlug,
    int ModelYear);

public sealed record VehicleRanking(
    string VehicleKey,
    string Make,
    string MakeSlug,
    string Model,
    string ModelSlug,
    int ModelYear,
    int TotalComplaints,
    int WithMileage);

public sealed record ComponentGroupSummary(string Group, string Slug, int ComplaintCount);

public sealed record ConsumerAdvisory(
    string CampaignNumber,
    string Make,
    string ComponentName,
    bool DoNotDrive,
    bool ParkOutside,
    DateOnly? ReportReceivedDate,
    int VehicleCount);

public sealed record ComplaintSummary(
    int Id,
    DateOnly ReceivedDate,
    int? MilesAtFailure,
    string ComponentGroup,
    string ComponentRaw,
    string Description,
    bool Crash,
    bool Fire,
    int Injured,
    int Deaths);

public sealed record RecalledVehicle(
    string Make,
    string Model,
    int? ModelYear,
    string VehicleKey,
    string MakeSlug,
    string ModelSlug);

public sealed record RecallCampaignDetail(
    string CampaignNumber,
    string Manufacturer,
    string ComponentName,
    string ComponentGroup,
    string DefectDescription,
    string Consequence,
    string CorrectiveAction,
    bool DoNotDrive,
    bool ParkOutside,
    int? PotentiallyAffected,
    DateOnly? ReportReceivedDate,
    DateOnly? OwnersNotifiedDate,
    IReadOnlyList<RecalledVehicle> Vehicles,
    int TotalVehicleCount);

public interface IVehicleCatalogQueries
{
    Task<CatalogOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MakeSummary>> GetMakesAsync(CancellationToken cancellationToken = default);

    Task<MakeDetail?> GetMakeAsync(string makeSlug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelYearSummary>> GetModelYearsAsync(
        string makeSlug,
        string modelSlug,
        CancellationToken cancellationToken = default);

    Task<VehicleIdentity?> ResolveVehicleAsync(
        string makeSlug,
        string modelSlug,
        int modelYear,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VehicleRanking>> GetMostReportedVehiclesAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComponentGroupSummary>> GetComponentGroupsAsync(
        CancellationToken cancellationToken = default);
}

public interface IVehicleProfileQueries
{
    Task<VehicleProfile?> GetAsync(string vehicleKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComplaintSummary>> GetRecentComplaintsAsync(
        string vehicleKey,
        int limit,
        CancellationToken cancellationToken = default);
}

public interface IRecallQueries
{
    Task<IReadOnlyList<RecallCampaign>> GetForVehicleAsync(
        string vehicleKey,
        CancellationToken cancellationToken = default);

    Task<RecallCampaignDetail?> GetByCampaignNumberAsync(
        string campaignNumber,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConsumerAdvisory>> GetLatestAdvisoriesAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
