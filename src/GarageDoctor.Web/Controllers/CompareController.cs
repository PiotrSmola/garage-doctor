using System.Globalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure.Queries;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

[Route("compare")]
public sealed class CompareController : Controller
{
    private const string FirstSide = "a";

    private const string SecondSide = "b";

    private const string FirstLabel = "First vehicle";

    private const string SecondLabel = "Second vehicle";

    private readonly IVehicleCatalogQueries _catalog;
    private readonly IVehicleProfileQueries _profiles;
    private readonly IRecallQueries _recalls;

    public CompareController(
        IVehicleCatalogQueries catalog,
        IVehicleProfileQueries profiles,
        IRecallQueries recalls)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(recalls);

        _catalog = catalog;
        _profiles = profiles;
        _recalls = recalls;
    }

    [HttpGet("", Name = "Compare")]
    public async Task<IActionResult> Index([FromQuery] CompareQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var firstRequest = CompareSideRequest.From(query.A, query.AMake, query.AModel, query.AYear);
        var secondRequest = CompareSideRequest.From(query.B, query.BMake, query.BModel, query.BYear);

        if (firstRequest.FoldsIntoKey || secondRequest.FoldsIntoKey)
        {
            return Redirect(ComparePaths.Build(firstRequest, secondRequest));
        }

        var makes = await _catalog.GetMakesAsync(cancellationToken).ConfigureAwait(false);

        var first = await BuildSideAsync(FirstSide, FirstLabel, firstRequest, makes, cancellationToken)
            .ConfigureAwait(false);

        var second = await BuildSideAsync(SecondSide, SecondLabel, secondRequest, makes, cancellationToken)
            .ConfigureAwait(false);

        var result = first.Vehicle is { } firstVehicle && second.Vehicle is { } secondVehicle
            ? CompareResultViewModel.Build(firstVehicle, secondVehicle)
            : null;

        return View(new ComparePageViewModel
        {
            First = first,
            Second = second,
            Result = result
        });
    }

    private async Task<CompareSideViewModel> BuildSideAsync(
        string side,
        string label,
        CompareSideRequest request,
        IReadOnlyList<MakeSummary> makes,
        CancellationToken cancellationToken)
    {
        var status = request.StatusBeforeLookup;
        VehicleIdentity? identity = null;
        VehicleProfile? profile = null;
        CompareRecallSummary? recalls = null;

        if (status == CompareSideStatus.Resolved
            && request.MakeSlug is { } makeSlug
            && request.ModelSlug is { } modelSlug
            && request.ModelYear is { } modelYear)
        {
            identity = await _catalog
                .ResolveVehicleAsync(makeSlug, modelSlug, modelYear, cancellationToken)
                .ConfigureAwait(false);

            if (identity is null)
            {
                status = CompareSideStatus.Unknown;
            }
            else
            {
                profile = await _profiles
                    .GetAsync(identity.VehicleKey, cancellationToken)
                    .ConfigureAwait(false);

                if (profile is null)
                {
                    status = CompareSideStatus.Missing;
                }
                else
                {
                    var campaigns = await _recalls
                        .GetForVehicleAsync(identity.VehicleKey, cancellationToken)
                        .ConfigureAwait(false);

                    recalls = CompareRecallSummary.From(campaigns);
                }
            }
        }

        var picker = await BuildPickerAsync(side, label, request, identity, makes, cancellationToken)
            .ConfigureAwait(false);

        return new CompareSideViewModel
        {
            Side = side,
            Label = label,
            Status = status,
            Picker = picker,
            RequestedKey = request.RequestedKey,
            Vehicle = identity is not null && profile is not null && recalls is not null
                ? new CompareVehicleViewModel
                {
                    Side = side,
                    Identity = identity,
                    Profile = profile,
                    Recalls = recalls
                }
                : null
        };
    }

    private async Task<CompareSidePicker> BuildPickerAsync(
        string side,
        string label,
        CompareSideRequest request,
        VehicleIdentity? identity,
        IReadOnlyList<MakeSummary> makes,
        CancellationToken cancellationToken)
    {
        var selectedMakeSlug = identity?.MakeSlug ?? request.MakeSlug;
        var selectedModelSlug = identity?.ModelSlug ?? request.ModelSlug;
        var selectedYear = identity?.ModelYear ?? request.ModelYear;

        var makeDetail = selectedMakeSlug is null
            ? null
            : await _catalog.GetMakeAsync(selectedMakeSlug, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ModelSummary> models = makeDetail?.Models ?? [];

        var selectedModel = selectedModelSlug is null
            ? null
            : models.FirstOrDefault(model => model.ModelSlug == selectedModelSlug);

        IReadOnlyList<ModelYearSummary> years = makeDetail is null || selectedModel is null
            ? []
            : await _catalog
                .GetModelYearsAsync(makeDetail.MakeSlug, selectedModel.ModelSlug, cancellationToken)
                .ConfigureAwait(false);

        return new CompareSidePicker
        {
            Side = side,
            Label = label,
            Makes = makes
                .OrderBy(make => make.Make, StringComparer.Ordinal)
                .Select(make => new CompareOption(make.MakeSlug, make.Make, make.MakeSlug == selectedMakeSlug))
                .ToList(),
            Models = models
                .Select(model => new CompareOption(
                    model.ModelSlug,
                    string.Create(CultureInfo.InvariantCulture, $"{model.Model} · {model.ComplaintCount:N0}"),
                    model.ModelSlug == selectedModelSlug))
                .ToList(),
            Years = years
                .Select(year => new CompareOption(
                    year.ModelYear.ToString(CultureInfo.InvariantCulture),
                    string.Create(CultureInfo.InvariantCulture, $"{year.ModelYear} · {year.ComplaintCount:N0}"),
                    year.ModelYear == selectedYear))
                .ToList()
        };
    }
}
