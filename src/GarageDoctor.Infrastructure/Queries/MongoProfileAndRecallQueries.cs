using GarageDoctor.Domain.Models;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure.Queries;

public sealed class MongoVehicleProfileQueries : IVehicleProfileQueries
{
    private readonly MongoContext _context;

    public MongoVehicleProfileQueries(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<VehicleProfile?> GetAsync(string vehicleKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleKey);

        return await _context.Profiles
            .Find(Builders<VehicleProfile>.Filter.Eq(profile => profile.VehicleKey, vehicleKey))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ComplaintSummary>> GetRecentComplaintsAsync(
        string vehicleKey,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var complaints = await _context.Complaints
            .Find(Builders<Complaint>.Filter.Eq(complaint => complaint.VehicleKey, vehicleKey))
            .SortByDescending(complaint => complaint.ReceivedDate)
            .Limit(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return complaints
            .Select(complaint => new ComplaintSummary(
                complaint.Id,
                complaint.ReceivedDate,
                complaint.MilesAtFailure,
                complaint.Component.Group,
                complaint.Component.Raw,
                complaint.Description,
                complaint.Crash,
                complaint.Fire,
                complaint.Injured,
                complaint.Deaths))
            .ToList();
    }
}

public sealed class MongoRecallQueries : IRecallQueries
{
    private const int VehicleListLimit = 400;

    private readonly MongoContext _context;

    public MongoRecallQueries(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<IReadOnlyList<RecallCampaign>> GetForVehicleAsync(
        string vehicleKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleKey);

        return await _context.Recalls
            .Find(Builders<RecallCampaign>.Filter.Eq(recall => recall.VehicleKey, vehicleKey))
            .SortByDescending(recall => recall.ReportReceivedDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RecallCampaignDetail?> GetByCampaignNumberAsync(
        string campaignNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignNumber);

        var rows = await _context.Recalls
            .Find(Builders<RecallCampaign>.Filter.Eq(recall => recall.CampaignNumber, campaignNumber))
            .Limit(VehicleListLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return null;
        }

        var first = rows[0];
        var vehicles = rows
            .Select(row => new RecalledVehicle(row.Make, row.Model, row.ModelYear, row.VehicleKey))
            .DistinctBy(vehicle => vehicle.VehicleKey)
            .OrderBy(vehicle => vehicle.Make, StringComparer.Ordinal)
            .ThenBy(vehicle => vehicle.Model, StringComparer.Ordinal)
            .ThenByDescending(vehicle => vehicle.ModelYear)
            .ToList();

        return new RecallCampaignDetail(
            first.CampaignNumber,
            first.Manufacturer,
            first.ComponentName,
            first.ComponentGroup,
            first.DefectDescription,
            first.Consequence,
            first.CorrectiveAction,
            rows.Any(row => row.DoNotDrive),
            rows.Any(row => row.ParkOutside),
            rows.Max(row => row.PotentiallyAffected),
            first.ReportReceivedDate,
            first.OwnersNotifiedDate,
            vehicles);
    }
}
