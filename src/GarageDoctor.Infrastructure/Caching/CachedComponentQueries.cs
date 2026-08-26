using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Infrastructure.Caching;

public sealed class CachedComponentQueries : IComponentQueries
{
    private readonly IComponentQueries _inner;
    private readonly QueryCache _cache;
    private readonly QueryCacheOptions _options;

    public CachedComponentQueries(IComponentQueries inner, QueryCache cache, QueryCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        _inner = inner;
        _cache = cache;
        _options = options;
    }

    public Task<IReadOnlyList<ComponentGroupSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("component:all"),
            _options.ComponentTtl,
            _inner.GetAllAsync,
            cancellationToken);

    public Task<ComponentDetail?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            _cache.BuildKey("component:detail", slug),
            _options.ComponentTtl,
            token => _inner.GetBySlugAsync(slug, token),
            cancellationToken);
}
