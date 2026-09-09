using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class VehicleCatalogBuilderTests(MongoFixture fixture)
{
    [Fact]
    public async Task RebuildVehiclesProducesOneEntryPerMakeModelYearWithComplaintCounts()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());

        var written = await CreateBuilder(context).RebuildVehiclesAsync(CancellationToken.None);

        Assert.Equal(3, written);

        var golf2015 = await RequireVehicleAsync(context, "volkswagen|golf|2015");

        Assert.Equal("VOLKSWAGEN", golf2015.Make);
        Assert.Equal("GOLF", golf2015.Model);
        Assert.Equal(2015, golf2015.ModelYear);
        Assert.Equal(4, golf2015.ComplaintCount);

        var golf2016 = await RequireVehicleAsync(context, "volkswagen|golf|2016");
        Assert.Equal(1, golf2016.ComplaintCount);

        var a3 = await RequireVehicleAsync(context, "audi|a3|2015");
        Assert.Equal("AUDI", a3.Make);
        Assert.Equal(2, a3.ComplaintCount);
    }

    [Fact]
    public async Task RecallCampaignsAreCountedOncePerVehicleEvenWhenTheySpanSeveralRows()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());
        await context.Recalls.InsertManyAsync(
            [
                Recall(1, "23V100000", "volkswagen|golf|2015"),
                Recall(2, "23V100000", "volkswagen|golf|2015"),
                Recall(3, "23V200000", "volkswagen|golf|2015"),
                Recall(4, "23V300000", "audi|a3|2015"),
                Recall(5, "23V400000", "porsche|911|2015")
            ],
            cancellationToken: CancellationToken.None);

        await CreateBuilder(context).RebuildVehiclesAsync(CancellationToken.None);

        Assert.Equal(2, (await RequireVehicleAsync(context, "volkswagen|golf|2015")).RecallCount);
        Assert.Equal(1, (await RequireVehicleAsync(context, "audi|a3|2015")).RecallCount);
        Assert.Equal(0, (await RequireVehicleAsync(context, "volkswagen|golf|2016")).RecallCount);
        Assert.Null(await context.Vehicles
            .Find(Builders<VehicleCatalogEntry>.Filter.Eq(entry => entry.Id, "porsche|911|2015"))
            .FirstOrDefaultAsync(CancellationToken.None));
    }

    private static RecallCampaign Recall(int id, string campaignNumber, string vehicleKey) => new()
    {
        Id = id,
        CampaignNumber = campaignNumber,
        Manufacturer = "Fictional Motors USA, LLC",
        Make = "VOLKSWAGEN",
        MakeRaw = "VOLKSWAGEN",
        Model = "GOLF",
        ModelRaw = "GOLF",
        ModelYear = 2015,
        ComponentName = "POWER TRAIN",
        ComponentGroup = "POWER TRAIN",
        RecallType = "V",
        DefectDescription = "Synthetic defect summary.",
        Consequence = "Synthetic consequence summary.",
        CorrectiveAction = "Synthetic corrective action.",
        VehicleKey = vehicleKey
    };

    [Fact]
    public async Task VehicleSlugsMatchTheCanonicalSlugsEmbeddedInTheVehicleKey()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());

        await CreateBuilder(context).RebuildVehiclesAsync(CancellationToken.None);

        var entries = await context.Vehicles
            .Find(FilterDefinition<VehicleCatalogEntry>.Empty)
            .ToListAsync(CancellationToken.None);

        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            Assert.Equal(VehicleKey.Slug(entry.Make), entry.MakeSlug);
            Assert.Equal(VehicleKey.Slug(entry.Model), entry.ModelSlug);
            Assert.Equal(VehicleKey.Create(entry.MakeSlug, entry.ModelSlug, entry.ModelYear), entry.Id);
        }
    }

    [Fact]
    public async Task ComplaintsWithAnUnknownModelYearStayOutOfTheCatalog()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());

        await CreateBuilder(context).RebuildVehiclesAsync(CancellationToken.None);

        var unknown = await context.Vehicles
            .Find(Builders<VehicleCatalogEntry>.Filter.Eq(entry => entry.Id, "audi|a3|unknown"))
            .FirstOrDefaultAsync(CancellationToken.None);

        Assert.Null(unknown);
    }

    [Fact]
    public async Task RebuildingVehiclesTwiceLeavesTheSameCountsInPlace()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());
        var builder = CreateBuilder(context);

        var first = await builder.RebuildVehiclesAsync(CancellationToken.None);
        var second = await builder.RebuildVehiclesAsync(CancellationToken.None);

        Assert.Equal(first, second);

        var golf2015 = await RequireVehicleAsync(context, "volkswagen|golf|2015");
        Assert.Equal(4, golf2015.ComplaintCount);
    }

    [Fact]
    public async Task RebuildComponentsFoldsRawTopLevelsIntoCanonicalGroups()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());

        var written = await CreateBuilder(context).RebuildComponentsAsync(CancellationToken.None);

        Assert.Equal(2, written);

        var engine = await RequireComponentAsync(context, "ENGINE");

        Assert.Equal("ENGINE", engine.Group);
        Assert.Equal(4, engine.ComplaintCount);
        Assert.Equal(["ENGINE", "ENGINE AND ENGINE COOLING"], engine.TopLevels);
        Assert.Empty(engine.TopVehicles);

        var powerTrain = await RequireComponentAsync(context, "POWER TRAIN");
        Assert.Equal(4, powerTrain.ComplaintCount);
        Assert.Equal(["POWER TRAIN"], powerTrain.TopLevels);
    }

    [Fact]
    public async Task RebuildComponentsPrecomputesTheHistogramAndBothRankings()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());
        await new ProfileBuilder(context, NullLogger<ProfileBuilder>.Instance).RebuildAllAsync(CancellationToken.None);

        await CreateBuilder(context).RebuildComponentsAsync(CancellationToken.None);

        var engine = await RequireComponentAsync(context, "ENGINE");

        Assert.Equal(4, engine.WithMileage);
        Assert.Equal(
            MileageBuckets.Empty().Select(bucket => bucket.From),
            engine.MileageHistogram.Select(bucket => bucket.From));
        Assert.Equal(4, engine.MileageHistogram[1].Count);
        Assert.Equal(4, engine.MileageHistogram.Sum(bucket => bucket.Count));

        Assert.Equal(
            [("AUDI", 2), ("VOLKSWAGEN", 2)],
            engine.TopMakes.Select(make => (make.Make, make.Count)).ToArray());

        Assert.Equal(
            [("volkswagen|golf|2015", 2), ("audi|a3|2015", 1)],
            engine.TopVehicles.Select(vehicle => (vehicle.VehicleKey, vehicle.Count)).ToArray());
        Assert.Equal("VOLKSWAGEN", engine.TopVehicles[0].Make);
        Assert.Equal("GOLF", engine.TopVehicles[0].Model);
        Assert.Equal(2015, engine.TopVehicles[0].ModelYear);
        Assert.Equal(4, engine.TopVehicles[0].TotalComplaints);
    }

    [Fact]
    public async Task ComponentCountsIncludeComplaintsWithAnUnknownModelYear()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());

        await CreateBuilder(context).RebuildComponentsAsync(CancellationToken.None);

        var engine = await RequireComponentAsync(context, "ENGINE");
        var vehicles = await CreateBuilder(context).RebuildVehiclesAsync(CancellationToken.None);

        Assert.Equal(4, engine.ComplaintCount);
        Assert.Equal(3, vehicles);
    }

    [Fact]
    public async Task RebuildingComponentsTwiceLeavesTheSameCountsInPlace()
    {
        var context = CreateContext();
        await SeedAsync(context, Sample());
        var builder = CreateBuilder(context);

        var first = await builder.RebuildComponentsAsync(CancellationToken.None);
        var second = await builder.RebuildComponentsAsync(CancellationToken.None);

        Assert.Equal(first, second);

        var powerTrain = await RequireComponentAsync(context, "POWER TRAIN");
        Assert.Equal(4, powerTrain.ComplaintCount);
    }

    private static IReadOnlyList<Complaint> Sample() =>
    [
        Complaint(1, "VOLKSWAGEN", "GOLF", 2015, "POWER TRAIN", "POWER TRAIN"),
        Complaint(2, "VOLKSWAGEN", "GOLF", 2015, "POWER TRAIN", "POWER TRAIN"),
        Complaint(3, "VOLKSWAGEN", "GOLF", 2015, "ENGINE", "ENGINE AND ENGINE COOLING"),
        Complaint(4, "VOLKSWAGEN", "GOLF", 2015, "ENGINE", "ENGINE"),
        Complaint(5, "VOLKSWAGEN", "GOLF", 2016, "POWER TRAIN", "POWER TRAIN"),
        Complaint(6, "AUDI", "A3", 2015, "POWER TRAIN", "POWER TRAIN"),
        Complaint(7, "AUDI", "A3", 2015, "ENGINE", "ENGINE AND ENGINE COOLING"),
        Complaint(8, "AUDI", "A3", null, "ENGINE", "ENGINE")
    ];

    private static Complaint Complaint(
        int id,
        string make,
        string model,
        int? modelYear,
        string componentGroup,
        string rawTopLevel) => new()
    {
        Id = id,
        OdiNumber = 12_000_000 + id,
        Manufacturer = "Fictional Motors Group",
        Make = make,
        MakeRaw = make,
        Model = model,
        ModelRaw = model,
        ModelYear = modelYear,
        Component = new ComponentPath
        {
            Raw = rawTopLevel,
            Group = componentGroup,
            Levels = [rawTopLevel]
        },
        MilesAtFailure = 30_000,
        Description = "Synthetic complaint written for the catalog aggregation tests.",
        ReceivedDate = new DateOnly(2019, 6, 1),
        VehicleKey = VehicleKey.Create(VehicleKey.Slug(make), VehicleKey.Slug(model), modelYear)
    };

    private MongoContext CreateContext() => new(fixture.CreateClient(), $"catalog_{Guid.NewGuid():N}");

    private static VehicleCatalogBuilder CreateBuilder(MongoContext context) =>
        new(context, NullLogger<VehicleCatalogBuilder>.Instance);

    private static Task SeedAsync(MongoContext context, IEnumerable<Complaint> complaints) =>
        context.Complaints.InsertManyAsync(complaints, cancellationToken: CancellationToken.None);

    private static async Task<VehicleCatalogEntry> RequireVehicleAsync(MongoContext context, string vehicleKey)
    {
        var entry = await context.Vehicles
            .Find(Builders<VehicleCatalogEntry>.Filter.Eq(vehicle => vehicle.Id, vehicleKey))
            .FirstOrDefaultAsync(CancellationToken.None);

        Assert.NotNull(entry);

        return entry;
    }

    private static async Task<ComponentTaxonomyEntry> RequireComponentAsync(MongoContext context, string group)
    {
        var entry = await context.Components
            .Find(Builders<ComponentTaxonomyEntry>.Filter.Eq(component => component.Id, group))
            .FirstOrDefaultAsync(CancellationToken.None);

        Assert.NotNull(entry);

        return entry;
    }
}
