using GarageDoctor.Domain.Models;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.ViewComponents;

public sealed class ComponentBreakdownViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(VehicleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var ordered = profile.Components
            .OrderByDescending(component => component.Count)
            .ThenBy(component => component.Group, StringComparer.Ordinal)
            .ToList();

        var scale = ordered.Count == 0 ? 0 : ordered[0].Count;

        var groups = ordered
            .Select(component => new ComponentShareViewModel
            {
                Group = component.Group,
                Count = component.Count,
                WidthPercent = scale == 0
                    ? 0
                    : (int)Math.Round(component.Count * 100d / scale, MidpointRounding.AwayFromZero),
                ShareLabel = profile.TotalComplaints == 0
                    ? "0%"
                    : $"{Math.Round(component.Count * 100d / profile.TotalComplaints, MidpointRounding.AwayFromZero):F0}%"
            })
            .ToList();

        return View(new ComponentBreakdownViewModel
        {
            Groups = groups,
            TotalComplaints = profile.TotalComplaints,
            Classified = ordered.Sum(component => component.Count)
        });
    }
}
