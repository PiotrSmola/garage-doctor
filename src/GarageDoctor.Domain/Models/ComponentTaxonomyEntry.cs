namespace GarageDoctor.Domain.Models;

public sealed class ComponentTaxonomyEntry
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required IReadOnlyList<string> TopLevels { get; init; }
    public required int ComplaintCount { get; init; }
    public required int WithMileage { get; init; }
    public required IReadOnlyList<MileageBucket> MileageHistogram { get; init; }
    public required IReadOnlyList<ComponentMakeStat> TopMakes { get; init; }
    public required IReadOnlyList<ComponentVehicleStat> TopVehicles { get; init; }
}

public sealed class ComponentMakeStat
{
    public required string Make { get; init; }
    public required int Count { get; init; }
}

public sealed class ComponentVehicleStat
{
    public required string VehicleKey { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required int ModelYear { get; init; }
    public required int Count { get; init; }
    public required int TotalComplaints { get; init; }
}
