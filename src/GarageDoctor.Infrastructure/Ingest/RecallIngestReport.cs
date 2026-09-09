namespace GarageDoctor.Infrastructure.Ingest;

public sealed class RecallIngestReport
{
    public required long LinesRead { get; init; }
    public required long BlankLinesSkipped { get; init; }
    public required long NonVehicleRecords { get; init; }
    public required long MappingFailures { get; init; }
    public required long DocumentsWritten { get; init; }
    public required long DoNotDriveCampaigns { get; init; }
    public required long ParkOutsideCampaigns { get; init; }
    public required TimeSpan Duration { get; init; }
}
