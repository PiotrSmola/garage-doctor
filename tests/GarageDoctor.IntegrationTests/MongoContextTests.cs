using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class MongoContextTests(MongoFixture fixture)
{
    [Fact]
    public void CollectionsResolveUnderExpectedNames()
    {
        var context = CreateContext();

        Assert.Equal("complaints", context.Complaints.CollectionNamespace.CollectionName);
        Assert.Equal("recalls", context.Recalls.CollectionNamespace.CollectionName);
        Assert.Equal("vehicles", context.Vehicles.CollectionNamespace.CollectionName);
        Assert.Equal("components", context.Components.CollectionNamespace.CollectionName);
        Assert.Equal("profiles", context.Profiles.CollectionNamespace.CollectionName);
    }

    [Fact]
    public void CollectionNameConstantsMatchTheResolvedCollections()
    {
        var context = CreateContext();

        Assert.Equal(CollectionNames.Complaints, context.Complaints.CollectionNamespace.CollectionName);
        Assert.Equal(CollectionNames.Recalls, context.Recalls.CollectionNamespace.CollectionName);
        Assert.Equal(CollectionNames.Vehicles, context.Vehicles.CollectionNamespace.CollectionName);
        Assert.Equal(CollectionNames.Components, context.Components.CollectionNamespace.CollectionName);
        Assert.Equal(CollectionNames.Profiles, context.Profiles.CollectionNamespace.CollectionName);
    }

    [Fact]
    public async Task ComplaintRoundTripsWithEveryPropertyPreserved()
    {
        var context = CreateContext();
        var original = FullyPopulatedComplaint();

        await context.Complaints.InsertOneAsync(original, cancellationToken: CancellationToken.None);

        var stored = await context.Complaints
            .Find(complaint => complaint.Id == original.Id)
            .SingleAsync(CancellationToken.None);

        Assert.Equal(original.Id, stored.Id);
        Assert.Equal(original.OdiNumber, stored.OdiNumber);
        Assert.Equal(original.Manufacturer, stored.Manufacturer);
        Assert.Equal(original.Make, stored.Make);
        Assert.Equal(original.MakeRaw, stored.MakeRaw);
        Assert.Equal(original.Model, stored.Model);
        Assert.Equal(original.ModelRaw, stored.ModelRaw);
        Assert.Equal(original.ModelYear, stored.ModelYear);
        Assert.Equal(original.MilesAtFailure, stored.MilesAtFailure);
        Assert.Equal(original.Description, stored.Description);
        Assert.Equal(original.FailureDate, stored.FailureDate);
        Assert.Equal(original.ReceivedDate, stored.ReceivedDate);
        Assert.Equal(original.Crash, stored.Crash);
        Assert.Equal(original.Fire, stored.Fire);
        Assert.Equal(original.Injured, stored.Injured);
        Assert.Equal(original.Deaths, stored.Deaths);
        Assert.Equal(original.PoliceReport, stored.PoliceReport);
        Assert.Equal(original.VehicleTowed, stored.VehicleTowed);
        Assert.Equal(original.MedicalAttention, stored.MedicalAttention);
        Assert.Equal(original.DriveTrain, stored.DriveTrain);
        Assert.Equal(original.FuelType, stored.FuelType);
        Assert.Equal(original.TransmissionType, stored.TransmissionType);
        Assert.Equal(original.Cylinders, stored.Cylinders);
        Assert.Equal(original.VehicleKey, stored.VehicleKey);

        Assert.Equal(original.Component.Raw, stored.Component.Raw);
        Assert.Equal(original.Component.Group, stored.Component.Group);
        Assert.Equal(original.Component.Levels, stored.Component.Levels);
    }

    [Fact]
    public async Task OptionalComplaintValuesRoundTripAsNull()
    {
        var context = CreateContext();
        var original = SparseComplaint();

        await context.Complaints.InsertOneAsync(original, cancellationToken: CancellationToken.None);

        var stored = await context.Complaints
            .Find(complaint => complaint.Id == original.Id)
            .SingleAsync(CancellationToken.None);

        Assert.Null(stored.ModelYear);
        Assert.Null(stored.MilesAtFailure);
        Assert.Null(stored.FailureDate);
        Assert.Null(stored.DriveTrain);
        Assert.Null(stored.FuelType);
        Assert.Null(stored.TransmissionType);
        Assert.Null(stored.Cylinders);
        Assert.Equal(original.ReceivedDate, stored.ReceivedDate);
        Assert.Empty(stored.Component.Levels);
    }

    [Fact]
    public async Task ComplaintIdentifierIsStoredAsUnderscoreId()
    {
        var context = CreateContext();
        var original = FullyPopulatedComplaint();

        await context.Complaints.InsertOneAsync(original, cancellationToken: CancellationToken.None);

        var raw = context.Database.GetCollection<BsonDocument>(CollectionNames.Complaints);
        var document = await raw
            .Find(Builders<BsonDocument>.Filter.Eq("_id", original.Id))
            .SingleAsync(CancellationToken.None);

        Assert.Equal(BsonType.Int32, document["_id"].BsonType);
        Assert.Equal(original.Id, document["_id"].AsInt32);
        Assert.False(document.Contains("id"));
        Assert.False(document.Contains("Id"));
    }

    [Fact]
    public async Task RecallCampaignIdentifierIsStoredAsIntegerUnderscoreId()
    {
        var context = CreateContext();
        var original = SampleRecall(4711);

        await context.Recalls.InsertOneAsync(original, cancellationToken: CancellationToken.None);

        var raw = context.Database.GetCollection<BsonDocument>(CollectionNames.Recalls);
        var document = await raw
            .Find(Builders<BsonDocument>.Filter.Eq("_id", original.Id))
            .SingleAsync(CancellationToken.None);

        Assert.Equal(BsonType.Int32, document["_id"].BsonType);
        Assert.Equal(4711, document["_id"].AsInt32);

        var stored = await context.Recalls
            .Find(recall => recall.Id == original.Id)
            .SingleAsync(CancellationToken.None);

        Assert.Equal(original.CampaignNumber, stored.CampaignNumber);
        Assert.Equal(original.OwnersNotifiedDate, stored.OwnersNotifiedDate);
        Assert.Equal(original.VehicleKey, stored.VehicleKey);
    }

    [Fact]
    public async Task StringIdentifiersAreStoredVerbatimAsUnderscoreId()
    {
        var context = CreateContext();

        await context.Profiles.InsertOneAsync(SampleProfile("audi|a3|2015"), cancellationToken: CancellationToken.None);
        await context.Vehicles.InsertOneAsync(SampleVehicle("audi|a3|2015"), cancellationToken: CancellationToken.None);
        await context.Components.InsertOneAsync(SampleComponent("power-train"), cancellationToken: CancellationToken.None);

        await AssertStringIdentifierAsync(context, CollectionNames.Profiles, "audi|a3|2015");
        await AssertStringIdentifierAsync(context, CollectionNames.Vehicles, "audi|a3|2015");
        await AssertStringIdentifierAsync(context, CollectionNames.Components, "power-train");

        var profile = await context.Profiles
            .Find(candidate => candidate.Id == "audi|a3|2015")
            .SingleAsync(CancellationToken.None);

        Assert.Equal(1153, profile.TotalComplaints);
        Assert.Equal(568, profile.WithMileage);
        Assert.Equal(52_786, profile.AverageMilesAtFailure);
        Assert.Equal(2, profile.MileageHistogram.Count);
        Assert.Null(profile.MileageHistogram[1].To);
        Assert.Equal(12, profile.Severity.Crashes);
    }

    [Theory]
    [InlineData(1972, 1, 1)]
    [InlineData(2000, 2, 29)]
    [InlineData(2019, 3, 14)]
    [InlineData(2031, 12, 31)]
    public async Task ReceivedDateRoundTripsExactly(int year, int month, int day)
    {
        var context = CreateContext();
        var receivedDate = new DateOnly(year, month, day);
        var original = DatedComplaint(year * 100 + month, receivedDate);

        await context.Complaints.InsertOneAsync(original, cancellationToken: CancellationToken.None);

        var stored = await context.Complaints
            .Find(complaint => complaint.Id == original.Id)
            .SingleAsync(CancellationToken.None);

        Assert.Equal(receivedDate, stored.ReceivedDate);
    }

    [Fact]
    public async Task ReceivedDateSupportsServerSideRangeQueries()
    {
        var context = CreateContext();

        await context.Complaints.InsertManyAsync(
            [
                DatedComplaint(1, new DateOnly(2018, 12, 31)),
                DatedComplaint(2, new DateOnly(2019, 1, 1)),
                DatedComplaint(3, new DateOnly(2019, 6, 30)),
                DatedComplaint(4, new DateOnly(2019, 12, 31)),
                DatedComplaint(5, new DateOnly(2020, 1, 1))
            ],
            cancellationToken: CancellationToken.None);

        var filter = Builders<Complaint>.Filter.And(
            Builders<Complaint>.Filter.Gte(complaint => complaint.ReceivedDate, new DateOnly(2019, 1, 1)),
            Builders<Complaint>.Filter.Lte(complaint => complaint.ReceivedDate, new DateOnly(2019, 12, 31)));

        var matches = await context.Complaints
            .Find(filter)
            .SortBy(complaint => complaint.ReceivedDate)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(new[] { 2, 3, 4 }, matches.Select(complaint => complaint.Id).ToArray());
    }

    private MongoContext CreateContext() => new(fixture.CreateClient(), $"context_{Guid.NewGuid():N}");

    private static async Task AssertStringIdentifierAsync(MongoContext context, string collectionName, string expectedId)
    {
        var raw = context.Database.GetCollection<BsonDocument>(collectionName);
        var document = await raw
            .Find(Builders<BsonDocument>.Filter.Eq("_id", expectedId))
            .SingleAsync(CancellationToken.None);

        Assert.Equal(BsonType.String, document["_id"].BsonType);
        Assert.Equal(expectedId, document["_id"].AsString);
    }

    private static Complaint FullyPopulatedComplaint() => DatedComplaint(1_204_517, new DateOnly(2019, 3, 14));

    private static Complaint DatedComplaint(int id, DateOnly receivedDate) => new()
    {
        Id = id,
        OdiNumber = 11_204_517,
        Manufacturer = "Fictional Motors Group",
        Make = "AUDI",
        MakeRaw = "AUDI AG",
        Model = "A3",
        ModelRaw = "A3 QUATTRO",
        ModelYear = 2015,
        Component = new ComponentPath
        {
            Raw = "SERVICE BRAKES, HYDRAULIC:FOUNDATION COMPONENTS:DISC",
            Group = "SERVICE BRAKES",
            Levels = ["SERVICE BRAKES, HYDRAULIC", "FOUNDATION COMPONENTS", "DISC"]
        },
        MilesAtFailure = 48_230,
        Description = "The brake pedal sank to the floor while slowing for a red light.",
        FailureDate = new DateOnly(2019, 2, 8),
        ReceivedDate = receivedDate,
        Crash = true,
        Fire = false,
        Injured = 2,
        Deaths = 0,
        PoliceReport = true,
        VehicleTowed = true,
        MedicalAttention = true,
        DriveTrain = "AWD",
        FuelType = "GAS",
        TransmissionType = "AUTO",
        Cylinders = 4,
        VehicleKey = "audi|a3|2015"
    };

    private static Complaint SparseComplaint() => new()
    {
        Id = 990_001,
        OdiNumber = 10_990_001,
        Manufacturer = "Unknown",
        Make = "UNKNOWN",
        MakeRaw = "",
        Model = "UNKNOWN",
        ModelRaw = "",
        ModelYear = null,
        Component = new ComponentPath { Raw = "", Group = "OTHER", Levels = [] },
        MilesAtFailure = null,
        Description = "No further detail was supplied by the owner.",
        FailureDate = null,
        ReceivedDate = new DateOnly(2004, 7, 2),
        VehicleKey = "unknown|unknown|unknown"
    };

    private static RecallCampaign SampleRecall(int id) => new()
    {
        Id = id,
        CampaignNumber = "19V123000",
        Manufacturer = "Fictional Motors Group",
        Make = "AUDI",
        MakeRaw = "AUDI AG",
        Model = "A3",
        ModelRaw = "A3 QUATTRO",
        ModelYear = 2015,
        ComponentName = "SERVICE BRAKES, HYDRAULIC",
        ComponentGroup = "SERVICE BRAKES",
        RecallType = "V",
        PotentiallyAffected = 84_120,
        OwnersNotifiedDate = new DateOnly(2019, 5, 20),
        ReportReceivedDate = new DateOnly(2019, 4, 3),
        DefectDescription = "Brake fluid may leak from the master cylinder.",
        Consequence = "Increased stopping distance raises the risk of a crash.",
        CorrectiveAction = "Dealers will replace the master cylinder free of charge.",
        DoNotDrive = false,
        ParkOutside = false,
        VehicleKey = "audi|a3|2015"
    };

    private static VehicleProfile SampleProfile(string id) => new()
    {
        Id = id,
        VehicleKey = id,
        Make = "AUDI",
        Model = "A3",
        ModelYear = 2015,
        TotalComplaints = 1153,
        WithMileage = 568,
        AverageMilesAtFailure = 52_786,
        Components = [new ComponentStat { Group = "POWER TRAIN", Count = 214 }],
        MileageHistogram =
        [
            new MileageBucket { From = 0, To = 25_000, Count = 189 },
            new MileageBucket { From = 250_000, To = null, Count = 3 }
        ],
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

    private static VehicleCatalogEntry SampleVehicle(string id) => new()
    {
        Id = id,
        Make = "AUDI",
        MakeSlug = "audi",
        Model = "A3",
        ModelSlug = "a3",
        ModelYear = 2015,
        ComplaintCount = 1153,
        RecallCount = 7
    };

    private static ComponentTaxonomyEntry SampleComponent(string id) => new()
    {
        Id = id,
        Group = "POWER TRAIN",
        TopLevels = ["POWER TRAIN", "POWER TRAIN:AUTOMATIC TRANSMISSION"],
        ComplaintCount = 91_204
    };
}
