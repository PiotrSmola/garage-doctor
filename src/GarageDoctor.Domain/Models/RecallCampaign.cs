namespace GarageDoctor.Domain.Models;

public sealed class RecallCampaign
{
    public required int Id { get; init; }
    public required string CampaignNumber { get; init; }
    public required string Manufacturer { get; init; }
    public required string Make { get; init; }
    public required string MakeRaw { get; init; }
    public required string Model { get; init; }
    public required string ModelRaw { get; init; }
    public int? ModelYear { get; init; }
    public required string ComponentName { get; init; }
    public required string ComponentGroup { get; init; }
    public required string RecallType { get; init; }
    public int? PotentiallyAffected { get; init; }
    public DateOnly? OwnersNotifiedDate { get; init; }
    public DateOnly? ReportReceivedDate { get; init; }
    public required string DefectDescription { get; init; }
    public required string Consequence { get; init; }
    public required string CorrectiveAction { get; init; }
    public bool DoNotDrive { get; init; }
    public bool ParkOutside { get; init; }
    public required string VehicleKey { get; init; }
}
