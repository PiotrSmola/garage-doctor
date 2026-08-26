using GarageDoctor.Infrastructure.Queries;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

[Route("vehicles")]
public sealed class VehiclesController : Controller
{
    private const int RecentComplaintLimit = 6;

    private readonly IVehicleCatalogQueries _catalog;
    private readonly IVehicleProfileQueries _profiles;

    public VehiclesController(IVehicleCatalogQueries catalog, IVehicleProfileQueries profiles)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(profiles);

        _catalog = catalog;
        _profiles = profiles;
    }

    [HttpGet("{makeSlug}/{modelSlug}/{modelYear:int}", Name = "VehicleProfile")]
    public async Task<IActionResult> Profile(
        string makeSlug,
        string modelSlug,
        int modelYear,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(makeSlug) || string.IsNullOrWhiteSpace(modelSlug))
        {
            return NotFound();
        }

        var vehicle = await _catalog
            .ResolveVehicleAsync(makeSlug, modelSlug, modelYear, cancellationToken)
            .ConfigureAwait(false);

        if (vehicle is null)
        {
            return NotFound();
        }

        var profile = await _profiles
            .GetAsync(vehicle.VehicleKey, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return NotFound();
        }

        var recent = await _profiles
            .GetRecentComplaintsAsync(vehicle.VehicleKey, RecentComplaintLimit, cancellationToken)
            .ConfigureAwait(false);

        return View(new VehicleProfilePageViewModel
        {
            Vehicle = vehicle,
            Profile = profile,
            RecentComplaints = recent.Select(RecentComplaintViewModel.From).ToList()
        });
    }
}
