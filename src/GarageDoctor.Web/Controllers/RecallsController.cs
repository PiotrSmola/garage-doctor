using GarageDoctor.Infrastructure.Queries;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

[Route("recalls")]
public sealed class RecallsController : Controller
{
    private const int ModelRowLimit = 60;

    private readonly IRecallQueries _recalls;

    public RecallsController(IRecallQueries recalls)
    {
        ArgumentNullException.ThrowIfNull(recalls);
        _recalls = recalls;
    }

    [HttpGet("{campaignNumber}")]
    public async Task<IActionResult> Details(string campaignNumber, CancellationToken cancellationToken)
    {
        var normalized = campaignNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return NotFound();
        }

        var campaign = await _recalls.GetByCampaignNumberAsync(normalized, cancellationToken);
        if (campaign is null)
        {
            return NotFound();
        }

        return View(Build(campaign));
    }

    private static RecallDetailViewModel Build(RecallCampaignDetail campaign)
    {
        var groups = campaign.Vehicles
            .GroupBy(vehicle => vehicle.Make, StringComparer.Ordinal)
            .Select(makeGroup => new RecalledMakeGroup(
                makeGroup.Key,
                makeGroup
                    .GroupBy(vehicle => vehicle.Model, StringComparer.Ordinal)
                    .Select(modelGroup => new RecalledModelRow(
                        modelGroup.Key,
                        modelGroup
                            .Select(ToYear)
                            .OrderByDescending(year => year.ModelYear)
                            .ToList()))
                    .OrderBy(row => row.Model, StringComparer.Ordinal)
                    .ToList()))
            .OrderBy(group => group.Make, StringComparer.Ordinal)
            .ToList();

        var modelRowCount = groups.Sum(group => group.Models.Count);
        var capped = Cap(groups, ModelRowLimit);

        return new RecallDetailViewModel
        {
            Campaign = campaign,
            Coverage = capped,
            CombinationCount = campaign.TotalVehicleCount,
            ListedCombinationCount = campaign.Vehicles.Count,
            ModelRowCount = modelRowCount,
            ShownModelRowCount = capped.Sum(group => group.Models.Count),
        };
    }

    private static List<RecalledMakeGroup> Cap(IReadOnlyList<RecalledMakeGroup> groups, int limit)
    {
        var capped = new List<RecalledMakeGroup>(groups.Count);
        var budget = limit;

        foreach (var group in groups)
        {
            if (budget <= 0)
            {
                break;
            }

            var models = group.Models.Count <= budget
                ? group.Models
                : group.Models.Take(budget).ToList();

            capped.Add(group with { Models = models });
            budget -= models.Count;
        }

        return capped;
    }

    private static RecalledYear ToYear(RecalledVehicle vehicle) =>
        new(vehicle.ModelYear, vehicle.MakeSlug, vehicle.ModelSlug);
}
