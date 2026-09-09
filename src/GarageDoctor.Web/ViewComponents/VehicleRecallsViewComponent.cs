using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure.Queries;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.ViewComponents;

public sealed class VehicleRecallsViewComponent : ViewComponent
{
    private readonly IRecallQueries _recalls;

    public VehicleRecallsViewComponent(IRecallQueries recalls)
    {
        ArgumentNullException.ThrowIfNull(recalls);
        _recalls = recalls;
    }

    public async Task<IViewComponentResult> InvokeAsync(string vehicleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleKey);

        var rows = await _recalls
            .GetForVehicleAsync(vehicleKey, HttpContext.RequestAborted)
            .ConfigureAwait(false);

        var campaigns = rows
            .GroupBy(row => row.CampaignNumber, StringComparer.Ordinal)
            .Select(Merge)
            .OrderByDescending(campaign => campaign.Urgent)
            .ThenByDescending(campaign => campaign.ReportReceivedDate ?? DateOnly.MinValue)
            .ThenBy(campaign => campaign.CampaignNumber, StringComparer.Ordinal)
            .ToList();

        return View(new VehicleRecallsViewModel
        {
            Campaigns = campaigns,
            UrgentCampaigns = campaigns.Where(campaign => campaign.Urgent).ToList()
        });
    }

    private static RecallCampaignRowViewModel Merge(IGrouping<string, RecallCampaign> group)
    {
        var first = group.First();

        return new RecallCampaignRowViewModel
        {
            CampaignNumber = group.Key,
            ComponentGroup = first.ComponentGroup,
            ComponentNames = group
                .Select(row => row.ComponentName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            DefectDescription = first.DefectDescription,
            Consequence = first.Consequence,
            CorrectiveAction = first.CorrectiveAction,
            ReportReceivedDate = group.Max(row => row.ReportReceivedDate),
            PotentiallyAffected = group.Max(row => row.PotentiallyAffected),
            DoNotDrive = group.Any(row => row.DoNotDrive),
            ParkOutside = group.Any(row => row.ParkOutside)
        };
    }
}
