using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Infrastructure.Caching;

public sealed class CachedRecallQueries : IRecallQueries
{
    private readonly IRecallQueries _inner;
    private readonly QueryCache _cache;
    private readonly QueryCacheOptions _options;

    public CachedRecallQueries(IRecallQueries inner, QueryCache cache, QueryCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        _inner = inner;
        _cache = cache;
        _options = options;
    }

    public Task<IReadOnlyList<RecallCampaign>> GetForVehicleAsync(
        string vehicleKey,
        CancellationToken cancellationToken = default) =>
        _inner.GetForVehicleAsync(vehicleKey, cancellationToken);

    public Task<RecallCampaignDetail?> GetByCampaignNumberAsync(
        string campaignNumber,
        CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("recall:campaign", campaignNumber),
            _options.RecallTtl,
            token => _inner.GetByCampaignNumberAsync(campaignNumber, token),
            cancellationToken);

    public Task<IReadOnlyList<ConsumerAdvisory>> GetLatestAdvisoriesAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("recall:advisories", limit),
            _options.AdvisoryTtl,
            token => _inner.GetLatestAdvisoriesAsync(limit, token),
            cancellationToken);
}
