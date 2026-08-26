using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure.Queries;

public sealed class MongoComponentQueries : IComponentQueries
{
    private const int RankingLimit = 12;

    private static readonly AggregateOptions Options = new() { AllowDiskUse = true };

    private readonly MongoContext _context;

    public MongoComponentQueries(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<IReadOnlyList<ComponentGroupSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _context.Components
            .Find(FilterDefinition<ComponentTaxonomyEntry>.Empty)
            .SortByDescending(entry => entry.ComplaintCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return groups
            .Select(entry => new ComponentGroupSummary(
                entry.Group,
                VehicleKey.Slug(entry.Group),
                entry.ComplaintCount,
                entry.TopLevels))
            .ToList();
    }

    public async Task<ComponentDetail?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var groups = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        var group = groups.FirstOrDefault(candidate => candidate.Slug == slug);
        if (group is null)
        {
            return null;
        }

        var taxonomy = await _context.Components
            .Find(Builders<ComponentTaxonomyEntry>.Filter.Eq(entry => entry.Id, group.Group))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var makes = await TopMakesAsync(group.Group, cancellationToken).ConfigureAwait(false);
        var vehicles = await TopVehiclesAsync(group.Group, cancellationToken).ConfigureAwait(false);
        var (histogram, withMileage) = await MileageAsync(group.Group, cancellationToken).ConfigureAwait(false);

        return new ComponentDetail(
            group.Group,
            group.Slug,
            group.ComplaintCount,
            taxonomy?.TopLevels ?? [],
            makes,
            vehicles,
            histogram,
            withMileage);
    }

    private async Task<IReadOnlyList<MakeSummary>> TopMakesAsync(string group, CancellationToken cancellationToken)
    {
        BsonDocument[] stages =
        [
            new("$match", new BsonDocument("component.group", group)),
            new("$group", new BsonDocument
            {
                { "_id", "$make" },
                { "complaintCount", new BsonDocument("$sum", 1) }
            }),
            new("$sort", new BsonDocument("complaintCount", -1)),
            new("$limit", RankingLimit)
        ];

        var results = await _context.Complaints
            .Aggregate<BsonDocument>(stages, Options, cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return results
            .Select(document => new MakeSummary(
                document["_id"].AsString,
                VehicleKey.Slug(document["_id"].AsString),
                document["complaintCount"].ToInt64(),
                0))
            .ToList();
    }

    private async Task<IReadOnlyList<ComponentVehicleRanking>> TopVehiclesAsync(string group, CancellationToken cancellationToken)
    {
        BsonDocument[] stages =
        [
            new("$unwind", "$components"),
            new("$match", new BsonDocument("components.group", group)),
            new("$sort", new BsonDocument("components.count", -1)),
            new("$limit", RankingLimit),
            new("$project", new BsonDocument
            {
                { "make", 1 },
                { "model", 1 },
                { "modelYear", 1 },
                { "totalComplaints", 1 },
                { "count", "$components.count" }
            })
        ];

        var results = await _context.Profiles
            .Aggregate<BsonDocument>(stages, Options, cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return results
            .Select(document => new ComponentVehicleRanking(
                document["_id"].AsString,
                document["make"].AsString,
                SlugAt(document["_id"].AsString, 0),
                document["model"].AsString,
                SlugAt(document["_id"].AsString, 1),
                document["modelYear"].ToInt32(),
                document["count"].ToInt32(),
                document["totalComplaints"].ToInt32()))
            .ToList();
    }

    private async Task<(IReadOnlyList<MileageBucket> Histogram, int WithMileage)> MileageAsync(
        string group,
        CancellationToken cancellationToken)
    {
        var boundaries = new BsonArray(Enumerable.Range(0, 11).Select(index => index * MileageBuckets.BucketSize));

        BsonDocument[] stages =
        [
            new("$match", new BsonDocument
            {
                { "component.group", group },
                { "milesAtFailure", new BsonDocument("$ne", BsonNull.Value) }
            }),
            new("$bucket", new BsonDocument
            {
                { "groupBy", "$milesAtFailure" },
                { "boundaries", boundaries },
                { "default", "overflow" },
                { "output", new BsonDocument("count", new BsonDocument("$sum", 1)) }
            })
        ];

        var buckets = await _context.Complaints
            .Aggregate<BsonDocument>(stages, Options, cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var counts = new int[MileageBuckets.Empty().Count];
        var withMileage = 0;

        foreach (var bucket in buckets)
        {
            var count = bucket["count"].ToInt32();
            withMileage += count;

            counts[bucket["_id"].IsInt32
                ? bucket["_id"].AsInt32 / MileageBuckets.BucketSize
                : counts.Length - 1] += count;
        }

        var template = MileageBuckets.Empty();
        var histogram = template
            .Select((bucket, index) => new MileageBucket
            {
                From = bucket.From,
                To = bucket.To,
                Count = counts[index]
            })
            .ToList();

        return (histogram, withMileage);
    }

    private static string SlugAt(string vehicleKey, int position)
    {
        var segments = vehicleKey.Split('|');
        return position < segments.Length ? segments[position] : string.Empty;
    }
}
