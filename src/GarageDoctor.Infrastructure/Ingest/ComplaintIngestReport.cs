namespace GarageDoctor.Infrastructure.Ingest;

public sealed class ComplaintIngestReport
{
    public required long LinesRead { get; init; }
    public required long BlankLinesSkipped { get; init; }
    public required long NonVehicleRecords { get; init; }
    public required long MappingFailures { get; init; }
    public required long DocumentsWritten { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IReadOnlyDictionary<string, int> RawMakeCounts { get; init; }
    public required IReadOnlyDictionary<string, int> CanonicalMakeCounts { get; init; }
}
