using System.Text.RegularExpressions;
using GarageDoctor.Domain.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GarageDoctor.Infrastructure.Queries;

public sealed class MongoSearchQueries : ISearchQueries
{
    private const int CountCap = 5_000;
    private const int MaxPageSize = 50;

    private static readonly FilterDefinitionBuilder<Complaint> Filter = Builders<Complaint>.Filter;

    private readonly MongoContext _context;

    public MongoSearchQueries(MongoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<ComplaintSearchResult> SearchAsync(
        ComplaintSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var term = request.Term?.Trim();
        var mode = ResolveMode(term);

        if (mode == SearchMode.None && !request.HasFilters)
        {
            return new ComplaintSearchResult([], 0, true, mode, page, pageSize);
        }

        var filter = BuildFilter(request, term, mode);

        var complaints = await _context.Complaints
            .Find(filter)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var matchCount = await _context.Complaints
            .CountDocumentsAsync(filter, new CountOptions { Limit = CountCap }, cancellationToken)
            .ConfigureAwait(false);

        return new ComplaintSearchResult(
            complaints.Select(ToHit).ToList(),
            matchCount,
            matchCount < CountCap,
            mode,
            page,
            pageSize);
    }

    private static SearchMode ResolveMode(string? term) =>
        string.IsNullOrWhiteSpace(term) ? SearchMode.None : SearchMode.FullText;

    private static FilterDefinition<Complaint> BuildFilter(
        ComplaintSearchRequest request,
        string? term,
        SearchMode mode)
    {
        var clauses = new List<FilterDefinition<Complaint>>();

        if (mode == SearchMode.FullText && term is not null)
        {
            clauses.Add(Filter.Text(term));
        }

        if (!string.IsNullOrWhiteSpace(request.MakeSlug))
        {
            clauses.Add(Filter.Regex(
                complaint => complaint.VehicleKey,
                new BsonRegularExpression("^" + Regex.Escape(request.MakeSlug) + @"\|")));
        }

        if (!string.IsNullOrWhiteSpace(request.ComponentGroup))
        {
            clauses.Add(Filter.Eq(complaint => complaint.Component.Group, request.ComponentGroup));
        }

        if (request.YearFrom is { } from)
        {
            clauses.Add(Filter.Gte(complaint => complaint.ModelYear, from));
        }

        if (request.YearTo is { } to)
        {
            clauses.Add(Filter.Lte(complaint => complaint.ModelYear, to));
        }

        if (request.MileageOnly)
        {
            clauses.Add(Filter.Ne(complaint => complaint.MilesAtFailure, null));
        }

        return clauses.Count == 1 ? clauses[0] : Filter.And(clauses);
    }

    private static ComplaintSearchHit ToHit(Complaint complaint)
    {
        var segments = complaint.VehicleKey.Split('|');

        return new ComplaintSearchHit(
            complaint.Id,
            complaint.Make,
            segments.Length > 0 ? segments[0] : string.Empty,
            complaint.Model,
            segments.Length > 1 ? segments[1] : string.Empty,
            complaint.ModelYear,
            complaint.VehicleKey,
            complaint.ReceivedDate,
            complaint.MilesAtFailure,
            complaint.Component.Group,
            complaint.Component.Raw,
            complaint.Description,
            complaint.Crash,
            complaint.Fire,
            complaint.Injured,
            complaint.Deaths);
    }
}
