using GarageDoctor.Domain.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure.Queries;

public sealed class MongoVehicleCatalogQueries : IVehicleCatalogQueries
{
    private readonly MongoContext _context;

    public MongoVehicleCatalogQueries(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<CatalogOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var complaints = await _context.Complaints
            .EstimatedDocumentCountAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var recalls = await _context.Recalls
            .EstimatedDocumentCountAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var vehicles = await _context.Vehicles
            .EstimatedDocumentCountAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var totals = await _context.Profiles
            .Aggregate(new AggregateOptions { AllowDiskUse = true })
            .Group(new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "withMileage", new BsonDocument("$sum", "$withMileage") },
                { "inCatalogue", new BsonDocument("$sum", "$totalComplaints") },
                { "makes", new BsonDocument("$addToSet", "$make") },
                { "earliest", new BsonDocument("$min", "$modelYear") },
                { "latest", new BsonDocument("$max", "$modelYear") }
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var canonicalMakes = await _context.Complaints
            .DistinctAsync(complaint => complaint.Make, FilterDefinition<Complaint>.Empty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new CatalogOverview(
            complaints,
            recalls,
            vehicles,
            totals is null ? 0 : totals["inCatalogue"].ToInt64(),
            totals is null ? 0 : totals["makes"].AsBsonArray.Count,
            (await canonicalMakes.ToListAsync(cancellationToken).ConfigureAwait(false)).Count,
            totals is null ? 0 : totals["withMileage"].ToInt64(),
            totals is null || totals["earliest"].IsBsonNull ? null : totals["earliest"].ToInt32(),
            totals is null || totals["latest"].IsBsonNull ? null : totals["latest"].ToInt32());
    }

    public async Task<IReadOnlyList<MakeSummary>> GetMakesAsync(CancellationToken cancellationToken = default)
    {
        var results = await _context.Vehicles
            .Aggregate(new AggregateOptions { AllowDiskUse = true })
            .Group(new BsonDocument
            {
                { "_id", "$makeSlug" },
                { "make", new BsonDocument("$first", "$make") },
                { "complaintCount", new BsonDocument("$sum", "$complaintCount") },
                { "models", new BsonDocument("$addToSet", "$modelSlug") }
            })
            .Project(new BsonDocument
            {
                { "make", 1 },
                { "complaintCount", 1 },
                { "modelCount", new BsonDocument("$size", "$models") }
            })
            .Sort(new BsonDocument("complaintCount", -1))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return results
            .Select(document => new MakeSummary(
                document["make"].AsString,
                document["_id"].AsString,
                document["complaintCount"].ToInt64(),
                document["modelCount"].ToInt32()))
            .ToList();
    }

    public async Task<MakeDetail?> GetMakeAsync(string makeSlug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(makeSlug);

        var results = await _context.Vehicles
            .Aggregate(new AggregateOptions { AllowDiskUse = true })
            .Match(Builders<VehicleCatalogEntry>.Filter.Eq(entry => entry.MakeSlug, makeSlug))
            .Group(new BsonDocument
            {
                { "_id", "$modelSlug" },
                { "make", new BsonDocument("$first", "$make") },
                { "model", new BsonDocument("$first", "$model") },
                { "complaintCount", new BsonDocument("$sum", "$complaintCount") },
                { "earliest", new BsonDocument("$min", "$modelYear") },
                { "latest", new BsonDocument("$max", "$modelYear") }
            })
            .Sort(new BsonDocument("complaintCount", -1))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (results.Count == 0)
        {
            return null;
        }

        var models = results
            .Select(document => new ModelSummary(
                document["model"].AsString,
                document["_id"].AsString,
                document["complaintCount"].ToInt64(),
                document["earliest"].ToInt32(),
                document["latest"].ToInt32()))
            .ToList();

        return new MakeDetail(
            results[0]["make"].AsString,
            makeSlug,
            models.Sum(model => model.ComplaintCount),
            models);
    }

    public async Task<IReadOnlyList<ModelYearSummary>> GetModelYearsAsync(
        string makeSlug,
        string modelSlug,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(makeSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSlug);

        var entries = await _context.Vehicles
            .Find(Builders<VehicleCatalogEntry>.Filter.And(
                Builders<VehicleCatalogEntry>.Filter.Eq(entry => entry.MakeSlug, makeSlug),
                Builders<VehicleCatalogEntry>.Filter.Eq(entry => entry.ModelSlug, modelSlug)))
            .SortByDescending(entry => entry.ModelYear)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries
            .Select(entry => new ModelYearSummary(entry.ModelYear, entry.Id, entry.ComplaintCount))
            .ToList();
    }

    public async Task<VehicleIdentity?> ResolveVehicleAsync(
        string makeSlug,
        string modelSlug,
        int modelYear,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(makeSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSlug);

        var entry = await _context.Vehicles
            .Find(Builders<VehicleCatalogEntry>.Filter.Eq(
                vehicle => vehicle.Id,
                Domain.Canonicalization.VehicleKey.Create(makeSlug, modelSlug, modelYear)))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entry is null
            ? null
            : new VehicleIdentity(entry.Id, entry.Make, entry.MakeSlug, entry.Model, entry.ModelSlug, entry.ModelYear);
    }
}
