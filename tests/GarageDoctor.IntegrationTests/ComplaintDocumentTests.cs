using GarageDoctor.Domain.Models;
using GarageDoctor.Infrastructure;
using MongoDB.Bson;

namespace GarageDoctor.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class ComplaintDocumentTests
{
    private static readonly string[] ForbiddenElementNames =
    [
        "dealerName",
        "dealerTel",
        "dealerCity",
        "dealerState",
        "dealerZip",
        "vin",
        "vehicleOperator",
        "city"
    ];

    private static readonly string[] ExpectedComplaintElementNames =
    [
        "_id",
        "component",
        "crash",
        "cylinders",
        "deaths",
        "description",
        "driveTrain",
        "failureDate",
        "fire",
        "fuelType",
        "injured",
        "make",
        "makeRaw",
        "manufacturer",
        "medicalAttention",
        "milesAtFailure",
        "model",
        "modelRaw",
        "modelYear",
        "odiNumber",
        "policeReport",
        "receivedDate",
        "transmissionType",
        "vehicleKey",
        "vehicleTowed"
    ];

    public ComplaintDocumentTests() => MongoConventions.Register();

    [Fact]
    public void SerializedComplaintCarriesNoPersonallyIdentifyingElement()
    {
        var names = AllElementNames(FullyPopulatedComplaint().ToBsonDocument()).ToArray();

        foreach (var forbidden in ForbiddenElementNames)
        {
            Assert.DoesNotContain(names, name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void SerializedComplaintExposesExactlyTheExpectedElements()
    {
        var document = FullyPopulatedComplaint().ToBsonDocument();

        Assert.Equal(ExpectedComplaintElementNames, document.Names.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void EveryElementNameIsCamelCase()
    {
        var document = FullyPopulatedComplaint().ToBsonDocument();

        foreach (var name in AllElementNames(document).Where(name => name != "_id"))
        {
            Assert.True(char.IsAsciiLetterLower(name[0]), $"Element '{name}' does not start with a lower case letter");
            Assert.True(name.All(char.IsAsciiLetterOrDigit), $"Element '{name}' contains a non alphanumeric character");
        }
    }

    [Fact]
    public void NestedComponentPathUsesCamelCaseElements()
    {
        var document = FullyPopulatedComplaint().ToBsonDocument();
        var component = document["component"].AsBsonDocument;

        Assert.Equal(new[] { "group", "levels", "raw" }, component.Names.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(BsonType.Array, component["levels"].BsonType);
    }

    [Fact]
    public void ComplaintIdentifierIsSerializedAsUnderscoreId()
    {
        var complaint = FullyPopulatedComplaint();

        var document = complaint.ToBsonDocument();

        Assert.Equal(BsonType.Int32, document["_id"].BsonType);
        Assert.Equal(complaint.Id, document["_id"].AsInt32);
    }

    [Fact]
    public void DateOnlyElementsAreSerializedAsBsonDatesAtMidnightUtc()
    {
        var document = FullyPopulatedComplaint().ToBsonDocument();

        Assert.Equal(BsonType.DateTime, document["receivedDate"].BsonType);
        Assert.Equal(BsonType.DateTime, document["failureDate"].BsonType);
        Assert.Equal(new DateTime(2019, 3, 14, 0, 0, 0, DateTimeKind.Utc), document["receivedDate"].ToUniversalTime());
        Assert.Equal(new DateTime(2019, 2, 8, 0, 0, 0, DateTimeKind.Utc), document["failureDate"].ToUniversalTime());
    }

    [Fact]
    public void AbsentOptionalValuesAreSerializedAsNullRatherThanOmitted()
    {
        var document = SparseComplaint().ToBsonDocument();

        Assert.Equal(BsonType.Null, document["modelYear"].BsonType);
        Assert.Equal(BsonType.Null, document["milesAtFailure"].BsonType);
        Assert.Equal(BsonType.Null, document["failureDate"].BsonType);
        Assert.Equal(BsonType.Null, document["driveTrain"].BsonType);
        Assert.Equal(BsonType.Null, document["cylinders"].BsonType);
    }

    private static IEnumerable<string> AllElementNames(BsonValue value)
    {
        switch (value)
        {
            case BsonDocument document:
                foreach (var element in document)
                {
                    yield return element.Name;

                    foreach (var nested in AllElementNames(element.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case BsonArray array:
                foreach (var item in array)
                {
                    foreach (var nested in AllElementNames(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static Complaint FullyPopulatedComplaint() => new()
    {
        Id = 1_204_517,
        OdiNumber = 11_204_517,
        Manufacturer = "Fictional Motors Group",
        Make = "AUDI",
        MakeRaw = "AUDI AG",
        Model = "A3",
        ModelRaw = "A3 QUATTRO",
        ModelYear = 2015,
        Component = new ComponentPath
        {
            Raw = "SERVICE BRAKES, HYDRAULIC:FOUNDATION COMPONENTS:DISC",
            Group = "SERVICE BRAKES",
            Levels = ["SERVICE BRAKES, HYDRAULIC", "FOUNDATION COMPONENTS", "DISC"]
        },
        MilesAtFailure = 48_230,
        Description = "The brake pedal sank to the floor while slowing for a red light.",
        FailureDate = new DateOnly(2019, 2, 8),
        ReceivedDate = new DateOnly(2019, 3, 14),
        Crash = true,
        Fire = false,
        Injured = 2,
        Deaths = 0,
        PoliceReport = true,
        VehicleTowed = true,
        MedicalAttention = true,
        DriveTrain = "AWD",
        FuelType = "GAS",
        TransmissionType = "AUTO",
        Cylinders = 4,
        VehicleKey = "audi|a3|2015"
    };

    private static Complaint SparseComplaint() => new()
    {
        Id = 990_001,
        OdiNumber = 10_990_001,
        Manufacturer = "Unknown",
        Make = "UNKNOWN",
        MakeRaw = "",
        Model = "UNKNOWN",
        ModelRaw = "",
        ModelYear = null,
        Component = new ComponentPath { Raw = "", Group = "OTHER", Levels = [] },
        MilesAtFailure = null,
        Description = "No further detail was supplied by the owner.",
        FailureDate = null,
        ReceivedDate = new DateOnly(2004, 7, 2),
        VehicleKey = "unknown|unknown|unknown"
    };
}
