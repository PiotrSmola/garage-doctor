using GarageDoctor.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GarageDoctor.Infrastructure.Caching;

public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddQueryCaching(this IServiceCollection services, QueryCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<QueryCache>();

        services.AddSingleton<MongoVehicleCatalogQueries>();
        services.Replace(ServiceDescriptor.Singleton<IVehicleCatalogQueries>(provider =>
            new CachedVehicleCatalogQueries(
                provider.GetRequiredService<MongoVehicleCatalogQueries>(),
                provider.GetRequiredService<QueryCache>(),
                provider.GetRequiredService<QueryCacheOptions>())));

        services.AddSingleton<MongoComponentQueries>();
        services.Replace(ServiceDescriptor.Singleton<IComponentQueries>(provider =>
            new CachedComponentQueries(
                provider.GetRequiredService<MongoComponentQueries>(),
                provider.GetRequiredService<QueryCache>(),
                provider.GetRequiredService<QueryCacheOptions>())));

        services.AddSingleton<MongoRecallQueries>();
        services.Replace(ServiceDescriptor.Singleton<IRecallQueries>(provider =>
            new CachedRecallQueries(
                provider.GetRequiredService<MongoRecallQueries>(),
                provider.GetRequiredService<QueryCache>(),
                provider.GetRequiredService<QueryCacheOptions>())));

        return services;
    }
}
