using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class SearchQueriesTests(MongoFixture fixture)
{
    [Fact]
    public async Task FullTextSearchFindsNarrativesAndReportsTheMode()
    {
        var context = await SeededContextAsync();

        var result = await new MongoSearchQueries(context).SearchAsync(
            new ComplaintSearchRequest { Term = "shudder" },
            CancellationToken.None);

        Assert.Equal(SearchMode.FullText, result.Mode);
        Assert.Equal(2, result.MatchCount);
        Assert.True(result.MatchCountIsExact);
        Assert.All(result.Hits, hit => Assert.Contains("shudder", hit.Description, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FilteredSearchSwitchesToScopedMatchingAndNarrowsByMake()
    {
        var context = await SeededContextAsync();

        var result = await new MongoSearchQueries(context).SearchAsync(
            new ComplaintSearchRequest { Term = "shudder", MakeSlug = "volkswagen" },
            CancellationToken.None);

        Assert.Equal(SearchMode.ScopedPhrase, result.Mode);
        Assert.Single(result.Hits);
        Assert.Equal("VOLKSWAGEN", result.Hits[0].Make);
        Assert.Equal("volkswagen", result.Hits[0].MakeSlug);
    }

    [Fact]
    public async Task ScopedMatchingTreatsTheTermAsLiteralTextRatherThanAPattern()
    {
        var context = await SeededContextAsync();

        var result = await new MongoSearchQueries(context).SearchAsync(
            new ComplaintSearchRequest { Term = "shud.er", MakeSlug = "volkswagen" },
            CancellationToken.None);

        Assert.Empty(result.Hits);
    }

    [Fact]
    public async Task FiltersWithoutATermStillReturnMatches()
    {
        var context = await SeededContextAsync();

        var result = await new MongoSearchQueries(context).SearchAsync(
            new ComplaintSearchRequest { ComponentGroup = "POWER TRAIN", MileageOnly = true },
            CancellationToken.None);

        Assert.Equal(SearchMode.None, result.Mode);
        Assert.NotEmpty(result.Hits);
        Assert.All(result.Hits, hit => Assert.NotNull(hit.MilesAtFailure));
        Assert.All(result.Hits, hit => Assert.Equal("POWER TRAIN", hit.ComponentGroup));
    }

    [Fact]
    public async Task ModelYearBoundsAreInclusive()
    {
        var context = await SeededContextAsync();

        var result = await new MongoSearchQueries(context).SearchAsync(
            new ComplaintSearchRequest { YearFrom = 2016, YearTo = 2016 },
            CancellationToken.None);

        Assert.All(result.Hits, hit => Assert.Equal(2016, hit.ModelYear));
        Assert.NotEmpty(result.Hits);
    }

    [Fact]
    public async Task PagingWalksThroughMatchesWithoutRepeating()
    {
        var context = await SeededContextAsync();
        var queries = new MongoSearchQueries(context);

        var first = await queries.SearchAsync(
            new ComplaintSearchRequest { ComponentGroup = "POWER TRAIN", PageSize = 1, Page = 1 },
            CancellationToken.None);
        var second = await queries.SearchAsync(
            new ComplaintSearchRequest { ComponentGroup = "POWER TRAIN", PageSize = 1, Page = 2 },
            CancellationToken.None);

        Assert.Single(first.Hits);
        Assert.Single(second.Hits);
        Assert.NotEqual(first.Hits[0].Id, second.Hits[0].Id);
        Assert.True(first.HasNextPage);
        Assert.False(first.HasPreviousPage);
        Assert.True(second.HasPreviousPage);
    }

    [Fact]
    public async Task EmptyRequestReturnsNothingRatherThanTheWholeCollection()
    {
        var context = await SeededContextAsync();

        var result = await new MongoSearchQueries(context).SearchAsync(
            new ComplaintSearchRequest(),
            CancellationToken.None);

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.MatchCount);
    }

    [Fact]
    public async Task ComponentGroupsComeBackRankedWithSlugs()
    {
        var context = await SeededContextAsync();

        var groups = await new MongoComponentQueries(context).GetAllAsync(CancellationToken.None);

        Assert.Equal("POWER TRAIN", groups[0].Group);
        Assert.Equal("power-train", groups[0].Slug);
        Assert.True(groups[0].ComplaintCount >= groups[^1].ComplaintCount);
    }

    [Fact]
    public async Task ComponentDetailRanksMakesAndVehiclesAndBuildsAFullHistogram()
    {
        var context = await SeededContextAsync();

        var detail = await new MongoComponentQueries(context).GetBySlugAsync("power-train", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("POWER TRAIN", detail.Group);
        Assert.NotEmpty(detail.TopMakes);
        Assert.Equal("VOLKSWAGEN", detail.TopMakes[0].Make);
        Assert.NotEmpty(detail.TopVehicles);
        Assert.Equal(11, detail.MileageHistogram.Count);
        Assert.Equal(detail.MileageHistogram.Sum(bucket => bucket.Count), detail.WithMileage);
    }

    [Fact]
    public async Task UnknownComponentSlugResolvesToNull()
    {
        var context = await SeededContextAsync();

        var detail = await new MongoComponentQueries(context).GetBySlugAsync("flux-capacitor", CancellationToken.None);

        Assert.Null(detail);
    }

    private async Task<MongoContext> SeededContextAsync()
    {
        var context = new MongoContext(fixture.CreateClient(), $"search_{Guid.NewGuid():N}");

        await context.Complaints.InsertManyAsync(
            [
                Complaint(1, "VOLKSWAGEN", "GOLF", 2015, "POWER TRAIN", 30_000, "Transmission shudder under load."),
                Complaint(2, "AUDI", "A3", 2016, "POWER TRAIN", 60_000, "Gearbox shudder when pulling away."),
                Complaint(3, "AUDI", "A3", 2016, "ENGINE", null, "Coolant loss with no visible leak."),
                Complaint(4, "VOLKSWAGEN", "GOLF", 2015, "POWER TRAIN", null, "Clutch judder at low speed."),
                Complaint(5, "VOLKSWAGEN", "PASSAT", 2014, "BRAKES", 120_000, "Pedal travels to the floor.")
            ],
            cancellationToken: CancellationToken.None);

        await new IndexBuilder(context, NullLogger<IndexBuilder>.Instance).CreateAllAsync(CancellationToken.None);
        await new VehicleCatalogBuilder(context, NullLogger<VehicleCatalogBuilder>.Instance)
            .RebuildComponentsAsync(CancellationToken.None);
        await new ProfileBuilder(context, NullLogger<ProfileBuilder>.Instance)
            .RebuildAllAsync(CancellationToken.None);

        return context;
    }

    private static Complaint Complaint(
        int id,
        string make,
        string model,
        int modelYear,
        string componentGroup,
        int? milesAtFailure,
        string description) => new()
    {
        Id = id,
        OdiNumber = 14_000_000 + id,
        Manufacturer = "Fictional Motors Group",
        Make = make,
        MakeRaw = make,
        Model = model,
        ModelRaw = model,
        ModelYear = modelYear,
        Component = new ComponentPath { Raw = componentGroup, Group = componentGroup, Levels = [componentGroup] },
        MilesAtFailure = milesAtFailure,
        Description = description,
        ReceivedDate = new DateOnly(2021, 6, 1),
        VehicleKey = VehicleKey.Create(VehicleKey.Slug(make), VehicleKey.Slug(model), modelYear)
    };
}
