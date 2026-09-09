namespace GarageDoctor.Domain.Models;

public sealed class Complaint
{
    public required int Id { get; init; }
    public required int OdiNumber { get; init; }
    public required string Manufacturer { get; init; }
    public required string Make { get; init; }
    public required string MakeRaw { get; init; }
    public required string Model { get; init; }
    public required string ModelRaw { get; init; }
    public int? ModelYear { get; init; }
    public required ComponentPath Component { get; init; }
    public int? MilesAtFailure { get; init; }
    public required string Description { get; init; }
    public DateOnly? FailureDate { get; init; }
    public required DateOnly ReceivedDate { get; init; }
    public bool Crash { get; init; }
    public bool Fire { get; init; }
    public int Injured { get; init; }
    public int Deaths { get; init; }
    public bool PoliceReport { get; init; }
    public bool VehicleTowed { get; init; }
    public bool MedicalAttention { get; init; }
    public string? DriveTrain { get; init; }
    public string? FuelType { get; init; }
    public string? TransmissionType { get; init; }
    public int? Cylinders { get; init; }
    public required string VehicleKey { get; init; }
}
