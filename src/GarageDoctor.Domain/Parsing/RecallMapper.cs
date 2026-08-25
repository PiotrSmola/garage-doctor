using System.Globalization;
using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;

namespace GarageDoctor.Domain.Parsing;

public sealed class RecallMapper
{
    private const string AffirmativeAdvisory = "Yes";
    private const string DateFormat = "yyyyMMdd";
    private const string UnknownRecordId = "unknown";
    private const int EarliestModelYear = 1900;
    private const int LatestModelYear = 2035;

    private readonly MakeCanonicalizer _makeCanonicalizer;
    private readonly ModelCanonicalizer _modelCanonicalizer;
    private readonly ComponentCanonicalizer _componentCanonicalizer;

    public RecallMapper(
        MakeCanonicalizer makeCanonicalizer,
        ModelCanonicalizer modelCanonicalizer,
        ComponentCanonicalizer componentCanonicalizer)
    {
        _makeCanonicalizer = makeCanonicalizer;
        _modelCanonicalizer = modelCanonicalizer;
        _componentCanonicalizer = componentCanonicalizer;
    }

    public bool IsVehicleRecall(string[] fields)
    {
        EnsureFieldCount(fields);
        return string.Equals(
            Field(fields, RecallFields.RecallType),
            RecallFields.VehicleRecallType,
            StringComparison.OrdinalIgnoreCase);
    }

    public RecallCampaign Map(string[] fields)
    {
        EnsureFieldCount(fields);

        var rawRecordId = Field(fields, RecallFields.RecordId);
        if (!TryParseNumber(rawRecordId, out var recordId))
        {
            throw new RecallMappingException(
                rawRecordId.Length == 0 ? UnknownRecordId : rawRecordId,
                $"RECORD_ID '{rawRecordId}' is not a number.");
        }

        var campaignNumber = Field(fields, RecallFields.CampaignNumber);
        if (campaignNumber.Length == 0)
        {
            throw new RecallMappingException(rawRecordId, "CAMPNO is empty.");
        }

        var makeRaw = Field(fields, RecallFields.MakeText);
        var modelRaw = Field(fields, RecallFields.ModelText);
        var make = _makeCanonicalizer.Canonicalize(makeRaw);
        var model = _modelCanonicalizer.Canonicalize(modelRaw);
        var modelYear = ParseModelYear(Field(fields, RecallFields.YearText));
        var componentName = Field(fields, RecallFields.ComponentName);
        var component = _componentCanonicalizer.Canonicalize(componentName);
        var vehicleKey = Canonicalization.VehicleKey.Create(make.Slug, model.Slug, modelYear);

        return new RecallCampaign
        {
            Id = recordId,
            CampaignNumber = campaignNumber,
            Manufacturer = Field(fields, RecallFields.ManufacturerName),
            Make = make.Name,
            MakeRaw = makeRaw,
            Model = model.Name,
            ModelRaw = modelRaw,
            ModelYear = modelYear,
            ComponentName = componentName,
            ComponentGroup = component.Group,
            RecallType = Field(fields, RecallFields.RecallType),
            PotentiallyAffected = ParsePotentiallyAffected(Field(fields, RecallFields.PotentiallyAffected)),
            OwnersNotifiedDate = ParseDate(Field(fields, RecallFields.OwnersNotifiedDate)),
            ReportReceivedDate = ParseDate(Field(fields, RecallFields.ReportReceivedDate)),
            DefectDescription = Field(fields, RecallFields.DefectDescription),
            Consequence = Field(fields, RecallFields.Consequence),
            CorrectiveAction = Field(fields, RecallFields.CorrectiveAction),
            DoNotDrive = IsAffirmative(Field(fields, RecallFields.DoNotDrive)),
            ParkOutside = IsAffirmative(Field(fields, RecallFields.ParkOutside)),
            VehicleKey = vehicleKey
        };
    }

    private static void EnsureFieldCount(string[] fields)
    {
        if (fields.Length == RecallFields.FieldCount)
        {
            return;
        }

        var recordId = fields.Length > RecallFields.RecordId
            ? Field(fields, RecallFields.RecordId)
            : UnknownRecordId;

        throw new RecallMappingException(
            recordId.Length == 0 ? UnknownRecordId : recordId,
            $"Expected {RecallFields.FieldCount} fields but found {fields.Length}.");
    }

    private static string Field(string[] fields, int index) => fields[index].Trim();

    private static bool IsAffirmative(string value) =>
        string.Equals(value, AffirmativeAdvisory, StringComparison.OrdinalIgnoreCase);

    private static int? ParseModelYear(string value) =>
        TryParseNumber(value, out var year) && year is >= EarliestModelYear and <= LatestModelYear
            ? year
            : null;

    private static int? ParsePotentiallyAffected(string value) =>
        TryParseNumber(value, out var count) ? count : null;

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static bool TryParseNumber(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
}
