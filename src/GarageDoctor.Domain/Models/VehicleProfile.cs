namespace GarageDoctor.Domain.Models;

public sealed class VehicleProfile
{
    public required string Id { get; init; }
    public required string VehicleKey { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required int ModelYear { get; init; }
    public required int TotalComplaints { get; init; }
    public required int WithMileage { get; init; }
    public int? AverageMilesAtFailure { get; init; }
    public required IReadOnlyList<ComponentStat> Components { get; init; }
    public required IReadOnlyList<MileageBucket> MileageHistogram { get; init; }
    public required IReadOnlyList<YearCount> Timeline { get; init; }
    public required SeveritySignals Severity { get; init; }
    public required DateTime ComputedAt { get; init; }
}

public sealed class ComponentStat
{
    public required string Group { get; init; }
    public required int Count { get; init; }
}

public sealed class MileageBucket
{
    public required int From { get; init; }
    public int? To { get; init; }
    public required int Count { get; init; }
}

public sealed class YearCount
{
    public required int Year { get; init; }
    public required int Count { get; init; }
}

public sealed class SeveritySignals
{
    public required int Crashes { get; init; }
    public required int Fires { get; init; }
    public required int Injured { get; init; }
    public required int Deaths { get; init; }
    public required int PoliceReports { get; init; }
    public required int VehiclesTowed { get; init; }
    public required int MedicalAttention { get; init; }
}
