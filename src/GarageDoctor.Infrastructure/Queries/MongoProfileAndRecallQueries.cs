using GarageDoctor.Domain.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure.Queries;

public sealed class MongoVehicleProfileQueries : IVehicleProfileQueries
{
    private readonly MongoContext _context;

    public MongoVehicleProfileQueries(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<VehicleProfile?> GetAsync(string vehicleKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleKey);

        return await _context.Profiles
            .Find(Builders<VehicleProfile>.Filter.Eq(profile => profile.VehicleKey, vehicleKey))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ComplaintSummary>> GetRecentComplaintsAsync(
        string vehicleKey,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var complaints = await _context.Complaints
            .Find(Builders<Complaint>.Filter.Eq(complaint => complaint.VehicleKey, vehicleKey))
            .SortByDescending(complaint => complaint.ReceivedDate)
            .Limit(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return complaints
            .Select(complaint => new ComplaintSummary(
                complaint.Id,
                complaint.ReceivedDate,
                complaint.MilesAtFailure,
                complaint.Component.Group,
                complaint.Component.Raw,
                complaint.Description,
                complaint.Crash,
                complaint.Fire,
                complaint.Injured,
                complaint.Deaths))
            .ToList();
    }
}

public sealed class MongoRecallQueries : IRecallQueries
{
    private const int VehicleListLimit = 400;

    private readonly MongoContext _context;

    public MongoRecallQueries(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<IReadOnlyList<RecallCampaign>> GetForVehicleAsync(
        string vehicleKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleKey);

        return await _context.Recalls
            .Find(Builders<RecallCampaign>.Filter.Eq(recall => recall.VehicleKey, vehicleKey))
            .SortByDescending(recall => recall.ReportReceivedDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RecallCampaignDetail?> GetByCampaignNumberAsync(
        string campaignNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignNumber);

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("campaignNumber", campaignNumber)),
            new BsonDocument("$facet", new BsonDocument
            {
                { "summary", SummaryStages() },
                { "vehicles", VehicleStages() },
                { "vehicleCount", VehicleCountStages() }
            })
        };

        var facets = await _context.Recalls
            .Aggregate<BsonDocument>(pipeline, new AggregateOptions { AllowDiskUse = true }, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var summaries = facets?["summary"].AsBsonArray;
        if (summaries is null || summaries.Count == 0)
        {
            return null;
        }

        var summary = summaries[0].AsBsonDocument;
        var vehicles = facets!["vehicles"].AsBsonArray
            .Select(entry => entry.AsBsonDocument)
            .Select(document => new RecalledVehicle(
                document["make"].AsString,
                document["model"].AsString,
                document["modelYear"].IsBsonNull ? null : document["modelYear"].ToInt32(),
                document["_id"].AsString,
                SlugAt(document["_id"].AsString, 0),
                SlugAt(document["_id"].AsString, 1)))
            .ToList();

        var counts = facets["vehicleCount"].AsBsonArray;

        return new RecallCampaignDetail(
            campaignNumber,
            Text(summary, "manufacturer"),
            Text(summary, "componentName"),
            Text(summary, "componentGroup"),
            Text(summary, "defectDescription"),
            Text(summary, "consequence"),
            Text(summary, "correctiveAction"),
            summary["doNotDrive"].ToBoolean(),
            summary["parkOutside"].ToBoolean(),
            summary["potentiallyAffected"].IsBsonNull ? null : summary["potentiallyAffected"].ToInt32(),
            Date(summary, "reportReceivedDate"),
            Date(summary, "ownersNotifiedDate"),
            vehicles,
            counts.Count == 0 ? vehicles.Count : counts[0].AsBsonDocument["total"].ToInt32());
    }

    private static BsonArray SummaryStages() =>
    [
        new BsonDocument("$sort", new BsonDocument("_id", 1)),
        new BsonDocument("$group", new BsonDocument
        {
            { "_id", BsonNull.Value },
            { "manufacturer", new BsonDocument("$first", "$manufacturer") },
            { "componentName", new BsonDocument("$first", "$componentName") },
            { "componentGroup", new BsonDocument("$first", "$componentGroup") },
            { "defectDescription", new BsonDocument("$first", "$defectDescription") },
            { "consequence", new BsonDocument("$first", "$consequence") },
            { "correctiveAction", new BsonDocument("$first", "$correctiveAction") },
            { "reportReceivedDate", new BsonDocument("$first", "$reportReceivedDate") },
            { "ownersNotifiedDate", new BsonDocument("$first", "$ownersNotifiedDate") },
            { "doNotDrive", new BsonDocument("$max", "$doNotDrive") },
            { "parkOutside", new BsonDocument("$max", "$parkOutside") },
            { "potentiallyAffected", new BsonDocument("$max", "$potentiallyAffected") }
        })
    ];

    private static BsonArray VehicleStages() =>
    [
        new BsonDocument("$group", new BsonDocument
        {
            { "_id", "$vehicleKey" },
            { "make", new BsonDocument("$first", "$make") },
            { "model", new BsonDocument("$first", "$model") },
            { "modelYear", new BsonDocument("$first", "$modelYear") }
        }),
        new BsonDocument("$sort", new BsonDocument
        {
            { "make", 1 },
            { "model", 1 },
            { "modelYear", -1 }
        }),
        new BsonDocument("$limit", VehicleListLimit)
    ];

    private static BsonArray VehicleCountStages() =>
    [
        new BsonDocument("$group", new BsonDocument("_id", "$vehicleKey")),
        new BsonDocument("$count", "total")
    ];

    private static string Text(BsonDocument document, string element) =>
        document.TryGetValue(element, out var value) && !value.IsBsonNull ? value.AsString : string.Empty;

    private static DateOnly? Date(BsonDocument document, string element) =>
        document.TryGetValue(element, out var value) && !value.IsBsonNull
            ? DateOnly.FromDateTime(value.ToUniversalTime())
            : null;

    private static string SlugAt(string vehicleKey, int position)
    {
        var segments = vehicleKey.Split('|');
        return position < segments.Length ? segments[position] : string.Empty;
    }
}
