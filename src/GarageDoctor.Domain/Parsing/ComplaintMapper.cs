using System.Globalization;
using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;

namespace GarageDoctor.Domain.Parsing;

public sealed class ComplaintMapper
{
    private const int EarliestModelYear = 1900;
    private const int LatestModelYear = 2035;
    private const int HighestPlausibleMileage = 999_999;
    private const string DateFormat = "yyyyMMdd";
    private const char YesFlag = 'Y';

    private readonly MakeCanonicalizer _makeCanonicalizer;
    private readonly ModelCanonicalizer _modelCanonicalizer;
    private readonly ComponentCanonicalizer _componentCanonicalizer;

    public ComplaintMapper(
        MakeCanonicalizer makeCanonicalizer,
        ModelCanonicalizer modelCanonicalizer,
        ComponentCanonicalizer componentCanonicalizer)
    {
        ArgumentNullException.ThrowIfNull(makeCanonicalizer);
        ArgumentNullException.ThrowIfNull(modelCanonicalizer);
        ArgumentNullException.ThrowIfNull(componentCanonicalizer);

        _makeCanonicalizer = makeCanonicalizer;
        _modelCanonicalizer = modelCanonicalizer;
        _componentCanonicalizer = componentCanonicalizer;
    }

    public bool IsVehicleComplaint(string[] fields)
    {
        EnsureFieldCount(fields);

        return fields[ComplaintFields.ProductType]
            .AsSpan()
            .Trim()
            .Equals(ComplaintFields.VehicleProductType, StringComparison.OrdinalIgnoreCase);
    }

    public Complaint Map(string[] fields)
    {
        EnsureFieldCount(fields);

        var complaintId = fields[ComplaintFields.ComplaintId].Trim();
        var id = ParseIdentifier(complaintId, complaintId, nameof(ComplaintFields.ComplaintId));
        var odiNumber = ParseIdentifier(
            fields[ComplaintFields.OdiNumber],
            complaintId,
            nameof(ComplaintFields.OdiNumber));

        var makeRaw = fields[ComplaintFields.MakeText].Trim();
        var modelRaw = fields[ComplaintFields.ModelText].Trim();
        var make = _makeCanonicalizer.Canonicalize(makeRaw);
        var model = _modelCanonicalizer.Canonicalize(modelRaw);
        var modelYear = ParseModelYear(fields[ComplaintFields.YearText]);

        var receivedDate = ParseDate(fields[ComplaintFields.ReceivedDate])
            ?? ParseDate(fields[ComplaintFields.DateAdded])
            ?? throw new ComplaintMappingException(
                complaintId,
                "Neither the received date nor the date added is a valid yyyyMMdd date.");

        return new Complaint
        {
            Id = id,
            OdiNumber = odiNumber,
            Manufacturer = fields[ComplaintFields.ManufacturerName].Trim(),
            Make = make.Name,
            MakeRaw = makeRaw,
            Model = model.Name,
            ModelRaw = modelRaw,
            ModelYear = modelYear,
            Component = _componentCanonicalizer.Canonicalize(fields[ComplaintFields.ComponentDescription]),
            MilesAtFailure = ParseMileage(fields[ComplaintFields.Miles]),
            Description = fields[ComplaintFields.ComplaintDescription].Trim(),
            FailureDate = ParseDate(fields[ComplaintFields.FailureDate]),
            ReceivedDate = receivedDate,
            Crash = IsYes(fields[ComplaintFields.Crash]),
            Fire = IsYes(fields[ComplaintFields.Fire]),
            Injured = ParseCasualtyCount(fields[ComplaintFields.Injured]),
            Deaths = ParseCasualtyCount(fields[ComplaintFields.Deaths]),
            PoliceReport = IsYes(fields[ComplaintFields.PoliceReport]),
            VehicleTowed = IsYes(fields[ComplaintFields.VehicleTowed]),
            MedicalAttention = IsYes(fields[ComplaintFields.MedicalAttention]),
            DriveTrain = UpperCaseOrNull(fields[ComplaintFields.DriveTrain]),
            FuelType = UpperCaseOrNull(fields[ComplaintFields.FuelType]),
            TransmissionType = UpperCaseOrNull(fields[ComplaintFields.TransmissionType]),
            Cylinders = ParseCylinders(fields[ComplaintFields.Cylinders]),
            VehicleKey = Canonicalization.VehicleKey.Create(make.Slug, model.Slug, modelYear)
        };
    }

    private static void EnsureFieldCount(string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Length == ComplaintFields.FieldCount)
        {
            return;
        }

        var complaintId = fields.Length > ComplaintFields.ComplaintId
            ? fields[ComplaintFields.ComplaintId].Trim()
            : string.Empty;

        throw new ComplaintMappingException(
            complaintId,
            $"Expected {ComplaintFields.FieldCount} fields but the record has {fields.Length}.");
    }

    private static int ParseIdentifier(string value, string complaintId, string fieldName)
    {
        var trimmed = value.AsSpan().Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var identifier))
        {
            throw new ComplaintMappingException(
                complaintId,
                $"Field {fieldName} is not a numeric identifier.");
        }

        return identifier;
    }

    private static int? ParseModelYear(string value)
    {
        var trimmed = value.AsSpan().Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var modelYear))
        {
            return null;
        }

        return modelYear is >= EarliestModelYear and <= LatestModelYear ? modelYear : null;
    }

    private static int? ParseMileage(string value)
    {
        var trimmed = value.AsSpan().Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var mileage))
        {
            return null;
        }

        return mileage is > 0 and <= HighestPlausibleMileage ? mileage : null;
    }

    private static int? ParseCylinders(string value)
    {
        var trimmed = value.AsSpan().Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var cylinders))
        {
            return null;
        }

        return cylinders > 0 ? cylinders : null;
    }

    private static int ParseCasualtyCount(string value)
    {
        var trimmed = value.AsSpan().Trim();
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return 0;
        }

        return count > 0 ? count : 0;
    }

    private static DateOnly? ParseDate(string value)
    {
        var trimmed = value.AsSpan().Trim();
        return DateOnly.TryParseExact(trimmed, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static bool IsYes(string value)
    {
        var trimmed = value.AsSpan().Trim();
        return trimmed.Length == 1 && char.ToUpperInvariant(trimmed[0]) == YesFlag;
    }

    private static string? UpperCaseOrNull(string value)
    {
        var trimmed = value.AsSpan().Trim();
        if (trimmed.IsEmpty)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            if (char.IsLower(character))
            {
                return trimmed.ToString().ToUpperInvariant();
            }
        }

        return trimmed.Length == value.Length ? value : trimmed.ToString();
    }
}
