using GarageDoctor.Domain.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure;

public sealed class VehicleCatalogBuilder
{
    private static readonly AggregateOptions Options = new() { AllowDiskUse = true };

    private readonly MongoContext _context;
    private readonly ILogger<VehicleCatalogBuilder> _logger;

    public VehicleCatalogBuilder(MongoContext context, ILogger<VehicleCatalogBuilder> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _logger = logger;
    }

    public async Task<long> RebuildVehiclesAsync(CancellationToken cancellationToken = default)
    {
        var complaintCount = await EstimatedComplaintCountAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Rebuilding the vehicle catalog from {ComplaintCount} complaints", complaintCount);

        await RunAsync(VehiclePipeline(), cancellationToken).ConfigureAwait(false);
        await RunRecallCountsAsync(cancellationToken).ConfigureAwait(false);

        var written = await _context.Vehicles
            .CountDocumentsAsync(FilterDefinition<VehicleCatalogEntry>.Empty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Rebuilt {VehicleCount} vehicle catalog entries", written);

        return written;
    }

    public async Task<long> RebuildComponentsAsync(CancellationToken cancellationToken = default)
    {
        var complaintCount = await EstimatedComplaintCountAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Rebuilding the component taxonomy from {ComplaintCount} complaints", complaintCount);

        await RunAsync(ComponentPipeline(), cancellationToken).ConfigureAwait(false);

        var written = await _context.Components
            .CountDocumentsAsync(FilterDefinition<ComponentTaxonomyEntry>.Empty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Rebuilt {ComponentCount} component taxonomy entries", written);

        return written;
    }

    private Task<long> EstimatedComplaintCountAsync(CancellationToken cancellationToken) =>
        _context.Complaints.EstimatedDocumentCountAsync(cancellationToken: cancellationToken);

    private Task RunRecallCountsAsync(CancellationToken cancellationToken) =>
        _context.Recalls.AggregateToCollectionAsync(
            PipelineDefinition<RecallCampaign, BsonDocument>.Create(RecallCountPipeline()),
            Options,
            cancellationToken);

    private static BsonDocument[] RecallCountPipeline() =>
    [
        new("$group", new BsonDocument("_id", new BsonDocument
        {
            { "vehicleKey", "$vehicleKey" },
            { "campaignNumber", "$campaignNumber" }
        })),
        new("$group", new BsonDocument
        {
            { "_id", "$_id.vehicleKey" },
            { "recallCount", new BsonDocument("$sum", 1) }
        }),
        new("$merge", new BsonDocument
        {
            { "into", CollectionNames.Vehicles },
            { "on", "_id" },
            { "whenMatched", "merge" },
            { "whenNotMatched", "discard" }
        })
    ];

    private Task RunAsync(BsonDocument[] stages, CancellationToken cancellationToken) =>
        _context.Complaints.AggregateToCollectionAsync(
            PipelineDefinition<Complaint, BsonDocument>.Create(stages),
            Options,
            cancellationToken);

    private static BsonDocument[] VehiclePipeline() =>
    [
        new("$match", new BsonDocument("modelYear", new BsonDocument("$ne", BsonNull.Value))),
        new("$group", new BsonDocument
        {
            { "_id", "$vehicleKey" },
            { "make", new BsonDocument("$first", "$make") },
            { "model", new BsonDocument("$first", "$model") },
            { "modelYear", new BsonDocument("$first", "$modelYear") },
            { "complaintCount", new BsonDocument("$sum", 1) }
        }),
        new("$project", new BsonDocument
        {
            { "make", 1 },
            { "model", 1 },
            { "modelYear", 1 },
            { "complaintCount", 1 },
            { "makeSlug", KeySegment(0) },
            { "modelSlug", KeySegment(1) }
        }),
        new("$merge", new BsonDocument
        {
            { "into", CollectionNames.Vehicles },
            { "on", "_id" },
            { "whenMatched", "merge" },
            { "whenNotMatched", "insert" }
        })
    ];

    private static BsonDocument[] ComponentPipeline() =>
    [
        new("$group", new BsonDocument
        {
            { "_id", "$component.group" },
            { "complaintCount", new BsonDocument("$sum", 1) },
            {
                "topLevels", new BsonDocument("$addToSet", new BsonDocument("$ifNull", new BsonArray
                {
                    new BsonDocument("$arrayElemAt", new BsonArray { "$component.levels", 0 }),
                    BsonNull.Value
                }))
            }
        }),
        new("$project", new BsonDocument
        {
            { "group", "$_id" },
            { "complaintCount", 1 },
            {
                "topLevels", new BsonDocument("$sortArray", new BsonDocument
                {
                    {
                        "input", new BsonDocument("$filter", new BsonDocument
                        {
                            { "input", "$topLevels" },
                            { "cond", new BsonDocument("$ne", new BsonArray { "$$this", BsonNull.Value }) }
                        })
                    },
                    { "sortBy", 1 }
                })
            }
        }),
        new("$merge", new BsonDocument
        {
            { "into", CollectionNames.Components },
            { "on", "_id" },
            { "whenMatched", "replace" },
            { "whenNotMatched", "insert" }
        })
    ];

    private static BsonDocument KeySegment(int position) =>
        new("$arrayElemAt", new BsonArray
        {
            new BsonDocument("$split", new BsonArray { "$_id", "|" }),
            position
        });
}
