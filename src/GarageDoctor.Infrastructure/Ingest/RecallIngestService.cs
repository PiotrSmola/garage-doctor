using System.Diagnostics;
using GarageDoctor.Domain.Models;
using GarageDoctor.Domain.Parsing;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure.Ingest;

public sealed class RecallIngestService
{
    private const int BatchSize = 10_000;

    private static readonly BulkWriteOptions UnorderedWrite = new() { IsOrdered = false };

    private readonly MongoContext _context;
    private readonly RecallMapper _mapper;
    private readonly ILogger<RecallIngestService> _logger;

    public RecallIngestService(MongoContext context, RecallMapper mapper, ILogger<RecallIngestService> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<RecallIngestReport> IngestAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var batch = new List<WriteModel<RecallCampaign>>(BatchSize);
        var stopwatch = Stopwatch.StartNew();

        long linesRead = 0;
        long blankLinesSkipped = 0;
        long nonVehicleRecords = 0;
        long mappingFailures = 0;
        long documentsWritten = 0;
        long doNotDriveCampaigns = 0;
        long parkOutsideCampaigns = 0;

        foreach (var filePath in filePaths)
        {
            _logger.LogInformation("Ingesting recalls from {FilePath}", filePath);

            var parser = FlatFileParser.ForRecalls();
            var progress = new SynchronousProgress<long>(lines =>
                _logger.LogInformation("Read {LineCount} recall lines", lines));

            await foreach (var fields in parser.ReadRecordsAsync(filePath, progress, cancellationToken).ConfigureAwait(false))
            {
                if (!_mapper.IsVehicleRecall(fields))
                {
                    nonVehicleRecords++;
                    continue;
                }

                RecallCampaign recall;
                try
                {
                    recall = _mapper.Map(fields);
                }
                catch (RecallMappingException exception)
                {
                    mappingFailures++;
                    _logger.LogDebug(exception, "Skipped recall record {RecordId}", exception.RecordId);
                    continue;
                }

                if (recall.DoNotDrive)
                {
                    doNotDriveCampaigns++;
                }

                if (recall.ParkOutside)
                {
                    parkOutsideCampaigns++;
                }

                batch.Add(new ReplaceOneModel<RecallCampaign>(
                    Builders<RecallCampaign>.Filter.Eq(existing => existing.Id, recall.Id),
                    recall)
                {
                    IsUpsert = true
                });

                if (batch.Count >= BatchSize)
                {
                    documentsWritten += await FlushAsync(batch, cancellationToken).ConfigureAwait(false);
                }
            }

            linesRead += parser.LinesRead;
            blankLinesSkipped += parser.BlankLinesSkipped;

            _logger.LogInformation(
                "Finished {FilePath}: {LineCount} lines, {BlankCount} blank lines skipped",
                filePath,
                parser.LinesRead,
                parser.BlankLinesSkipped);
        }

        documentsWritten += await FlushAsync(batch, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        _logger.LogInformation(
            "Ingested {DocumentCount} recall rows in {ElapsedSeconds:F1}s, {DoNotDriveCount} do-not-drive and {ParkOutsideCount} park-outside rows",
            documentsWritten,
            stopwatch.Elapsed.TotalSeconds,
            doNotDriveCampaigns,
            parkOutsideCampaigns);

        return new RecallIngestReport
        {
            LinesRead = linesRead,
            BlankLinesSkipped = blankLinesSkipped,
            NonVehicleRecords = nonVehicleRecords,
            MappingFailures = mappingFailures,
            DocumentsWritten = documentsWritten,
            DoNotDriveCampaigns = doNotDriveCampaigns,
            ParkOutsideCampaigns = parkOutsideCampaigns,
            Duration = stopwatch.Elapsed
        };
    }

    private async Task<long> FlushAsync(List<WriteModel<RecallCampaign>> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        var result = await _context.Recalls
            .BulkWriteAsync(batch, UnorderedWrite, cancellationToken)
            .ConfigureAwait(false);

        var written = result.MatchedCount + result.Upserts.Count;
        batch.Clear();
        return written;
    }
}
