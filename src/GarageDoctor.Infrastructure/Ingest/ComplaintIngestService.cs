using System.Diagnostics;
using GarageDoctor.Domain.Models;
using GarageDoctor.Domain.Parsing;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure.Ingest;

public sealed class ComplaintIngestService
{
    private const int BatchSize = 10_000;

    private static readonly BulkWriteOptions UnorderedWrite = new() { IsOrdered = false };

    private readonly MongoContext _context;
    private readonly ComplaintMapper _mapper;
    private readonly ILogger<ComplaintIngestService> _logger;

    public ComplaintIngestService(MongoContext context, ComplaintMapper mapper, ILogger<ComplaintIngestService> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<ComplaintIngestReport> IngestAsync(
        string filePath,
        long? recordLimit = null,
        CancellationToken cancellationToken = default)
    {
        var parser = FlatFileParser.ForComplaints();
        var rawMakeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var canonicalMakeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var batch = new List<WriteModel<Complaint>>(BatchSize);
        var stopwatch = Stopwatch.StartNew();
        var progress = new SynchronousProgress<long>(lines =>
            _logger.LogInformation("Read {LineCount} complaint lines", lines));

        long nonVehicleRecords = 0;
        long mappingFailures = 0;
        long documentsWritten = 0;

        _logger.LogInformation("Ingesting complaints from {FilePath}", filePath);

        await foreach (var fields in parser.ReadRecordsAsync(filePath, progress, cancellationToken).ConfigureAwait(false))
        {
            if (!_mapper.IsVehicleComplaint(fields))
            {
                nonVehicleRecords++;
                continue;
            }

            Complaint complaint;
            try
            {
                complaint = _mapper.Map(fields);
            }
            catch (ComplaintMappingException exception)
            {
                mappingFailures++;
                _logger.LogDebug(exception, "Skipped complaint {ComplaintId}", exception.ComplaintId);
                continue;
            }

            Increment(rawMakeCounts, complaint.MakeRaw);
            Increment(canonicalMakeCounts, complaint.Make);

            batch.Add(new ReplaceOneModel<Complaint>(
                Builders<Complaint>.Filter.Eq(existing => existing.Id, complaint.Id),
                complaint)
            {
                IsUpsert = true
            });

            if (batch.Count >= BatchSize)
            {
                documentsWritten += await FlushAsync(batch, cancellationToken).ConfigureAwait(false);
            }

            if (recordLimit is { } limit && documentsWritten + batch.Count >= limit)
            {
                break;
            }
        }

        documentsWritten += await FlushAsync(batch, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        _logger.LogInformation(
            "Ingested {DocumentCount} complaints in {ElapsedSeconds:F1}s, skipped {NonVehicleCount} non-vehicle records and {FailureCount} unmappable records",
            documentsWritten,
            stopwatch.Elapsed.TotalSeconds,
            nonVehicleRecords,
            mappingFailures);

        return new ComplaintIngestReport
        {
            LinesRead = parser.LinesRead,
            BlankLinesSkipped = parser.BlankLinesSkipped,
            NonVehicleRecords = nonVehicleRecords,
            MappingFailures = mappingFailures,
            DocumentsWritten = documentsWritten,
            Duration = stopwatch.Elapsed,
            RawMakeCounts = rawMakeCounts,
            CanonicalMakeCounts = canonicalMakeCounts
        };
    }

    private async Task<long> FlushAsync(List<WriteModel<Complaint>> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        var result = await _context.Complaints
            .BulkWriteAsync(batch, UnorderedWrite, cancellationToken)
            .ConfigureAwait(false);

        var written = result.MatchedCount + result.Upserts.Count;
        batch.Clear();
        return written;
    }

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
}
