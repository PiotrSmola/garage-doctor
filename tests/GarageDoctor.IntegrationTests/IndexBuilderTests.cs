using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class IndexBuilderTests(MongoFixture fixture)
{
    [Fact]
    public async Task CreateAllProducesEveryComplaintIndexWithTheExpectedKeySpecification()
    {
        var context = CreateContext();
        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);

        var indexes = await ReadIndexesAsync(context.Complaints);

        Assert.Equal(
            new BsonDocument { { "vehicleKey", 1 }, { "receivedDate", -1 } },
            indexes["complaints_vehicleKey_receivedDate"]["key"].AsBsonDocument);

        Assert.Equal(
            new BsonDocument { { "make", 1 }, { "model", 1 }, { "modelYear", 1 } },
            indexes["complaints_make_model_modelYear"]["key"].AsBsonDocument);

        Assert.Equal(
            new BsonDocument { { "component.group", 1 }, { "make", 1 } },
            indexes["complaints_componentGroup_make"]["key"].AsBsonDocument);

        Assert.Equal(
            new BsonDocument { { "modelYear", 1 }, { "milesAtFailure", 1 } },
            indexes["complaints_modelYear_milesAtFailure"]["key"].AsBsonDocument);

        Assert.Equal(
            new BsonDocument("milesAtFailure", 1),
            indexes["complaints_milesAtFailure"]["key"].AsBsonDocument);

        Assert.Equal(
            new BsonDocument("description", 1),
            indexes["complaints_description_text"]["weights"].AsBsonDocument);
    }

    [Fact]
    public async Task CreateAllDropsTheComplaintIndexesItReplaced()
    {
        var context = CreateContext();
        await context.Complaints.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Complaint>(
                    Builders<Complaint>.IndexKeys.Ascending(complaint => complaint.VehicleKey),
                    new CreateIndexOptions { Name = "complaints_vehicleKey" }),
                new CreateIndexModel<Complaint>(
                    Builders<Complaint>.IndexKeys.Descending(complaint => complaint.ReceivedDate),
                    new CreateIndexOptions { Name = "complaints_receivedDate_desc" })
            ],
            CancellationToken.None);

        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);

        var indexes = await ReadIndexesAsync(context.Complaints);

        Assert.DoesNotContain("complaints_vehicleKey", indexes.Keys);
        Assert.DoesNotContain("complaints_receivedDate_desc", indexes.Keys);
        Assert.Contains("complaints_vehicleKey_receivedDate", indexes.Keys);
    }

    [Fact]
    public async Task CreateAllProducesTheUniqueProfileAndVehicleIndexes()
    {
        var context = CreateContext();
        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);

        var profiles = await ReadIndexesAsync(context.Profiles);
        var vehicles = await ReadIndexesAsync(context.Vehicles);

        Assert.Equal(
            new BsonDocument("vehicleKey", 1),
            profiles["profiles_vehicleKey_unique"]["key"].AsBsonDocument);
        Assert.True(profiles["profiles_vehicleKey_unique"]["unique"].AsBoolean);

        Assert.Equal(
            new BsonDocument("totalComplaints", -1),
            profiles["profiles_totalComplaints_desc"]["key"].AsBsonDocument);

        Assert.Equal(
            new BsonDocument { { "make", 1 }, { "model", 1 }, { "modelYear", 1 } },
            vehicles["vehicles_make_model_modelYear_unique"]["key"].AsBsonDocument);
        Assert.True(vehicles["vehicles_make_model_modelYear_unique"]["unique"].AsBoolean);
    }

    [Fact]
    public async Task CreateAllProducesTheRecallLookupIndexes()
    {
        var context = CreateContext();
        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);

        var recalls = await ReadIndexesAsync(context.Recalls);

        Assert.Equal(new BsonDocument("campaignNumber", 1), recalls["recalls_campaignNumber"]["key"].AsBsonDocument);
        Assert.Equal(new BsonDocument("vehicleKey", 1), recalls["recalls_vehicleKey"]["key"].AsBsonDocument);
    }

    [Fact]
    public async Task AdvisoryIndexesCoverOnlyTheFlaggedRecallRows()
    {
        var context = CreateContext();
        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);

        var recalls = await ReadIndexesAsync(context.Recalls);

        Assert.Equal(new BsonDocument("doNotDrive", 1), recalls["recalls_doNotDrive_flagged"]["key"].AsBsonDocument);
        Assert.Equal(
            new BsonDocument("doNotDrive", true),
            recalls["recalls_doNotDrive_flagged"]["partialFilterExpression"].AsBsonDocument);

        Assert.Equal(new BsonDocument("parkOutside", 1), recalls["recalls_parkOutside_flagged"]["key"].AsBsonDocument);
        Assert.Equal(
            new BsonDocument("parkOutside", true),
            recalls["recalls_parkOutside_flagged"]["partialFilterExpression"].AsBsonDocument);
    }

    [Fact]
    public async Task CreateAllIsIdempotent()
    {
        var context = CreateContext();
        var builder = CreateBuilder(context);

        await builder.CreateAllAsync(CancellationToken.None);
        var afterFirstRun = await ReadIndexNamesAsync(context);

        await builder.CreateAllAsync(CancellationToken.None);
        var afterSecondRun = await ReadIndexNamesAsync(context);

        Assert.Equal(afterFirstRun, afterSecondRun);
        Assert.Contains("complaints_description_text", afterFirstRun);
        Assert.Contains("profiles_vehicleKey_unique", afterFirstRun);
    }

    [Fact]
    public async Task CreateAllSucceedsOnACollectionThatAlreadyHoldsDocuments()
    {
        var context = CreateContext();
        await context.Complaints.InsertOneAsync(SampleComplaint(1, "Loud grinding noise from the front wheels."), cancellationToken: CancellationToken.None);

        var builder = CreateBuilder(context);
        await builder.CreateAllAsync(CancellationToken.None);
        await builder.CreateAllAsync(CancellationToken.None);

        var indexes = await ReadIndexesAsync(context.Complaints);

        Assert.Equal(7, indexes.Count);
    }

    [Fact]
    public async Task UniqueProfileIndexRejectsADuplicateVehicleKey()
    {
        var context = CreateContext();
        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);

        await context.Profiles.InsertOneAsync(SampleProfile("audi|a3|2015", "audi|a3|2015"), cancellationToken: CancellationToken.None);

        var failure = await Assert.ThrowsAsync<MongoWriteException>(
            () => context.Profiles.InsertOneAsync(
                SampleProfile("audi|a3|2015|duplicate", "audi|a3|2015"),
                cancellationToken: CancellationToken.None));

        Assert.Equal(ServerErrorCategory.DuplicateKey, failure.WriteError.Category);
    }

    [Fact]
    public async Task DescriptionTextIndexSupportsTextSearch()
    {
        var context = CreateContext();
        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);

        await context.Complaints.InsertManyAsync(
            [
                SampleComplaint(1, "The brake pedal sank to the floor without any warning."),
                SampleComplaint(2, "The transmission slipped out of gear while merging onto the highway.")
            ],
            cancellationToken: CancellationToken.None);

        var matches = await context.Complaints
            .Find(Builders<Complaint>.Filter.Text("brake"))
            .ToListAsync(CancellationToken.None);

        var match = Assert.Single(matches);
        Assert.Equal(1, match.Id);
    }

    [Fact]
    public async Task ProfileLookupUsesTheVehicleKeyIndexInsteadOfACollectionScan()
    {
        var context = CreateContext();
        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);
        await context.Profiles.InsertOneAsync(SampleProfile("audi|a3|2015", "audi|a3|2015"), cancellationToken: CancellationToken.None);

        var explain = await context.Database.RunCommandAsync<BsonDocument>(
            new BsonDocument
            {
                {
                    "explain", new BsonDocument
                    {
                        { "find", CollectionNames.Profiles },
                        { "filter", new BsonDocument("vehicleKey", "audi|a3|2015") }
                    }
                },
                { "verbosity", "executionStats" }
            },
            cancellationToken: CancellationToken.None);

        var stages = ExplainPlans.Stages(explain);

        Assert.Contains(stages, stage => stage.EndsWith("IXSCAN", StringComparison.Ordinal));
        Assert.DoesNotContain(stages, stage => stage.EndsWith("COLLSCAN", StringComparison.Ordinal));
        Assert.Equal(1, explain["executionStats"]["nReturned"].ToInt32());
        Assert.InRange(explain["executionStats"]["executionTimeMillis"].ToInt32(), 0, 19);
    }

    [Fact]
    public async Task RecentComplaintsOfAVehicleComeOffTheCompoundIndexWithoutAnInMemorySort()
    {
        var context = CreateContext();
        await CreateBuilder(context).CreateAllAsync(CancellationToken.None);
        await context.Complaints.InsertManyAsync(
            [
                SampleComplaint(1, "The brake pedal sank to the floor without any warning."),
                SampleComplaint(2, "The transmission slipped out of gear while merging onto the highway."),
                SampleComplaint(3, "Coolant loss with no visible leak.")
            ],
            cancellationToken: CancellationToken.None);

        var explain = await context.Database.RunCommandAsync<BsonDocument>(
            new BsonDocument
            {
                {
                    "explain", new BsonDocument
                    {
                        { "find", CollectionNames.Complaints },
                        { "filter", new BsonDocument("vehicleKey", "audi|a3|2015") },
                        { "sort", new BsonDocument("receivedDate", -1) },
                        { "limit", 6 }
                    }
                },
                { "verbosity", "executionStats" }
            },
            cancellationToken: CancellationToken.None);

        var stages = ExplainPlans.Stages(explain);

        Assert.Contains("complaints_vehicleKey_receivedDate", ExplainPlans.IndexNames(explain));
        Assert.DoesNotContain(stages, stage => stage.EndsWith("COLLSCAN", StringComparison.Ordinal));
        Assert.DoesNotContain("SORT", stages);
        Assert.Equal(3, explain["executionStats"]["nReturned"].ToInt32());
    }

    private MongoContext CreateContext() => new(fixture.CreateClient(), $"indexes_{Guid.NewGuid():N}");

    private static IndexBuilder CreateBuilder(MongoContext context) => new(context, NullLogger<IndexBuilder>.Instance);

    private static async Task<Dictionary<string, BsonDocument>> ReadIndexesAsync<TDocument>(IMongoCollection<TDocument> collection)
    {
        using var cursor = await collection.Indexes.ListAsync(CancellationToken.None);
        var indexes = await cursor.ToListAsync(CancellationToken.None);

        return indexes.ToDictionary(index => index["name"].AsString, StringComparer.Ordinal);
    }

    private static async Task<string[]> ReadIndexNamesAsync(MongoContext context)
    {
        var complaints = await ReadIndexesAsync(context.Complaints);
        var recalls = await ReadIndexesAsync(context.Recalls);
        var vehicles = await ReadIndexesAsync(context.Vehicles);
        var profiles = await ReadIndexesAsync(context.Profiles);

        return complaints.Keys
            .Concat(recalls.Keys)
            .Concat(vehicles.Keys)
            .Concat(profiles.Keys)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static Complaint SampleComplaint(int id, string description) => new()
    {
        Id = id,
        OdiNumber = 11_000_000 + id,
        Manufacturer = "Fictional Motors Group",
        Make = "AUDI",
        MakeRaw = "AUDI AG",
        Model = "A3",
        ModelRaw = "A3 QUATTRO",
        ModelYear = 2015,
        Component = new ComponentPath
        {
            Raw = "SERVICE BRAKES, HYDRAULIC:FOUNDATION COMPONENTS",
            Group = "SERVICE BRAKES",
            Levels = ["SERVICE BRAKES, HYDRAULIC", "FOUNDATION COMPONENTS"]
        },
        MilesAtFailure = 48_230,
        Description = description,
        FailureDate = new DateOnly(2019, 2, 8),
        ReceivedDate = new DateOnly(2019, 3, 14),
        VehicleKey = "audi|a3|2015"
    };

    private static VehicleProfile SampleProfile(string id, string vehicleKey) => new()
    {
        Id = id,
        VehicleKey = vehicleKey,
        Make = "AUDI",
        Model = "A3",
        ModelYear = 2015,
        TotalComplaints = 1153,
        WithMileage = 568,
        AverageMilesAtFailure = 52_786,
        Components = [new ComponentStat { Group = "POWER TRAIN", Count = 214 }],
        MileageHistogram = [new MileageBucket { From = 0, To = 25_000, Count = 189 }],
        Timeline = [new YearCount { Year = 2019, Count = 42 }],
        Severity = new SeveritySignals
        {
            Crashes = 12,
            Fires = 2,
            Injured = 5,
            Deaths = 0,
            PoliceReports = 7,
            VehiclesTowed = 19,
            MedicalAttention = 3
        },
        ComputedAt = new DateTime(2026, 8, 21, 10, 30, 0, DateTimeKind.Utc)
    };
}
