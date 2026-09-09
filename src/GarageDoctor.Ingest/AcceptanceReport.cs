using System.Diagnostics;
using System.Globalization;
using System.Text;
using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.Ingest;

public sealed class AcceptanceReport
{
    private static readonly string[] PersonalDataElements =
        ["dealerName", "dealerTel", "dealerCity", "dealerState", "dealerZip", "vin", "vehicleOperator", "city"];

    private static readonly (string Make, int Reference)[] ReferenceMakes =
    [
        ("FORD", 382512), ("CHEVROLET", 251568), ("DODGE", 153956), ("TOYOTA", 139142),
        ("HONDA", 124361), ("JEEP", 124093), ("NISSAN", 96769), ("HYUNDAI", 77360),
        ("CHRYSLER", 67100), ("GMC", 63688), ("VOLKSWAGEN", 48705), ("BMW", 40800),
        ("AUDI", 16213), ("VOLVO", 15161), ("MERCEDES-BENZ", 15055)
    ];

    private static readonly int[] ReferenceVolkswagenPowerTrain = [1089, 430, 614, 518, 253, 99, 44, 21, 6];

    private readonly MongoContext _context;

    public AcceptanceReport(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<string> RenderAsync(CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("=== ACCEPTANCE REPORT ===");
        builder.AppendLine();

        var peakMemoryMegabytes = Process.GetCurrentProcess().PeakWorkingSet64 / 1_048_576;
        builder.AppendLine(Line("2. peak process memory", $"{peakMemoryMegabytes} MB", peakMemoryMegabytes < 500));

        var complaintCount = await _context.Complaints.CountDocumentsAsync(FilterDefinition<Complaint>.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);
        builder.AppendLine(Line("3. complaints ingested", complaintCount.ToString("N0", CultureInfo.InvariantCulture), complaintCount > 2_000_000));

        var distinctMakes = await _context.Complaints.DistinctAsync(complaint => complaint.Make, FilterDefinition<Complaint>.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);
        var makeCount = (await distinctMakes.ToListAsync(cancellationToken).ConfigureAwait(false)).Count;
        builder.AppendLine(Line("4. distinct canonical makes", makeCount.ToString(CultureInfo.InvariantCulture), makeCount < 1069));

        builder.AppendLine();
        builder.AppendLine("5. reference make counts (reference table was measured on an older snapshot; the file grows daily)");
        foreach (var (make, reference) in ReferenceMakes)
        {
            var count = await _context.Complaints
                .CountDocumentsAsync(Builders<Complaint>.Filter.Eq(complaint => complaint.Make, make), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var difference = count - reference;
            builder.AppendLine($"     {make,-16} {count,9:N0}   reference {reference,9:N0}   diff {difference,+8:N0}");
        }

        builder.AppendLine();
        var audiProfile = await _context.Profiles
            .Find(Builders<VehicleProfile>.Filter.Eq(profile => profile.VehicleKey, "audi|a3|2015"))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        builder.AppendLine(Line("6. profile audi|a3|2015",
            audiProfile is null
                ? "missing"
                : $"{audiProfile.TotalComplaints} complaints, {audiProfile.WithMileage} with mileage",
            audiProfile is not null));

        var histogram = await BuildVolkswagenPowerTrainHistogramAsync(cancellationToken).ConfigureAwait(false);
        var matches = ReferenceVolkswagenPowerTrain.Length <= histogram.Count
            && !ReferenceVolkswagenPowerTrain.Where((expected, index) => histogram[index] != expected).Any();
        builder.AppendLine(Line("7. VW POWER TRAIN histogram", string.Join(" / ", histogram.Take(9)), matches));
        builder.AppendLine($"     reference                  {string.Join(" / ", ReferenceVolkswagenPowerTrain)}");

        var (indexed, elapsedMilliseconds) = await ExplainProfileLookupAsync(cancellationToken).ConfigureAwait(false);
        builder.AppendLine(Line("9. profile lookup", $"{elapsedMilliseconds} ms, index scan: {indexed}", indexed && elapsedMilliseconds < 20));

        var sample = await _context.Database
            .GetCollection<BsonDocument>(CollectionNames.Complaints)
            .Find(new BsonDocument())
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var leakedElements = sample is null
            ? []
            : PersonalDataElements.Where(element => sample.Contains(element)).ToArray();
        builder.AppendLine(Line("10. personal data elements", leakedElements.Length == 0 ? "none present" : string.Join(", ", leakedElements), leakedElements.Length == 0));

        builder.AppendLine();
        return builder.ToString();
    }

    private async Task<IReadOnlyList<int>> BuildVolkswagenPowerTrainHistogramAsync(CancellationToken cancellationToken)
    {
        var boundaries = new BsonArray(Enumerable.Range(0, 11).Select(index => index * 25_000));
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "make", "VOLKSWAGEN" },
                { "component.group", "POWER TRAIN" },
                { "milesAtFailure", new BsonDocument("$ne", BsonNull.Value) }
            }),
            new BsonDocument("$bucket", new BsonDocument
            {
                { "groupBy", "$milesAtFailure" },
                { "boundaries", boundaries },
                { "default", "overflow" },
                { "output", new BsonDocument("count", new BsonDocument("$sum", 1)) }
            })
        };

        var buckets = await _context.Database
            .GetCollection<BsonDocument>(CollectionNames.Complaints)
            .Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var counts = new int[10];
        foreach (var bucket in buckets)
        {
            if (bucket["_id"].IsInt32)
            {
                counts[bucket["_id"].AsInt32 / 25_000] = bucket["count"].AsInt32;
            }
        }

        return counts;
    }

    private async Task<(bool Indexed, double ElapsedMilliseconds)> ExplainProfileLookupAsync(CancellationToken cancellationToken)
    {
        var explain = await _context.Database.RunCommandAsync<BsonDocument>(
            new BsonDocument
            {
                { "explain", new BsonDocument
                    {
                        { "find", CollectionNames.Profiles },
                        { "filter", new BsonDocument("vehicleKey", "audi|a3|2015") }
                    }
                },
                { "verbosity", "executionStats" }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var plan = explain.ToString();
        var indexed = plan.Contains("IXSCAN", StringComparison.Ordinal) && !plan.Contains("COLLSCAN", StringComparison.Ordinal);
        var elapsed = explain.TryGetValue("executionStats", out var stats) && stats.AsBsonDocument.TryGetValue("executionTimeMillis", out var time)
            ? time.ToDouble()
            : -1;

        return (indexed, elapsed);
    }

    private static string Line(string label, string value, bool passed) =>
        $"  [{(passed ? "ok" : "!!")}] {label,-28} {value}";
}
