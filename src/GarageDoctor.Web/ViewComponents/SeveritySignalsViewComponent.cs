using GarageDoctor.Domain.Models;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.ViewComponents;

public sealed class SeveritySignalsViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(VehicleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var severity = profile.Severity;

        var signals = new List<SeveritySignalViewModel>
        {
            Signal("Crashes", severity.Crashes, critical: false),
            Signal("Fires", severity.Fires, critical: true),
            Signal("Injured", severity.Injured, critical: true),
            Signal("Deaths", severity.Deaths, critical: true),
            Signal("Police reports", severity.PoliceReports, critical: false),
            Signal("Vehicles towed", severity.VehiclesTowed, critical: false),
            Signal("Medical attention", severity.MedicalAttention, critical: false)
        };

        return View(new SeveritySignalsViewModel
        {
            Signals = signals,
            TotalComplaints = profile.TotalComplaints,
            Deaths = severity.Deaths,
            Fires = severity.Fires,
            SevereSummary = Summarize(severity.Deaths, severity.Fires)
        });
    }

    private static SeveritySignalViewModel Signal(string label, int count, bool critical) =>
        new()
        {
            Label = label,
            Count = count,
            Critical = critical
        };

    private static string Summarize(int deaths, int fires)
    {
        var parts = new List<string>(2);

        if (deaths > 0)
        {
            parts.Add($"{deaths:N0} {(deaths == 1 ? "report describes a death" : "reports describe a death")}");
        }

        if (fires > 0)
        {
            parts.Add($"{fires:N0} {(fires == 1 ? "report describes a fire" : "reports describe a fire")}");
        }

        return parts.Count == 0
            ? string.Empty
            : $"For this model year, {string.Join(" and ", parts)}. These are owner accounts, not findings of a defect investigation.";
    }
}
