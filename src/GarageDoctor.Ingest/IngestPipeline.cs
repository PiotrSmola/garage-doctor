using System.Text.Json;
using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Infrastructure;
using GarageDoctor.Infrastructure.Ingest;
using Microsoft.Extensions.Logging;

namespace GarageDoctor.Ingest;

public sealed class IngestPipeline
{
    private const int RareMakeThreshold = 10;

    private static readonly JsonSerializerOptions ReportSerialization = new() { WriteIndented = true };

    private readonly MongoContext _context;
    private readonly NhtsaDataFiles _dataFiles;
    private readonly ComplaintIngestService _complaintIngest;
    private readonly RecallIngestService _recallIngest;
    private readonly VehicleCatalogBuilder _catalogBuilder;
    private readonly IndexBuilder _indexBuilder;
    private readonly ProfileBuilder _profileBuilder;
    private readonly ComponentCanonicalizer _componentCanonicalizer;
    private readonly ILogger<IngestPipeline> _logger;

    public IngestPipeline(
        MongoContext context,
        NhtsaDataFiles dataFiles,
        ComplaintIngestService complaintIngest,
        RecallIngestService recallIngest,
        VehicleCatalogBuilder catalogBuilder,
        IndexBuilder indexBuilder,
        ProfileBuilder profileBuilder,
        ComponentCanonicalizer componentCanonicalizer,
        ILogger<IngestPipeline> logger)
    {
        _context = context;
        _dataFiles = dataFiles;
        _complaintIngest = complaintIngest;
        _recallIngest = recallIngest;
        _catalogBuilder = catalogBuilder;
        _indexBuilder = indexBuilder;
        _profileBuilder = profileBuilder;
        _componentCanonicalizer = componentCanonicalizer;
        _logger = logger;
    }

    public async Task RunAsync(IngestArguments arguments, CancellationToken cancellationToken = default)
    {
        await _dataFiles.EnsureAsync(cancellationToken).ConfigureAwait(false);

        if (arguments.Resume)
        {
            _logger.LogInformation("Resuming into existing collections");
        }
        else
        {
            await DropCollectionsAsync(cancellationToken).ConfigureAwait(false);
        }

        var complaints = await _complaintIngest
            .IngestAsync(_dataFiles.ComplaintsFile, arguments.RecordLimit, cancellationToken)
            .ConfigureAwait(false);

        if (arguments.SkipRecalls)
        {
            _logger.LogInformation("Skipping recalls");
        }
        else
        {
            await _recallIngest.IngestAsync(_dataFiles.RecallFiles, cancellationToken).ConfigureAwait(false);
        }

        var vehicles = await _catalogBuilder.RebuildVehiclesAsync(cancellationToken).ConfigureAwait(false);
        var components = await _catalogBuilder.RebuildComponentsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Catalog rebuilt: {VehicleCount} vehicles, {ComponentCount} component groups", vehicles, components);

        await _indexBuilder.CreateAllAsync(cancellationToken).ConfigureAwait(false);

        var profiles = await _profileBuilder.RebuildAllAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Precomputed {ProfileCount} vehicle profiles", profiles);

        await WriteReportsAsync(arguments.DataDirectory, complaints, cancellationToken).ConfigureAwait(false);

        var report = await new AcceptanceReport(_context).RenderAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine(report);
    }

    private async Task DropCollectionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Dropping target collections");

        string[] collections =
        [
            CollectionNames.Complaints,
            CollectionNames.Recalls,
            CollectionNames.Vehicles,
            CollectionNames.Components,
            CollectionNames.Profiles
        ];

        foreach (var name in collections)
        {
            await _context.Database.DropCollectionAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteReportsAsync(string dataDirectory, ComplaintIngestReport complaints, CancellationToken cancellationToken)
    {
        var rareMakes = complaints.RawMakeCounts
            .Where(entry => entry.Value < RareMakeThreshold)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        await WriteJsonAsync(
            Path.Combine(dataDirectory, "rare-makes.json"),
            new
            {
                generatedAt = DateTime.UtcNow,
                threshold = RareMakeThreshold,
                count = rareMakes.Count,
                makes = rareMakes
            },
            cancellationToken).ConfigureAwait(false);

        var unmapped = _componentCanonicalizer.UnmappedTopLevels
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        await WriteJsonAsync(
            Path.Combine(dataDirectory, "unmapped-components.json"),
            new
            {
                generatedAt = DateTime.UtcNow,
                count = unmapped.Count,
                topLevels = unmapped
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Wrote reports: {RareMakeCount} rare makes, {UnmappedCount} unmapped component categories",
            rareMakes.Count,
            unmapped.Count);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, ReportSerialization, cancellationToken).ConfigureAwait(false);
    }
}
