using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Domain.Parsing;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Ingest;
using GarageDoctor.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class ComplaintIngestServiceTests(MongoFixture fixture)
{
    private const int SampleRows = 50_000;

    private static readonly string[] PersonalDataElements =
        ["dealerName", "dealerTel", "dealerCity", "dealerState", "dealerZip", "vin", "vehicleOperator", "city"];

    [Fact]
    public async Task IngestsOnlyVehicleComplaintsFromAFiftyThousandRowSample()
    {
        using var file = SyntheticComplaintFile.Create(SampleRows);
        var context = CreateContext();
        var service = CreateService(context);

        var report = await service.IngestAsync(file.Path, cancellationToken: CancellationToken.None);

        Assert.Equal(SampleRows, report.LinesRead);
        Assert.Equal(file.NonVehicleRows, report.NonVehicleRecords);
        Assert.Equal(0, report.MappingFailures);
        Assert.Equal(file.VehicleRows, report.DocumentsWritten);
        Assert.Equal(file.VehicleRows, await CountAsync(context));
    }

    [Fact]
    public async Task SecondIngestReplacesInsteadOfDuplicating()
    {
        using var file = SyntheticComplaintFile.Create(5_000);
        var context = CreateContext();
        var service = CreateService(context);

        await service.IngestAsync(file.Path, cancellationToken: CancellationToken.None);
        var countAfterFirstRun = await CountAsync(context);

        var secondReport = await service.IngestAsync(file.Path, cancellationToken: CancellationToken.None);

        Assert.Equal(countAfterFirstRun, await CountAsync(context));
        Assert.Equal(countAfterFirstRun, secondReport.DocumentsWritten);
    }

    [Fact]
    public async Task StoresNoPersonalDataAlthoughTheSourceRowsCarryIt()
    {
        using var file = SyntheticComplaintFile.Create(1_000);
        var context = CreateContext();
        var service = CreateService(context);

        await service.IngestAsync(file.Path, cancellationToken: CancellationToken.None);

        var documents = await context.Database
            .GetCollection<BsonDocument>(CollectionNames.Complaints)
            .Find(new BsonDocument())
            .Limit(50)
            .ToListAsync(CancellationToken.None);

        Assert.NotEmpty(documents);
        foreach (var document in documents)
        {
            foreach (var element in PersonalDataElements)
            {
                Assert.DoesNotContain(document.Names, name => string.Equals(name, element, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public async Task CanonicalizesMakesWhileIngesting()
    {
        using var file = SyntheticComplaintFile.Create(1_000);
        var context = CreateContext();
        var service = CreateService(context);

        var report = await service.IngestAsync(file.Path, cancellationToken: CancellationToken.None);

        Assert.Contains("VW", report.RawMakeCounts.Keys);
        Assert.Contains("VOLKSWAGEN", report.RawMakeCounts.Keys);
        Assert.DoesNotContain("VW", report.CanonicalMakeCounts.Keys);

        var volkswagen = await context.Complaints
            .CountDocumentsAsync(Builders<Complaint>.Filter.Eq(complaint => complaint.Make, "VOLKSWAGEN"),
                cancellationToken: CancellationToken.None);
        Assert.Equal(report.CanonicalMakeCounts["VOLKSWAGEN"], volkswagen);

        var sample = await context.Complaints
            .Find(Builders<Complaint>.Filter.Eq(complaint => complaint.MakeRaw, "VW"))
            .FirstAsync(CancellationToken.None);
        Assert.Equal("VOLKSWAGEN", sample.Make);
        Assert.StartsWith("volkswagen|golf|", sample.VehicleKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopsAtTheRequestedRecordLimit()
    {
        using var file = SyntheticComplaintFile.Create(SampleRows);
        var context = CreateContext();
        var service = CreateService(context);

        var report = await service.IngestAsync(file.Path, recordLimit: 10_000, cancellationToken: CancellationToken.None);

        Assert.Equal(10_000, report.DocumentsWritten);
        Assert.Equal(10_000, await CountAsync(context));
        Assert.True(report.LinesRead < SampleRows, "the parser must stop reading once the limit is reached");
    }

    private MongoContext CreateContext() =>
        new(fixture.CreateClient(), $"ingest-{Guid.NewGuid():N}");

    private static ComplaintIngestService CreateService(MongoContext context) =>
        new(context, CreateMapper(), NullLogger<ComplaintIngestService>.Instance);

    private static ComplaintMapper CreateMapper() =>
        new(MakeCanonicalizer.Load(TestPaths.DataFile("make-aliases.json")),
            new ModelCanonicalizer(),
            ComponentCanonicalizer.Load(TestPaths.DataFile("component-groups.json")));

    private static async Task<long> CountAsync(MongoContext context) =>
        await context.Complaints.CountDocumentsAsync(FilterDefinition<Complaint>.Empty);
}
