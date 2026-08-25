using GarageDoctor.Domain.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure;

public sealed class ProfileBuilder
{
    private const string BucketCountPrefix = "mileageBucket";

    private const string BucketIndexField = "mileageBucketIndex";

    private static readonly AggregateOptions Options = new() { AllowDiskUse = true };

    private readonly MongoContext _context;
    private readonly ILogger<ProfileBuilder> _logger;

    public ProfileBuilder(MongoContext context, ILogger<ProfileBuilder> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _logger = logger;
    }

    public async Task<long> RebuildAllAsync(CancellationToken cancellationToken = default)
    {
        var computedAt = TruncatedToMilliseconds(DateTime.UtcNow);
        var complaintCount = await _context.Complaints
            .EstimatedDocumentCountAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Rebuilding vehicle profiles from {ComplaintCount} complaints", complaintCount);

        await RunAsync(ScalarsHistogramAndSeverityPipeline(computedAt), cancellationToken).ConfigureAwait(false);
        await RunAsync(ComponentsPipeline(), cancellationToken).ConfigureAwait(false);
        await RunAsync(TimelinePipeline(), cancellationToken).ConfigureAwait(false);

        var written = await _context.Profiles
            .CountDocumentsAsync(
                Builders<VehicleProfile>.Filter.Gte(profile => profile.ComputedAt, computedAt),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Rebuilt {ProfileCount} vehicle profiles", written);

        return written;
    }

    public async Task<VehicleProfile?> GetAsync(string vehicleKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleKey);

        return await _context.Profiles
            .Find(Builders<VehicleProfile>.Filter.Eq(profile => profile.VehicleKey, vehicleKey))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private Task RunAsync(BsonDocument[] stages, CancellationToken cancellationToken) =>
        _context.Complaints.AggregateToCollectionAsync(
            PipelineDefinition<Complaint, BsonDocument>.Create(stages),
            Options,
            cancellationToken);

    private static BsonDocument[] ScalarsHistogramAndSeverityPipeline(DateTime computedAt) =>
    [
        KnownModelYearStage(),
        new("$set", new BsonDocument(BucketIndexField, BucketIndexExpression())),
        new("$group", ScalarGroup()),
        new("$project", ProfileProjection(computedAt)),
        MergeIntoProfilesStage()
    ];

    private static BsonDocument[] ComponentsPipeline() =>
    [
        KnownModelYearStage(),
        new("$group", new BsonDocument
        {
            {
                "_id", new BsonDocument
                {
                    { "vehicleKey", "$vehicleKey" },
                    { "group", "$component.group" }
                }
            },
            { "count", new BsonDocument("$sum", 1) }
        }),
        new("$group", new BsonDocument
        {
            { "_id", "$_id.vehicleKey" },
            {
                "components", new BsonDocument("$push", new BsonDocument
                {
                    { "group", "$_id.group" },
                    { "count", "$count" }
                })
            }
        }),
        new("$set", new BsonDocument("components", new BsonDocument("$sortArray", new BsonDocument
        {
            { "input", "$components" },
            { "sortBy", new BsonDocument { { "count", -1 }, { "group", 1 } } }
        }))),
        MergeIntoProfilesStage()
    ];

    private static BsonDocument[] TimelinePipeline() =>
    [
        KnownModelYearStage(),
        new("$group", new BsonDocument
        {
            {
                "_id", new BsonDocument
                {
                    { "vehicleKey", "$vehicleKey" },
                    { "year", new BsonDocument("$year", "$receivedDate") }
                }
            },
            { "count", new BsonDocument("$sum", 1) }
        }),
        new("$group", new BsonDocument
        {
            { "_id", "$_id.vehicleKey" },
            {
                "timeline", new BsonDocument("$push", new BsonDocument
                {
                    { "year", "$_id.year" },
                    { "count", "$count" }
                })
            }
        }),
        new("$set", new BsonDocument("timeline", new BsonDocument("$sortArray", new BsonDocument
        {
            { "input", "$timeline" },
            { "sortBy", new BsonDocument("year", 1) }
        }))),
        MergeIntoProfilesStage()
    ];

    private static BsonDocument KnownModelYearStage() =>
        new("$match", new BsonDocument("modelYear", new BsonDocument("$ne", BsonNull.Value)));

    private static BsonDocument MergeIntoProfilesStage() =>
        new("$merge", new BsonDocument
        {
            { "into", CollectionNames.Profiles },
            { "on", "_id" },
            { "whenMatched", "merge" },
            { "whenNotMatched", "insert" }
        });

    private static BsonDocument BucketIndexExpression() =>
        new("$cond", new BsonArray
        {
            new BsonDocument("$ne", new BsonArray { "$milesAtFailure", BsonNull.Value }),
            new BsonDocument("$toInt", new BsonDocument("$min", new BsonArray
            {
                new BsonDocument("$max", new BsonArray
                {
                    new BsonDocument("$floor", new BsonDocument("$divide", new BsonArray
                    {
                        "$milesAtFailure",
                        MileageBuckets.BucketSize
                    })),
                    0
                }),
                LastBucketIndex
            })),
            BsonNull.Value
        });

    private static BsonDocument ScalarGroup()
    {
        var group = new BsonDocument
        {
            { "_id", "$vehicleKey" },
            { "make", new BsonDocument("$first", "$make") },
            { "model", new BsonDocument("$first", "$model") },
            { "modelYear", new BsonDocument("$first", "$modelYear") },
            { "totalComplaints", new BsonDocument("$sum", 1) },
            {
                "withMileage", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$ne", new BsonArray { "$milesAtFailure", BsonNull.Value }),
                    1,
                    0
                }))
            },
            {
                "mileageTotal", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray
                {
                    "$milesAtFailure",
                    0
                }))
            },
            { "crashes", FlagSum("$crash") },
            { "fires", FlagSum("$fire") },
            { "injured", new BsonDocument("$sum", "$injured") },
            { "deaths", new BsonDocument("$sum", "$deaths") },
            { "policeReports", FlagSum("$policeReport") },
            { "vehiclesTowed", FlagSum("$vehicleTowed") },
            { "medicalAttention", FlagSum("$medicalAttention") }
        };

        for (var index = 0; index <= LastBucketIndex; index++)
        {
            group.Add(BucketCountPrefix + index, new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { "$" + BucketIndexField, index }),
                1,
                0
            })));
        }

        return group;
    }

    private static BsonDocument ProfileProjection(DateTime computedAt) =>
        new()
        {
            { "vehicleKey", "$_id" },
            { "make", 1 },
            { "model", 1 },
            { "modelYear", 1 },
            { "totalComplaints", 1 },
            { "withMileage", 1 },
            { "averageMilesAtFailure", AverageMilesExpression() },
            { "components", new BsonArray() },
            { "mileageHistogram", HistogramExpression() },
            { "timeline", new BsonArray() },
            { "severity", SeverityExpression() },
            { "computedAt", computedAt }
        };

    private static BsonDocument AverageMilesExpression() =>
        new("$cond", new BsonArray
        {
            new BsonDocument("$gt", new BsonArray { "$withMileage", 0 }),
            new BsonDocument("$toInt", new BsonDocument("$round", new BsonArray
            {
                new BsonDocument("$divide", new BsonArray { "$mileageTotal", "$withMileage" }),
                0
            })),
            BsonNull.Value
        });

    private static BsonArray HistogramExpression()
    {
        var buckets = MileageBuckets.Empty();
        var histogram = new BsonArray();

        for (var index = 0; index < buckets.Count; index++)
        {
            var bucket = buckets[index];

            histogram.Add(new BsonDocument
            {
                { "from", bucket.From },
                { "to", bucket.To is int upperBound ? new BsonInt32(upperBound) : BsonNull.Value },
                { "count", "$" + BucketCountPrefix + index }
            });
        }

        return histogram;
    }

    private static BsonDocument SeverityExpression() =>
        new()
        {
            { "crashes", "$crashes" },
            { "fires", "$fires" },
            { "injured", "$injured" },
            { "deaths", "$deaths" },
            { "policeReports", "$policeReports" },
            { "vehiclesTowed", "$vehiclesTowed" },
            { "medicalAttention", "$medicalAttention" }
        };

    private static BsonDocument FlagSum(string fieldPath) =>
        new("$sum", new BsonDocument("$cond", new BsonArray { fieldPath, 1, 0 }));

    private static int LastBucketIndex => MileageBuckets.LastBucketStart / MileageBuckets.BucketSize;

    private static DateTime TruncatedToMilliseconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
}
