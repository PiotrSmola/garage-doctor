namespace GarageDoctor.Domain.Models;

public sealed class ComponentTaxonomyEntry
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required IReadOnlyList<string> TopLevels { get; init; }
    public required int ComplaintCount { get; init; }
}
