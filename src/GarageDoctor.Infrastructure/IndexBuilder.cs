using GarageDoctor.Domain.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure;

public sealed class IndexBuilder
{
    private readonly MongoContext _context;
    private readonly ILogger<IndexBuilder> _logger;

    public IndexBuilder(MongoContext context, ILogger<IndexBuilder> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _logger = logger;
    }

    public async Task CreateAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Ensuring MongoDB indexes on database {DatabaseName}", _context.Database.DatabaseNamespace.DatabaseName);

        await EnsureAsync(_context.Complaints, ComplaintIndexes(), cancellationToken).ConfigureAwait(false);
        await EnsureAsync(_context.Recalls, RecallIndexes(), cancellationToken).ConfigureAwait(false);
        await EnsureAsync(_context.Vehicles, VehicleIndexes(), cancellationToken).ConfigureAwait(false);
        await EnsureAsync(_context.Profiles, ProfileIndexes(), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Finished ensuring MongoDB indexes on database {DatabaseName}", _context.Database.DatabaseNamespace.DatabaseName);
    }

    private static IReadOnlyList<CreateIndexModel<Complaint>> ComplaintIndexes() =>
    [
        new(Builders<Complaint>.IndexKeys.Ascending(complaint => complaint.VehicleKey),
            new CreateIndexOptions { Name = "complaints_vehicleKey" }),
        new(Builders<Complaint>.IndexKeys
                .Ascending(complaint => complaint.Make)
                .Ascending(complaint => complaint.Model)
                .Ascending(complaint => complaint.ModelYear),
            new CreateIndexOptions { Name = "complaints_make_model_modelYear" }),
        new(Builders<Complaint>.IndexKeys
                .Ascending(complaint => complaint.Component.Group)
                .Ascending(complaint => complaint.Make),
            new CreateIndexOptions { Name = "complaints_componentGroup_make" }),
        new(Builders<Complaint>.IndexKeys.Descending(complaint => complaint.ReceivedDate),
            new CreateIndexOptions { Name = "complaints_receivedDate_desc" }),
        new(Builders<Complaint>.IndexKeys.Text(complaint => complaint.Description),
            new CreateIndexOptions { Name = "complaints_description_text" })
    ];

    private static IReadOnlyList<CreateIndexModel<RecallCampaign>> RecallIndexes() =>
    [
        new(Builders<RecallCampaign>.IndexKeys.Ascending(recall => recall.CampaignNumber),
            new CreateIndexOptions { Name = "recalls_campaignNumber" }),
        new(Builders<RecallCampaign>.IndexKeys.Ascending(recall => recall.VehicleKey),
            new CreateIndexOptions { Name = "recalls_vehicleKey" })
    ];

    private static IReadOnlyList<CreateIndexModel<VehicleCatalogEntry>> VehicleIndexes() =>
    [
        new(Builders<VehicleCatalogEntry>.IndexKeys
                .Ascending(vehicle => vehicle.Make)
                .Ascending(vehicle => vehicle.Model)
                .Ascending(vehicle => vehicle.ModelYear),
            new CreateIndexOptions { Name = "vehicles_make_model_modelYear_unique", Unique = true })
    ];

    private static IReadOnlyList<CreateIndexModel<VehicleProfile>> ProfileIndexes() =>
    [
        new(Builders<VehicleProfile>.IndexKeys.Ascending(profile => profile.VehicleKey),
            new CreateIndexOptions { Name = "profiles_vehicleKey_unique", Unique = true })
    ];

    private async Task EnsureAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        IReadOnlyList<CreateIndexModel<TDocument>> models,
        CancellationToken cancellationToken)
    {
        var collectionName = collection.CollectionNamespace.CollectionName;
        var existingNames = await ListIndexNamesAsync(collection, cancellationToken).ConfigureAwait(false);

        var missing = models
            .Where(model => model.Options.Name is not null && !existingNames.Contains(model.Options.Name))
            .ToArray();

        if (missing.Length == 0)
        {
            _logger.LogInformation(
                "Collection {CollectionName} already carries all {ExpectedCount} expected indexes",
                collectionName,
                models.Count);
            return;
        }

        var missingNames = string.Join(", ", missing.Select(model => model.Options.Name));

        _logger.LogInformation(
            "Creating {MissingCount} index(es) on collection {CollectionName}: {IndexNames}",
            missing.Length,
            collectionName,
            missingNames);

        await collection.Indexes.CreateManyAsync(missing, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Created {MissingCount} index(es) on collection {CollectionName}: {IndexNames}",
            missing.Length,
            collectionName,
            missingNames);
    }

    private static async Task<HashSet<string>> ListIndexNamesAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        using var cursor = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var index in cursor.Current)
            {
                names.Add(index["name"].AsString);
            }
        }

        return names;
    }
}
