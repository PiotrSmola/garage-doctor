using GarageDoctor.Infrastructure.Queries;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

[Route("about")]
public sealed class AboutController : Controller
{
    private readonly IVehicleCatalogQueries _catalog;

    public AboutController(IVehicleCatalogQueries catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var overview = await _catalog.GetOverviewAsync(cancellationToken);

        return View(overview);
    }
}
