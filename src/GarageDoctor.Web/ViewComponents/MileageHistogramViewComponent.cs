using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.ViewComponents;

public sealed class MileageHistogramViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(VehicleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var counts = profile.MileageHistogram.ToDictionary(bucket => bucket.From, bucket => bucket.Count);
        var scale = counts.Count == 0 ? 0 : counts.Values.Max();

        var buckets = MileageBuckets.Empty()
            .Select(bucket => Render(bucket, counts.GetValueOrDefault(bucket.From), scale, profile.WithMileage))
            .ToList();

        return View(new MileageHistogramViewModel
        {
            Buckets = buckets,
            WithMileage = profile.WithMileage,
            TotalComplaints = profile.TotalComplaints,
            AverageMilesAtFailure = profile.AverageMilesAtFailure
        });
    }

    private static MileageBucketViewModel Render(MileageBucket bucket, int count, int scale, int sample) =>
        new()
        {
            Label = Label(bucket),
            Count = count,
            WidthPercent = scale == 0 ? 0 : (int)Math.Round(count * 100d / scale, MidpointRounding.AwayFromZero),
            ShareLabel = sample == 0 ? "0%" : $"{Math.Round(count * 100d / sample, MidpointRounding.AwayFromZero):F0}%"
        };

    private static string Label(MileageBucket bucket) => bucket.To is { } upper
        ? $"{bucket.From / 1000:N0}–{upper / 1000:N0}k"
        : $"{bucket.From / 1000:N0}k+";
}
