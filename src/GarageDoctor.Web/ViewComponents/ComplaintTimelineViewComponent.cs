using System.Text.Json;
using GarageDoctor.Domain.Models;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.ViewComponents;

public sealed class ComplaintTimelineViewComponent : ViewComponent
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IViewComponentResult Invoke(VehicleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var points = Densify(profile.Timeline);
        var scale = points.Count == 0 ? 0 : points.Max(point => point.Count);

        var rows = points
            .Select(point => new TimelinePointViewModel
            {
                Year = point.Year,
                Count = point.Count,
                WidthPercent = scale == 0
                    ? 0
                    : (int)Math.Round(point.Count * 100d / scale, MidpointRounding.AwayFromZero)
            })
            .ToList();

        var peak = points.Count == 0
            ? null
            : points.OrderByDescending(point => point.Count).ThenBy(point => point.Year).First();

        var payload = new TimelineChartPayload
        {
            Years = rows.Select(row => row.Year).ToList(),
            Counts = rows.Select(row => row.Count).ToList()
        };

        return View(new ComplaintTimelineViewModel
        {
            Points = rows,
            TotalComplaints = profile.TotalComplaints,
            ChartPayload = JsonSerializer.Serialize(payload, PayloadOptions),
            PeakYear = peak?.Year,
            PeakCount = peak?.Count,
            LastYearIsPartial = rows.Count > 0 && rows[^1].Year == TimeProvider.System.GetUtcNow().Year
        });
    }

    private static List<YearCount> Densify(IReadOnlyList<YearCount> timeline)
    {
        if (timeline.Count == 0)
        {
            return [];
        }

        var counts = timeline
            .GroupBy(entry => entry.Year)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count));

        var first = counts.Keys.Min();
        var last = counts.Keys.Max();
        var dense = new List<YearCount>(last - first + 1);

        for (var year = first; year <= last; year++)
        {
            dense.Add(new YearCount { Year = year, Count = counts.GetValueOrDefault(year) });
        }

        return dense;
    }
}
