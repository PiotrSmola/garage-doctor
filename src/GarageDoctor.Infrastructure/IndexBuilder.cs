using GarageDoctor.Domain.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure;

public sealed class IndexBuilder
{
    // Indexes an earlier version of this builder created and a later one replaced. They are dropped
    // once their successor exists, so a database ingested before the change stops paying for two
    // indexes over the same keys.
    private static readonly string[] RetiredComplaintIndexes =
    [
        "complaints_vehicleKey",
        "complaints_receivedDate_desc"
    ];

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

        await EnsureAsync(_context.Complaints, ComplaintIndexes(), RetiredComplaintIndexes, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(_context.Recalls, RecallIndexes(), [], cancellationToken).ConfigureAwait(false);
        await EnsureAsync(_context.Vehicles, VehicleIndexes(), [], cancellationToken).ConfigureAwait(false);
        await EnsureAsync(_context.Profiles, ProfileIndexes(), [], cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Finished ensuring MongoDB indexes on database {DatabaseName}", _context.Database.DatabaseNamespace.DatabaseName);
    }

    private static IReadOnlyList<CreateIndexModel<Complaint>> ComplaintIndexes() =>
    [
        // The vehicle profile page reads the newest complaints of one vehicle. With receivedDate in
        // the index those come straight off it instead of fetching every complaint of the vehicle
        // and sorting in memory. The vehicleKey prefix also serves the make filter of the search page.
        new(Builders<Complaint>.IndexKeys
                .Ascending(complaint => complaint.VehicleKey)
                .Descending(complaint => complaint.ReceivedDate),
            new CreateIndexOptions { Name = "complaints_vehicleKey_receivedDate" }),
        new(Builders<Complaint>.IndexKeys
                .Ascending(complaint => complaint.Make)
                .Ascending(complaint => complaint.Model)
                .Ascending(complaint => complaint.ModelYear),
            new CreateIndexOptions { Name = "complaints_make_model_modelYear" }),
        new(Builders<Complaint>.IndexKeys
                .Ascending(complaint => complaint.Component.Group)
                .Ascending(complaint => complaint.Make),
            new CreateIndexOptions { Name = "complaints_componentGroup_make" }),
        // Search filters that carry neither a term nor a make: a model year range, optionally
        // narrowed to complaints with an odometer reading, and the odometer filter on its own.
        // Without these both the page and its count walk the whole collection.
        new(Builders<Complaint>.IndexKeys
                .Ascending(complaint => complaint.ModelYear)
                .Ascending(complaint => complaint.MilesAtFailure),
            new CreateIndexOptions { Name = "complaints_modelYear_milesAtFailure" }),
        new(Builders<Complaint>.IndexKeys.Ascending(complaint => complaint.MilesAtFailure),
            new CreateIndexOptions { Name = "complaints_milesAtFailure" }),
        new(Builders<Complaint>.IndexKeys.Text(complaint => complaint.Description),
            new CreateIndexOptions { Name = "complaints_description_text" })
    ];

    private static IReadOnlyList<CreateIndexModel<RecallCampaign>> RecallIndexes() =>
    [
        new(Builders<RecallCampaign>.IndexKeys.Ascending(recall => recall.CampaignNumber),
            new CreateIndexOptions { Name = "recalls_campaignNumber" }),
        new(Builders<RecallCampaign>.IndexKeys.Ascending(recall => recall.VehicleKey),
            new CreateIndexOptions { Name = "recalls_vehicleKey" }),
        // Consumer advisories are a few thousand rows out of several hundred thousand, so the
        // indexes hold only the flagged rows and the advisory query touches nothing else.
        new(Builders<RecallCampaign>.IndexKeys.Ascending(recall => recall.DoNotDrive),
            new CreateIndexOptions<RecallCampaign>
            {
                Name = "recalls_doNotDrive_flagged",
                PartialFilterExpression = Builders<RecallCampaign>.Filter.Eq(recall => recall.DoNotDrive, true)
            }),
        new(Builders<RecallCampaign>.IndexKeys.Ascending(recall => recall.ParkOutside),
            new CreateIndexOptions<RecallCampaign>
            {
                Name = "recalls_parkOutside_flagged",
                PartialFilterExpression = Builders<RecallCampaign>.Filter.Eq(recall => recall.ParkOutside, true)
            })
    ];

    private static IReadOnlyList<CreateIndexModel<VehicleCatalogEntry>> VehicleIndexes() =>
    [
        new(Builders<VehicleCatalogEntry>.IndexKeys
                .Ascending(vehicle => vehicle.Make)
                .Ascending(vehicle => vehicle.Model)
                .Ascending(vehicle => vehicle.ModelYear),
            new CreateIndexOptions { Name = "vehicles_make_model_modelYear_unique", Unique = true }),
        new(Builders<VehicleCatalogEntry>.IndexKeys
                .Ascending(vehicle => vehicle.MakeSlug)
                .Ascending(vehicle => vehicle.ModelSlug)
                .Descending(vehicle => vehicle.ModelYear),
            new CreateIndexOptions { Name = "vehicles_makeSlug_modelSlug_modelYear" })
    ];

    private static IReadOnlyList<CreateIndexModel<VehicleProfile>> ProfileIndexes() =>
    [
        new(Builders<VehicleProfile>.IndexKeys.Ascending(profile => profile.VehicleKey),
            new CreateIndexOptions { Name = "profiles_vehicleKey_unique", Unique = true }),
        // The most reported vehicles on the home page are the top of this index.
        new(Builders<VehicleProfile>.IndexKeys.Descending(profile => profile.TotalComplaints),
            new CreateIndexOptions { Name = "profiles_totalComplaints_desc" })
    ];

    private async Task EnsureAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        IReadOnlyList<CreateIndexModel<TDocument>> models,
        IReadOnlyList<string> retired,
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
        }
        else
        {
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

        // Successors first, retired ones second, so a query running in between never lacks an index.
        foreach (var name in retired.Where(existingNames.Contains))
        {
            _logger.LogInformation("Dropping retired index {IndexName} from collection {CollectionName}", name, collectionName);

            await collection.Indexes.DropOneAsync(name, cancellationToken).ConfigureAwait(false);
        }
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
