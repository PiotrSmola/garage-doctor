using System.Diagnostics;
using System.Globalization;
using GarageDoctor.Infrastructure.Queries;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

[Route("search")]
public sealed class SearchController : Controller
{
    private const int ResultsPerPage = 20;
    private const long FrequentMakeThreshold = 10_000;

    private readonly ISearchQueries _search;
    private readonly IVehicleCatalogQueries _catalog;
    private readonly IComponentQueries _components;

    public SearchController(
        ISearchQueries search,
        IVehicleCatalogQueries catalog,
        IComponentQueries components)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(components);

        _search = search;
        _catalog = catalog;
        _components = components;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? term,
        string? makeSlug,
        string? componentGroup,
        int? yearFrom,
        int? yearTo,
        bool mileageOnly,
        int? page,
        CancellationToken cancellationToken)
    {
        var makesTask = _catalog.GetMakesAsync(cancellationToken);
        var groupsTask = _components.GetAllAsync(cancellationToken);

        await Task.WhenAll(makesTask, groupsTask).ConfigureAwait(false);

        var makes = await makesTask.ConfigureAwait(false);
        var groups = await groupsTask.ConfigureAwait(false);

        var requestedMake = Clean(makeSlug)?.ToLowerInvariant();
        var requestedGroup = Clean(componentGroup);

        var resolvedMake = requestedMake is null
            ? null
            : makes.FirstOrDefault(candidate => candidate.MakeSlug == requestedMake);

        var resolvedGroup = requestedGroup is null
            ? null
            : groups.FirstOrDefault(candidate =>
                string.Equals(candidate.Group, requestedGroup, StringComparison.OrdinalIgnoreCase));

        var (lower, upper) = OrderYears(yearFrom, yearTo);

        var form = new SearchFormViewModel
        {
            Term = Clean(term),
            MakeSlug = resolvedMake?.MakeSlug,
            ComponentGroup = resolvedGroup?.Group,
            YearFrom = lower,
            YearTo = upper,
            MileageOnly = mileageOnly
        };

        var currentPage = Math.Max(1, page ?? 1);

        var stopwatch = Stopwatch.StartNew();

        var result = await _search
            .SearchAsync(
                new ComplaintSearchRequest
                {
                    Term = form.Term,
                    MakeSlug = form.MakeSlug,
                    ComponentGroup = form.ComponentGroup,
                    YearFrom = form.YearFrom,
                    YearTo = form.YearTo,
                    MileageOnly = form.MileageOnly,
                    Page = currentPage,
                    PageSize = ResultsPerPage
                },
                cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();

        var makeOptions = makes
            .OrderBy(entry => entry.Make, StringComparer.Ordinal)
            .Select(entry => new SearchFilterOption(entry.MakeSlug, entry.Make))
            .ToList();

        var groupOptions = groups
            .Select(entry => new SearchFilterOption(entry.Group, GroupLabel(entry)))
            .ToList();

        return View(new SearchPageViewModel
        {
            Form = form,
            Makes = makeOptions,
            FrequentMakes = makes
                .Where(entry => entry.ComplaintCount >= FrequentMakeThreshold)
                .OrderBy(entry => entry.Make, StringComparer.Ordinal)
                .Select(entry => new SearchFilterOption(entry.MakeSlug, entry.Make))
                .ToList(),
            ComponentGroups = groupOptions,
            Examples = BuildExamples(makes, groups),
            Hits = result.Hits.Select(hit => SearchHitViewModel.From(hit, form.Term, result.Mode)).ToList(),
            Mode = result.Mode,
            MatchCount = result.MatchCount,
            MatchCountIsExact = result.MatchCountIsExact,
            Page = result.Page,
            PageSize = result.PageSize,
            LastPage = result.LastPage,
            HasPreviousPage = result.HasPreviousPage,
            HasNextPage = result.HasNextPage && (!result.MatchCountIsExact || result.Page < result.LastPage),
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            MakeLabel = resolvedMake?.Make,
            UnresolvedMake = resolvedMake is null ? requestedMake : null,
            UnresolvedComponentGroup = resolvedGroup is null ? requestedGroup : null
        });
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static (int? Lower, int? Upper) OrderYears(int? from, int? to) =>
        from is { } lower && to is { } upper && lower > upper ? (to, from) : (from, to);

    private static string GroupLabel(ComponentGroupSummary group) => string.Create(
        CultureInfo.InvariantCulture,
        $"{group.Group} · {group.ComplaintCount:N0}");

    private static IReadOnlyList<SearchExample> BuildExamples(
        IReadOnlyList<MakeSummary> makes,
        IReadOnlyList<ComponentGroupSummary> groups)
    {
        var examples = new List<SearchExample>
        {
            new("shudder, every make on file", "shudder", null, null)
        };

        if (makes.Count > 0)
        {
            examples.Add(new SearchExample(
                string.Create(CultureInfo.InvariantCulture, $"transmission slipping, {makes[0].Make} only"),
                "transmission slipping",
                makes[0].MakeSlug,
                null));
        }

        if (groups.Count > 0)
        {
            examples.Add(new SearchExample(
                string.Create(CultureInfo.InvariantCulture, $"leak, filed under {groups[0].Group}"),
                "leak",
                null,
                groups[0].Group));
        }

        return examples;
    }
}
