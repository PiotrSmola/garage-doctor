using System.Globalization;
using GarageDoctor.Infrastructure.Queries;

namespace GarageDoctor.Web.Models;

public sealed record RecalledYear(int? ModelYear, string MakeSlug, string ModelSlug)
{
    public bool CanLink => ModelYear.HasValue && MakeSlug.Length > 0 && ModelSlug.Length > 0;

    public string Path => $"/vehicles/{MakeSlug}/{ModelSlug}/{ModelYear}";
}

public sealed record RecalledModelRow(string Model, IReadOnlyList<RecalledYear> Years);

public sealed record RecalledMakeGroup(string Make, IReadOnlyList<RecalledModelRow> Models);

public sealed record RecallDetailViewModel
{
    public required RecallCampaignDetail Campaign { get; init; }

    public required IReadOnlyList<RecalledMakeGroup> Coverage { get; init; }

    public required int CombinationCount { get; init; }

    public required int ModelRowCount { get; init; }

    public required int ShownModelRowCount { get; init; }

    public required int ListedCombinationCount { get; init; }

    public bool HasAdvisory => Campaign.DoNotDrive || Campaign.ParkOutside;

    public bool CoverageCapped => ShownModelRowCount < ModelRowCount;

    public bool SourceRowLimitReached => ListedCombinationCount < CombinationCount;

    public bool HasCoverage => Coverage.Count > 0;
}

public static class Formats
{
    private static readonly NumberFormatInfo Grouped = CreateGrouped();

    public static string Number(long value) => value.ToString("#,##0", Grouped);

    public static string NumberOrDash(int? value) => value.HasValue ? Number(value.Value) : "not stated";

    public static string DateOrDash(DateOnly? value) => value.HasValue
        ? value.Value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
        : "not stated";

    public static string Share(long part, long whole) => whole == 0
        ? "not known"
        : ((double)part / whole).ToString("P1", CultureInfo.InvariantCulture);

    private static NumberFormatInfo CreateGrouped()
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberGroupSeparator = " ";
        return format;
    }
}
