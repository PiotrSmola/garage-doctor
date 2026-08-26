using GarageDoctor.Infrastructure.Queries;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

[Route("makes")]
public sealed class MakesController : Controller
{
    private const long LeaderThreshold = 1000;
    private const long SingleFigureThreshold = 10;

    private readonly IVehicleCatalogQueries _catalog;

    public MakesController(IVehicleCatalogQueries catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var makes = await _catalog.GetMakesAsync(cancellationToken);
        var total = makes.Sum(entry => entry.ComplaintCount);

        return View(new MakesIndexViewModel
        {
            Leaders = makes
                .Where(entry => entry.ComplaintCount >= LeaderThreshold)
                .Select(entry => new MakeListing(
                    entry.MakeSlug,
                    entry.Make,
                    entry.ComplaintCount,
                    entry.ModelCount,
                    total))
                .ToList(),
            Tail = makes
                .Where(entry => entry.ComplaintCount < LeaderThreshold)
                .OrderBy(entry => entry.Make, StringComparer.Ordinal)
                .Select(entry => new MakeLink(entry.MakeSlug, entry.Make))
                .ToList(),
            LeaderThreshold = LeaderThreshold,
            TotalMakes = makes.Count,
            TotalComplaints = total,
            SingleFigureMakes = makes.Count(entry => entry.ComplaintCount < SingleFigureThreshold)
        });
    }

    [HttpGet("{makeSlug}")]
    public async Task<IActionResult> Details(string makeSlug, CancellationToken cancellationToken)
    {
        var slug = makeSlug?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(slug))
        {
            return NotFound();
        }

        var detail = await _catalog.GetMakeAsync(slug, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        return View(new MakeDetailsViewModel
        {
            Make = detail.Make,
            MakeSlug = detail.MakeSlug,
            ComplaintCount = detail.ComplaintCount,
            EarliestModelYear = detail.Models.Min(entry => entry.EarliestModelYear),
            LatestModelYear = detail.Models.Max(entry => entry.LatestModelYear),
            Models = detail.Models
                .Select(entry => new ModelListing(
                    entry.ModelSlug,
                    entry.Model,
                    entry.ComplaintCount,
                    entry.EarliestModelYear,
                    entry.LatestModelYear,
                    detail.ComplaintCount))
                .ToList()
        });
    }
}
