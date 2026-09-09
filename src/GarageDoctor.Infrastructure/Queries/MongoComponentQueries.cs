using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure.Queries;

/// <summary>
/// Reads the component taxonomy. Every number on the component pages, including the histogram and
/// both rankings, is precomputed by <see cref="VehicleCatalogBuilder.RebuildComponentsAsync"/> during
/// ingest, so a page here is one read of a collection that holds a few dozen small documents.
/// </summary>
public sealed class MongoComponentQueries : IComponentQueries
{
    private readonly MongoContext _context;

    public MongoComponentQueries(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<IReadOnlyList<ComponentGroupSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entries = await LoadAsync(cancellationToken).ConfigureAwait(false);

        return entries.Select(ToSummary).ToList();
    }

    public async Task<ComponentDetail?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var entries = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(candidate => VehicleKey.Slug(candidate.Group) == slug);

        return entry is null ? null : ToDetail(entry);
    }

    private Task<List<ComponentTaxonomyEntry>> LoadAsync(CancellationToken cancellationToken) =>
        _context.Components
            .Find(FilterDefinition<ComponentTaxonomyEntry>.Empty)
            .SortByDescending(entry => entry.ComplaintCount)
            .ToListAsync(cancellationToken);

    private static ComponentGroupSummary ToSummary(ComponentTaxonomyEntry entry) =>
        new(entry.Group, VehicleKey.Slug(entry.Group), entry.ComplaintCount, entry.TopLevels);

    private static ComponentDetail ToDetail(ComponentTaxonomyEntry entry) =>
        new(
            entry.Group,
            VehicleKey.Slug(entry.Group),
            entry.ComplaintCount,
            entry.TopLevels,
            entry.TopMakes
                .Select(make => new MakeSummary(make.Make, VehicleKey.Slug(make.Make), make.Count, 0))
                .ToList(),
            entry.TopVehicles
                .Select(vehicle => new ComponentVehicleRanking(
                    vehicle.VehicleKey,
                    vehicle.Make,
                    SlugAt(vehicle.VehicleKey, 0),
                    vehicle.Model,
                    SlugAt(vehicle.VehicleKey, 1),
                    vehicle.ModelYear,
                    vehicle.Count,
                    vehicle.TotalComplaints))
                .ToList(),
            entry.MileageHistogram,
            entry.WithMileage);

    private static string SlugAt(string vehicleKey, int position)
    {
        var segments = vehicleKey.Split('|');
        return position < segments.Length ? segments[position] : string.Empty;
    }
}
