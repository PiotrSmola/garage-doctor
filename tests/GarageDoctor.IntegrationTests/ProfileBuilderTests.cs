using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class ProfileBuilderTests(MongoFixture fixture)
{
    private static readonly int[] MeasuredVolkswagenPowerTrainShape = [1089, 430, 614, 518, 253, 99, 44, 21, 6];

    [Fact]
    public async Task RebuildAllProducesOneProfilePerVehicleKeyWithTheExpectedShape()
    {
        var context = CreateContext();
        await SeedAsync(context, ShapeSample());

        var written = await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);

        Assert.Equal(2, written);
        Assert.Equal(2, await context.Profiles.CountDocumentsAsync(FilterDefinition<VehicleProfile>.Empty));

        var profile = await RequireProfileAsync(context, "volkswagen|golf|2015");

        Assert.Equal("volkswagen|golf|2015", profile.Id);
        Assert.Equal("VOLKSWAGEN", profile.Make);
        Assert.Equal("GOLF", profile.Model);
        Assert.Equal(2015, profile.ModelYear);
        Assert.Equal(6, profile.TotalComplaints);
        Assert.Equal(3, profile.WithMileage);
        Assert.Equal(25_001, profile.AverageMilesAtFailure);
    }

    [Fact]
    public async Task ComponentsAreSortedByCountDescending()
    {
        var context = CreateContext();
        await SeedAsync(context, ShapeSample());
        await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);

        var profile = await RequireProfileAsync(context, "volkswagen|golf|2015");

        Assert.Equal(
            [("POWER TRAIN", 3), ("SERVICE BRAKES", 2), ("ELECTRICAL SYSTEM", 1)],
            profile.Components.Select(component => (component.Group, component.Count)).ToArray());
    }

    [Fact]
    public async Task TimelineIsSortedByCalendarYearAscending()
    {
        var context = CreateContext();
        await SeedAsync(context, ShapeSample());
        await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);

        var profile = await RequireProfileAsync(context, "volkswagen|golf|2015");

        Assert.Equal(
            [(2017, 1), (2019, 2), (2021, 3)],
            profile.Timeline.Select(entry => (entry.Year, entry.Count)).ToArray());
    }

    [Fact]
    public async Task SeveritySignalsAreSummedAcrossEveryComplaint()
    {
        var context = CreateContext();
        await SeedAsync(context, ShapeSample());
        await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);

        var severity = (await RequireProfileAsync(context, "volkswagen|golf|2015")).Severity;

        Assert.Equal(2, severity.Crashes);
        Assert.Equal(1, severity.Fires);
        Assert.Equal(3, severity.Injured);
        Assert.Equal(1, severity.Deaths);
        Assert.Equal(3, severity.PoliceReports);
        Assert.Equal(2, severity.VehiclesTowed);
        Assert.Equal(1, severity.MedicalAttention);
    }

    [Fact]
    public async Task ComputedAtIsStampedInUtcAtRebuildTime()
    {
        var context = CreateContext();
        await SeedAsync(context, ShapeSample());

        var before = DateTime.UtcNow.AddSeconds(-1);
        await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);
        var after = DateTime.UtcNow.AddSeconds(1);

        var profile = await RequireProfileAsync(context, "volkswagen|golf|2015");

        Assert.Equal(DateTimeKind.Utc, profile.ComputedAt.Kind);
        Assert.InRange(profile.ComputedAt, before, after);
    }

    [Fact]
    public async Task MileageHistogramReproducesTheMeasuredBimodalDistribution()
    {
        var context = CreateContext();
        var scaledShape = MeasuredVolkswagenPowerTrainShape
            .Select(count => (int)Math.Round(count / 10.0, MidpointRounding.AwayFromZero))
            .ToArray();

        await SeedAsync(context, BucketedComplaints(scaledShape));
        await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);

        var profile = await RequireProfileAsync(context, "volkswagen|golf|2015");
        var counts = profile.MileageHistogram.Select(bucket => bucket.Count).ToArray();

        Assert.Equal(11, profile.MileageHistogram.Count);
        Assert.Equal([109, 43, 61, 52, 25, 10, 4, 2, 1, 0, 0], counts);
        Assert.Equal(scaledShape.Sum(), profile.WithMileage);
        Assert.Equal(scaledShape.Sum(), profile.TotalComplaints);

        Assert.True(counts[0] > counts[1], "The warranty period peak must stand above the trough");
        Assert.True(counts[1] < counts[2], "The trough must sit below the wear period peak");
        Assert.True(counts[2] > counts[3], "The wear period peak must fall away again");
    }

    [Fact]
    public async Task EveryBucketIsPresentEvenWhenNoComplaintFallsIntoIt()
    {
        var context = CreateContext();
        await SeedAsync(context, [SeedComplaint(1, milesAtFailure: 12_500)]);
        await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);

        var histogram = (await RequireProfileAsync(context, "volkswagen|golf|2015")).MileageHistogram;

        Assert.Equal(
            MileageBuckets.Empty().Select(bucket => bucket.From),
            histogram.Select(bucket => bucket.From));
        Assert.Equal(
            MileageBuckets.Empty().Select(bucket => bucket.To),
            histogram.Select(bucket => bucket.To));
        Assert.Equal(1, histogram.Sum(bucket => bucket.Count));
        Assert.Null(histogram[^1].To);
        Assert.Equal(MileageBuckets.LastBucketStart, histogram[^1].From);
    }

    [Fact]
    public async Task MileageBoundariesLandInTheExpectedBuckets()
    {
        var context = CreateContext();
        await SeedAsync(context,
        [
            SeedComplaint(1, milesAtFailure: 0),
            SeedComplaint(2, milesAtFailure: 24_999),
            SeedComplaint(3, milesAtFailure: 25_000),
            SeedComplaint(4, milesAtFailure: 250_000),
            SeedComplaint(5, milesAtFailure: 400_000),
            SeedComplaint(6, milesAtFailure: null)
        ]);

        await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);

        var profile = await RequireProfileAsync(context, "volkswagen|golf|2015");
        var counts = profile.MileageHistogram.Select(bucket => bucket.Count).ToArray();

        Assert.Equal([2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 2], counts);
        Assert.Equal(6, profile.TotalComplaints);
        Assert.Equal(5, profile.WithMileage);
        Assert.Equal(5, counts.Sum());
    }

    [Fact]
    public async Task ComplaintsWithoutAModelYearProduceNoProfileAndDoNotBreakThePipeline()
    {
        var context = CreateContext();
        await SeedAsync(context,
        [
            SeedComplaint(1, milesAtFailure: 10_000),
            SeedComplaint(2, modelYear: null, milesAtFailure: 30_000),
            SeedComplaint(3, modelYear: null, milesAtFailure: null)
        ]);

        var written = await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);

        Assert.Equal(1, written);
        Assert.Equal(1, await context.Profiles.CountDocumentsAsync(FilterDefinition<VehicleProfile>.Empty));
        Assert.Null(await CreateBuilder(context).GetAsync("volkswagen|golf|unknown", CancellationToken.None));
    }

    [Fact]
    public async Task RebuildAllIsIdempotent()
    {
        var context = CreateContext();
        await SeedAsync(context, ShapeSample());
        var builder = CreateBuilder(context);

        var firstCount = await builder.RebuildAllAsync(CancellationToken.None);
        var afterFirstRun = await ReadProfilesWithoutTimestampAsync(context);

        var secondCount = await builder.RebuildAllAsync(CancellationToken.None);
        var afterSecondRun = await ReadProfilesWithoutTimestampAsync(context);

        Assert.Equal(firstCount, secondCount);
        Assert.Equal(2, secondCount);
        Assert.Equal(afterFirstRun, afterSecondRun);
        Assert.Equal(2, await context.Profiles.CountDocumentsAsync(FilterDefinition<VehicleProfile>.Empty));
    }

    [Fact]
    public async Task GetReturnsTheStoredProfileAndNullForAnUnknownKey()
    {
        var context = CreateContext();
        await SeedAsync(context, ShapeSample());
        var builder = CreateBuilder(context);
        await builder.RebuildAllAsync(CancellationToken.None);

        var known = await builder.GetAsync("audi|a3|2015", CancellationToken.None);
        var unknown = await builder.GetAsync("lancia|delta|1988", CancellationToken.None);

        Assert.NotNull(known);
        Assert.Equal("audi|a3|2015", known.VehicleKey);
        Assert.Equal(2, known.TotalComplaints);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task ProfileLookupUsesAnIndexScanRatherThanACollectionScan()
    {
        var context = CreateContext();
        await SeedAsync(context, ShapeSample());
        await CreateBuilder(context).RebuildAllAsync(CancellationToken.None);
        await new IndexBuilder(context, NullLogger<IndexBuilder>.Instance).CreateAllAsync(CancellationToken.None);

        var explain = await context.Database.RunCommandAsync<BsonDocument>(
            new BsonDocument
            {
                {
                    "explain", new BsonDocument
                    {
                        { "find", CollectionNames.Profiles },
                        { "filter", new BsonDocument("vehicleKey", "volkswagen|golf|2015") },
                        { "limit", 1 }
                    }
                },
                { "verbosity", "executionStats" }
            },
            cancellationToken: CancellationToken.None);

        var stages = ValuesNamed(explain, "stage").ToArray();

        Assert.NotEmpty(stages);
        Assert.Contains(stages, stage => stage.EndsWith("IXSCAN", StringComparison.Ordinal));
        Assert.DoesNotContain(stages, stage => stage.EndsWith("COLLSCAN", StringComparison.Ordinal));
        Assert.Equal(1, explain["executionStats"]["nReturned"].ToInt32());
    }

    private static Complaint[] ShapeSample() =>
    [
        SeedComplaint(1, componentGroup: "POWER TRAIN", milesAtFailure: 10_000, receivedYear: 2017,
            crash: true, policeReport: true, vehicleTowed: true, injured: 1),
        SeedComplaint(2, componentGroup: "POWER TRAIN", milesAtFailure: 20_000, receivedYear: 2019,
            crash: true, fire: true, policeReport: true, medicalAttention: true, injured: 2, deaths: 1),
        SeedComplaint(3, componentGroup: "POWER TRAIN", milesAtFailure: 45_002, receivedYear: 2019,
            policeReport: true, vehicleTowed: true),
        SeedComplaint(4, componentGroup: "SERVICE BRAKES", milesAtFailure: null, receivedYear: 2021),
        SeedComplaint(5, componentGroup: "SERVICE BRAKES", milesAtFailure: null, receivedYear: 2021),
        SeedComplaint(6, componentGroup: "ELECTRICAL SYSTEM", milesAtFailure: null, receivedYear: 2021),
        SeedComplaint(7, make: "AUDI", model: "A3", componentGroup: "ENGINE", milesAtFailure: 60_000),
        SeedComplaint(8, make: "AUDI", model: "A3", componentGroup: "ENGINE", milesAtFailure: 80_000)
    ];

    private static Complaint[] BucketedComplaints(int[] countsPerBucket)
    {
        var complaints = new List<Complaint>();
        var nextId = 1;

        for (var index = 0; index < countsPerBucket.Length; index++)
        {
            var milesAtFailure = (index * MileageBuckets.BucketSize) + (MileageBuckets.BucketSize / 2);

            for (var seeded = 0; seeded < countsPerBucket[index]; seeded++)
            {
                complaints.Add(SeedComplaint(nextId++, milesAtFailure: milesAtFailure));
            }
        }

        return [.. complaints];
    }

    private static Complaint SeedComplaint(
        int id,
        string make = "VOLKSWAGEN",
        string model = "GOLF",
        int? modelYear = 2015,
        string componentGroup = "POWER TRAIN",
        int? milesAtFailure = null,
        int receivedYear = 2019,
        bool crash = false,
        bool fire = false,
        int injured = 0,
        int deaths = 0,
        bool policeReport = false,
        bool vehicleTowed = false,
        bool medicalAttention = false) => new()
    {
        Id = id,
        OdiNumber = 11_000_000 + id,
        Manufacturer = "Fictional Motors Group",
        Make = make,
        MakeRaw = make,
        Model = model,
        ModelRaw = model,
        ModelYear = modelYear,
        Component = new ComponentPath
        {
            Raw = componentGroup,
            Group = componentGroup,
            Levels = [componentGroup]
        },
        MilesAtFailure = milesAtFailure,
        Description = "Synthetic complaint written for the profile aggregation tests.",
        FailureDate = null,
        ReceivedDate = new DateOnly(receivedYear, 6, 1),
        Crash = crash,
        Fire = fire,
        Injured = injured,
        Deaths = deaths,
        PoliceReport = policeReport,
        VehicleTowed = vehicleTowed,
        MedicalAttention = medicalAttention,
        VehicleKey = VehicleKey.Create(VehicleKey.Slug(make), VehicleKey.Slug(model), modelYear)
    };

    private MongoContext CreateContext() => new(fixture.CreateClient(), $"profiles_{Guid.NewGuid():N}");

    private static ProfileBuilder CreateBuilder(MongoContext context) => new(context, NullLogger<ProfileBuilder>.Instance);

    private static Task SeedAsync(MongoContext context, IEnumerable<Complaint> complaints) =>
        context.Complaints.InsertManyAsync(complaints, cancellationToken: CancellationToken.None);

    private static async Task<VehicleProfile> RequireProfileAsync(MongoContext context, string vehicleKey)
    {
        var profile = await CreateBuilder(context).GetAsync(vehicleKey, CancellationToken.None);

        Assert.NotNull(profile);

        return profile;
    }

    private static async Task<List<BsonDocument>> ReadProfilesWithoutTimestampAsync(MongoContext context)
    {
        var documents = await context.Database
            .GetCollection<BsonDocument>(CollectionNames.Profiles)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync(CancellationToken.None);

        foreach (var document in documents)
        {
            document.Remove("computedAt");
        }

        return documents;
    }

    private static IEnumerable<string> ValuesNamed(BsonValue value, string elementName)
    {
        switch (value)
        {
            case BsonDocument document:
                foreach (var element in document)
                {
                    if (element.Name == elementName && element.Value.IsString)
                    {
                        yield return element.Value.AsString;
                    }

                    foreach (var nested in ValuesNamed(element.Value, elementName))
                    {
                        yield return nested;
                    }
                }

                break;

            case BsonArray array:
                foreach (var item in array)
                {
                    foreach (var nested in ValuesNamed(item, elementName))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
