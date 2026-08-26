using GarageDoctor.Infrastructure.Queries;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

public sealed class HomeController : Controller
{
    private const int RankedMakeCount = 12;

    private const long FrequentMakeThreshold = 10_000;

    private const int RankedVehicleCount = 10;

    private const int RankedComponentCount = 10;

    private const int AdvisoryCount = 5;

    private readonly IVehicleCatalogQueries _catalog;
    private readonly IRecallQueries _recalls;

    public HomeController(IVehicleCatalogQueries catalog, IRecallQueries recalls)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(recalls);
        _catalog = catalog;
        _recalls = recalls;
    }

    [HttpGet("/")]
    public async Task<IActionResult> Index(
        string? make,
        string? model,
        int? year,
        CancellationToken cancellationToken)
    {
        var makeSlug = Slug(make);
        var modelSlug = Slug(model);

        var selectedMake = makeSlug is null
            ? null
            : await _catalog.GetMakeAsync(makeSlug, cancellationToken);

        var selectedModel = selectedMake is null || modelSlug is null
            ? null
            : selectedMake.Models.FirstOrDefault(candidate => candidate.ModelSlug == modelSlug);

        IReadOnlyList<ModelYearSummary> modelYears = selectedMake is null || selectedModel is null
            ? []
            : await _catalog.GetModelYearsAsync(
                selectedMake.MakeSlug,
                selectedModel.ModelSlug,
                cancellationToken);

        if (selectedMake is not null
            && selectedModel is not null
            && year is { } requestedYear
            && modelYears.Any(candidate => candidate.ModelYear == requestedYear))
        {
            return Redirect(ProfilePath(selectedMake.MakeSlug, selectedModel.ModelSlug, requestedYear));
        }

        var makes = await _catalog.GetMakesAsync(cancellationToken);
        var overview = await _catalog.GetOverviewAsync(cancellationToken);
        var vehicles = await _catalog.GetMostReportedVehiclesAsync(RankedVehicleCount, cancellationToken);
        var components = await _catalog.GetComponentGroupsAsync(cancellationToken);
        var advisories = await _recalls.GetLatestAdvisoriesAsync(AdvisoryCount, cancellationToken);
        var ranked = makes.Take(RankedMakeCount).ToList();
        var leaderCount = ranked.Count == 0 ? 0 : ranked[0].ComplaintCount;
        var rankedComponents = components.Take(RankedComponentCount).ToList();
        var componentLeader = rankedComponents.Count == 0 ? 0 : rankedComponents[0].ComplaintCount;

        return View(new HomeIndexViewModel
        {
            Overview = overview,
            TopMakes = ranked
                .Select(entry => new MakeRanking(entry.MakeSlug, entry.Make, entry.ComplaintCount, leaderCount))
                .ToList(),
            TopVehicles = vehicles,
            TopComponents = rankedComponents
                .Select(entry => new ComponentRanking(
                    entry.Group,
                    entry.Slug,
                    entry.ComplaintCount,
                    componentLeader,
                    overview.Complaints))
                .ToList(),
            Advisories = advisories,
            Cascade = new SearchCascade
            {
                Makes = makes
                    .OrderBy(entry => entry.Make, StringComparer.Ordinal)
                    .Select(entry => new CascadeMake(entry.MakeSlug, entry.Make))
                    .ToList(),
                FrequentMakes = makes
                    .Where(entry => entry.ComplaintCount >= FrequentMakeThreshold)
                    .OrderBy(entry => entry.Make, StringComparer.Ordinal)
                    .Select(entry => new CascadeMake(entry.MakeSlug, entry.Make))
                    .ToList(),
                Models = selectedMake is null
                    ? []
                    : selectedMake.Models
                        .Select(entry => new CascadeModel(entry.ModelSlug, entry.Model, entry.ComplaintCount))
                        .ToList(),
                Years = modelYears
                    .Select(entry => new CascadeYear(entry.ModelYear, entry.ComplaintCount))
                    .ToList(),
                SelectedMakeSlug = selectedMake?.MakeSlug,
                SelectedMakeName = selectedMake?.Make,
                SelectedModelSlug = selectedModel?.ModelSlug,
                SelectedModelName = selectedModel?.Model,
                UnresolvedMake = selectedMake is null ? makeSlug : null
            }
        });
    }

    private static string ProfilePath(string makeSlug, string modelSlug, int modelYear) =>
        $"/vehicles/{Uri.EscapeDataString(makeSlug)}/{Uri.EscapeDataString(modelSlug)}/{modelYear}";

    private static string? Slug(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }
}
