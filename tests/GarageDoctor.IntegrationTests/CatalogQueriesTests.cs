using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Queries;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class CatalogQueriesTests(MongoFixture fixture)
{
    [Fact]
    public async Task MakesAreRankedByComplaintCountAndCarryTheirModelCount()
    {
        var context = await SeededContextAsync();

        var makes = await new MongoVehicleCatalogQueries(context).GetMakesAsync(CancellationToken.None);

        Assert.Equal(["VOLKSWAGEN", "AUDI"], makes.Select(make => make.Make));
        Assert.Equal(5, makes[0].ComplaintCount);
        Assert.Equal(2, makes[0].ModelCount);
        Assert.Equal("volkswagen", makes[0].MakeSlug);
        Assert.Equal(1, makes[1].ModelCount);
    }

    [Fact]
    public async Task MakeDetailListsModelsWithTheirYearSpan()
    {
        var context = await SeededContextAsync();

        var make = await new MongoVehicleCatalogQueries(context).GetMakeAsync("volkswagen", CancellationToken.None);

        Assert.NotNull(make);
        Assert.Equal("VOLKSWAGEN", make.Make);
        Assert.Equal(5, make.ComplaintCount);

        var golf = make.Models.Single(model => model.ModelSlug == "golf");
        Assert.Equal(2015, golf.EarliestModelYear);
        Assert.Equal(2016, golf.LatestModelYear);
        Assert.Equal(3, golf.ComplaintCount);
    }

    [Fact]
    public async Task UnknownMakeResolvesToNull()
    {
        var context = await SeededContextAsync();

        var make = await new MongoVehicleCatalogQueries(context).GetMakeAsync("delorean", CancellationToken.None);

        Assert.Null(make);
    }

    [Fact]
    public async Task ModelYearsComeBackNewestFirst()
    {
        var context = await SeededContextAsync();

        var years = await new MongoVehicleCatalogQueries(context)
            .GetModelYearsAsync("volkswagen", "golf", CancellationToken.None);

        Assert.Equal([2016, 2015], years.Select(year => year.ModelYear));
        Assert.Equal("volkswagen|golf|2016", years[0].VehicleKey);
    }

    [Fact]
    public async Task VehicleResolutionMatchesTheCanonicalKeyAndRejectsUnknownCombinations()
    {
        var context = await SeededContextAsync();
        var queries = new MongoVehicleCatalogQueries(context);

        var resolved = await queries.ResolveVehicleAsync("volkswagen", "golf", 2015, CancellationToken.None);
        var missing = await queries.ResolveVehicleAsync("volkswagen", "golf", 1998, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal("volkswagen|golf|2015", resolved.VehicleKey);
        Assert.Equal("GOLF", resolved.Model);
        Assert.Null(missing);
    }

    [Fact]
    public async Task OverviewCountsTheCollectionsAndTheMileageCoverage()
    {
        var context = await SeededContextAsync();
        await context.Profiles.InsertManyAsync(
            [
                Profile("volkswagen|golf|2015", "VOLKSWAGEN", "GOLF", 2015, totalComplaints: 3, withMileage: 2),
                Profile("audi|a3|2015", "AUDI", "A3", 2015, totalComplaints: 1, withMileage: 1)
            ],
            cancellationToken: CancellationToken.None);

        var overview = await new MongoVehicleCatalogQueries(context).GetOverviewAsync(CancellationToken.None);

        Assert.Equal(4, overview.Complaints);
        Assert.Equal(4, overview.VehicleCombinations);
        Assert.Equal(2, overview.Makes);
        Assert.Equal(3, overview.ComplaintsWithMileage);
        Assert.Equal(2015, overview.EarliestModelYear);
    }

    [Fact]
    public async Task RecentComplaintsComeBackNewestFirstAndRespectTheLimit()
    {
        var context = await SeededContextAsync();

        var recent = await new MongoVehicleProfileQueries(context)
            .GetRecentComplaintsAsync("volkswagen|golf|2015", 2, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.True(recent[0].ReceivedDate >= recent[1].ReceivedDate);
        Assert.Equal("POWER TRAIN", recent[0].ComponentGroup);
    }

    [Fact]
    public async Task RecallCampaignFoldsItsRowsIntoOneDetailWithTheAdvisoriesPreserved()
    {
        var context = await SeededContextAsync();
        await context.Recalls.InsertManyAsync(
            [
                Recall(1, "23V456000", "volkswagen|golf|2015", "GOLF", 2015, doNotDrive: false, parkOutside: true, potentiallyAffected: 1200),
                Recall(2, "23V456000", "volkswagen|golf|2016", "GOLF", 2016, doNotDrive: true, parkOutside: false, potentiallyAffected: 900)
            ],
            cancellationToken: CancellationToken.None);

        var detail = await new MongoRecallQueries(context).GetByCampaignNumberAsync("23V456000", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(2, detail.Vehicles.Count);
        Assert.True(detail.DoNotDrive);
        Assert.True(detail.ParkOutside);
        Assert.Equal(1200, detail.PotentiallyAffected);
    }

    [Fact]
    public async Task UnknownCampaignResolvesToNull()
    {
        var context = await SeededContextAsync();

        var detail = await new MongoRecallQueries(context).GetByCampaignNumberAsync("00V000000", CancellationToken.None);

        Assert.Null(detail);
    }

    private async Task<MongoContext> SeededContextAsync()
    {
        var context = new MongoContext(fixture.CreateClient(), $"queries_{Guid.NewGuid():N}");

        await context.Complaints.InsertManyAsync(
            [
                Complaint(1, "VOLKSWAGEN", "GOLF", 2015, "POWER TRAIN", new DateOnly(2021, 5, 4)),
                Complaint(2, "VOLKSWAGEN", "GOLF", 2015, "BRAKES", new DateOnly(2020, 3, 1)),
                Complaint(3, "VOLKSWAGEN", "GOLF", 2016, "ENGINE", new DateOnly(2019, 8, 9)),
                Complaint(4, "AUDI", "A3", 2015, "ENGINE", new DateOnly(2018, 2, 2))
            ],
            cancellationToken: CancellationToken.None);

        await new VehicleCatalogBuilder(context, NullLogger<VehicleCatalogBuilder>.Instance)
            .RebuildVehiclesAsync(CancellationToken.None);

        await context.Vehicles.InsertOneAsync(
            new VehicleCatalogEntry
            {
                Id = "volkswagen|passat|2014",
                Make = "VOLKSWAGEN",
                MakeSlug = "volkswagen",
                Model = "PASSAT",
                ModelSlug = "passat",
                ModelYear = 2014,
                ComplaintCount = 2
            },
            cancellationToken: CancellationToken.None);

        return context;
    }

    private static Complaint Complaint(
        int id,
        string make,
        string model,
        int modelYear,
        string componentGroup,
        DateOnly receivedDate) => new()
    {
        Id = id,
        OdiNumber = 13_000_000 + id,
        Manufacturer = "Fictional Motors Group",
        Make = make,
        MakeRaw = make,
        Model = model,
        ModelRaw = model,
        ModelYear = modelYear,
        Component = new ComponentPath { Raw = componentGroup, Group = componentGroup, Levels = [componentGroup] },
        MilesAtFailure = 42_000,
        Description = "Synthetic complaint written for the read side query tests.",
        ReceivedDate = receivedDate,
        VehicleKey = VehicleKey.Create(VehicleKey.Slug(make), VehicleKey.Slug(model), modelYear)
    };

    private static VehicleProfile Profile(
        string vehicleKey,
        string make,
        string model,
        int modelYear,
        int totalComplaints,
        int withMileage) => new()
    {
        Id = vehicleKey,
        VehicleKey = vehicleKey,
        Make = make,
        Model = model,
        ModelYear = modelYear,
        TotalComplaints = totalComplaints,
        WithMileage = withMileage,
        Components = [],
        MileageHistogram = MileageBuckets.Empty(),
        Timeline = [],
        Severity = new SeveritySignals
        {
            Crashes = 0,
            Fires = 0,
            Injured = 0,
            Deaths = 0,
            PoliceReports = 0,
            VehiclesTowed = 0,
            MedicalAttention = 0
        },
        ComputedAt = DateTime.UtcNow
    };

    private static RecallCampaign Recall(
        int id,
        string campaignNumber,
        string vehicleKey,
        string model,
        int modelYear,
        bool doNotDrive,
        bool parkOutside,
        int potentiallyAffected) => new()
    {
        Id = id,
        CampaignNumber = campaignNumber,
        Manufacturer = "Fictional Motors USA, LLC",
        Make = "VOLKSWAGEN",
        MakeRaw = "VOLKSWAGEN",
        Model = model,
        ModelRaw = model,
        ModelYear = modelYear,
        ComponentName = "POWER TRAIN",
        ComponentGroup = "POWER TRAIN",
        RecallType = "V",
        PotentiallyAffected = potentiallyAffected,
        DefectDescription = "Synthetic defect summary.",
        Consequence = "Synthetic consequence summary.",
        CorrectiveAction = "Synthetic corrective action.",
        DoNotDrive = doNotDrive,
        ParkOutside = parkOutside,
        VehicleKey = vehicleKey
    };
}
