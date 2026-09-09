using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Domain.Parsing;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Ingest;
using GarageDoctor.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class RecallIngestServiceTests(MongoFixture fixture)
{
    [Fact]
    public async Task IngestsVehicleRecallsFromBothFilesAndSkipsBlankLines()
    {
        using var files = SyntheticRecallFile.Create(500);
        var context = CreateContext();
        var service = CreateService(context);

        var report = await service.IngestAsync(files.Paths, CancellationToken.None);

        Assert.Equal(files.VehicleRows, report.DocumentsWritten);
        Assert.Equal(files.VehicleRows, await CountAsync(context));
        Assert.Equal(2, report.BlankLinesSkipped);
        Assert.Equal(0, report.MappingFailures);
    }

    [Fact]
    public async Task CountsDoNotDriveAndParkOutsideAdvisoriesDespiteCarriageReturns()
    {
        using var files = SyntheticRecallFile.Create(500);
        var context = CreateContext();
        var service = CreateService(context);

        var report = await service.IngestAsync(files.Paths, CancellationToken.None);

        Assert.Equal(files.DoNotDriveRows, report.DoNotDriveCampaigns);
        Assert.Equal(files.ParkOutsideRows, report.ParkOutsideCampaigns);

        var stored = await context.Recalls
            .CountDocumentsAsync(Builders<RecallCampaign>.Filter.Eq(recall => recall.ParkOutside, true),
                cancellationToken: CancellationToken.None);
        Assert.Equal(files.ParkOutsideRows, stored);
    }

    [Fact]
    public async Task SecondIngestReplacesInsteadOfDuplicating()
    {
        using var files = SyntheticRecallFile.Create(200);
        var context = CreateContext();
        var service = CreateService(context);

        await service.IngestAsync(files.Paths, CancellationToken.None);
        var countAfterFirstRun = await CountAsync(context);

        await service.IngestAsync(files.Paths, CancellationToken.None);

        Assert.Equal(countAfterFirstRun, await CountAsync(context));
    }

    [Fact]
    public async Task LandsRecallsOnTheSameVehicleKeyShapeAsComplaints()
    {
        using var files = SyntheticRecallFile.Create(100);
        var context = CreateContext();
        var service = CreateService(context);

        await service.IngestAsync(files.Paths, CancellationToken.None);

        var mercedes = await context.Recalls
            .Find(Builders<RecallCampaign>.Filter.Eq(recall => recall.MakeRaw, "MERCEDES BENZ"))
            .FirstAsync(CancellationToken.None);

        Assert.Equal("MERCEDES-BENZ", mercedes.Make);
        Assert.StartsWith("mercedes-benz|c300|", mercedes.VehicleKey, StringComparison.Ordinal);
        Assert.Equal("POWER TRAIN", mercedes.ComponentGroup);
    }

    private MongoContext CreateContext() =>
        new(fixture.CreateClient(), $"recalls-{Guid.NewGuid():N}");

    private static RecallIngestService CreateService(MongoContext context) =>
        new(context, CreateMapper(), NullLogger<RecallIngestService>.Instance);

    private static RecallMapper CreateMapper() =>
        new(MakeCanonicalizer.Load(TestPaths.DataFile("make-aliases.json")),
            new ModelCanonicalizer(),
            ComponentCanonicalizer.Load(TestPaths.DataFile("component-groups.json")));

    private static async Task<long> CountAsync(MongoContext context) =>
        await context.Recalls.CountDocumentsAsync(FilterDefinition<RecallCampaign>.Empty);
}
