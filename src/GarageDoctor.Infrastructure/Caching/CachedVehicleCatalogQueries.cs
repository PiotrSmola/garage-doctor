using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Infrastructure.Caching;

public sealed class CachedVehicleCatalogQueries : IVehicleCatalogQueries
{
    private readonly IVehicleCatalogQueries _inner;
    private readonly QueryCache _cache;
    private readonly QueryCacheOptions _options;

    public CachedVehicleCatalogQueries(IVehicleCatalogQueries inner, QueryCache cache, QueryCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        _inner = inner;
        _cache = cache;
        _options = options;
    }

    public Task<CatalogOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("catalog:overview"),
            _options.CatalogTtl,
            _inner.GetOverviewAsync,
            cancellationToken);

    public Task<IReadOnlyList<MakeSummary>> GetMakesAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("catalog:makes"),
            _options.CatalogTtl,
            _inner.GetMakesAsync,
            cancellationToken);

    public Task<MakeDetail?> GetMakeAsync(string makeSlug, CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("catalog:make", makeSlug),
            _options.CatalogTtl,
            token => _inner.GetMakeAsync(makeSlug, token),
            cancellationToken);

    public Task<IReadOnlyList<ModelYearSummary>> GetModelYearsAsync(
        string makeSlug,
        string modelSlug,
        CancellationToken cancellationToken = default) =>
        _inner.GetModelYearsAsync(makeSlug, modelSlug, cancellationToken);

    public Task<VehicleIdentity?> ResolveVehicleAsync(
        string makeSlug,
        string modelSlug,
        int modelYear,
        CancellationToken cancellationToken = default) =>
        _inner.ResolveVehicleAsync(makeSlug, modelSlug, modelYear, cancellationToken);

    public Task<IReadOnlyList<VehicleRanking>> GetMostReportedVehiclesAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("catalog:most-reported", limit),
            _options.CatalogTtl,
            token => _inner.GetMostReportedVehiclesAsync(limit, token),
            cancellationToken);

    public Task<IReadOnlyList<ComponentGroupSummary>> GetComponentGroupsAsync(
        CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("catalog:component-groups"),
            _options.CatalogTtl,
            _inner.GetComponentGroupsAsync,
            cancellationToken);
}
