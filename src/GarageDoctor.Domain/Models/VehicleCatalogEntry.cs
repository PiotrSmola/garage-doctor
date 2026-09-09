namespace GarageDoctor.Domain.Models;

public sealed class VehicleCatalogEntry
{
    public required string Id { get; init; }
    public required string Make { get; init; }
    public required string MakeSlug { get; init; }
    public required string Model { get; init; }
    public required string ModelSlug { get; init; }
    public required int ModelYear { get; init; }
    public required int ComplaintCount { get; init; }
    public int RecallCount { get; init; }
}
