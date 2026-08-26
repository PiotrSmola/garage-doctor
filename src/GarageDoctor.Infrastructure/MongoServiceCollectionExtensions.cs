using GarageDoctor.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure;

public static class MongoServiceCollectionExtensions
{
    public static IServiceCollection AddMongo(this IServiceCollection services, MongoSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        MongoConventions.Register();

        services.AddSingleton(settings);
        services.AddSingleton<IMongoClient>(_ => new MongoClient(settings.ConnectionString));
        services.AddSingleton(provider => new MongoContext(
            provider.GetRequiredService<IMongoClient>(),
            provider.GetRequiredService<MongoSettings>().DatabaseName));
        services.AddSingleton<IndexBuilder>();
        services.AddSingleton<IVehicleCatalogQueries, MongoVehicleCatalogQueries>();
        services.AddSingleton<IVehicleProfileQueries, MongoVehicleProfileQueries>();
        services.AddSingleton<IRecallQueries, MongoRecallQueries>();
        services.AddSingleton<ISearchQueries, MongoSearchQueries>();
        services.AddSingleton<IComponentQueries, MongoComponentQueries>();

        return services;
    }
}
