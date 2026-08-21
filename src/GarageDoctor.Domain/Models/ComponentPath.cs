namespace GarageDoctor.Domain.Models;

public sealed class ComponentPath
{
    public required string Raw { get; init; }
    public required string Group { get; init; }
    public required IReadOnlyList<string> Levels { get; init; }
}
