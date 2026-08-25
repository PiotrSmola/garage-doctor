using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Models;
using GarageDoctor.Domain.Parsing;
using GarageDoctor.Tests;

namespace GarageDoctor.UnitTests.Mapping;

public sealed class ComplaintMapperTests
{
    private const string PersonalDataMarker = "PERSONALDATAMARKER";

    private static readonly Dictionary<string, string> MakeAliases = new()
    {
        ["VW"] = "VOLKSWAGEN",
        ["VOLKSWAGEN"] = "VOLKSWAGEN",
        ["MERCEDES"] = "MERCEDES-BENZ",
        ["MERCEDES BENZ"] = "MERCEDES-BENZ",
        ["MERCEDES-BENZ"] = "MERCEDES-BENZ",
        ["FORD MOTOR"] = "FORD"
    };

    private static readonly Dictionary<string, string> ComponentGroups = new()
    {
        ["POWER TRAIN"] = "POWER TRAIN",
        ["ENGINE AND ENGINE COOLING"] = "ENGINE"
    };

    private static readonly int[] SeverityFlagFields =
    [
        ComplaintFields.Crash,
        ComplaintFields.Fire,
        ComplaintFields.PoliceReport,
        ComplaintFields.MedicalAttention,
        ComplaintFields.VehicleTowed
    ];

    private static ComplaintMapper Mapper() => new(
        MakeCanonicalizer.FromAliases(MakeAliases),
        new ModelCanonicalizer(),
        ComponentCanonicalizer.FromGroups(ComponentGroups));

    private static string[] Record(params (int Index, string Value)[] overrides)
    {
        var fields = new string[ComplaintFields.FieldCount];
        Array.Fill(fields, string.Empty);
        fields[ComplaintFields.ComplaintId] = "1234567";
        fields[ComplaintFields.OdiNumber] = "11000123";
        fields[ComplaintFields.ReceivedDate] = "20160325";
        fields[ComplaintFields.ProductType] = "V";

        foreach (var (index, value) in overrides)
        {
            fields[index] = value;
        }

        return fields;
    }

    [Theory]
    [InlineData("V")]
    [InlineData("v")]
    [InlineData("V   ")]
    [InlineData("   V")]
    public void RecognisesVehicleComplaintsByProductType(string productType)
    {
        Assert.True(Mapper().IsVehicleComplaint(Record((ComplaintFields.ProductType, productType))));
    }

    [Theory]
    [InlineData("T")]
    [InlineData("C")]
    [InlineData("E")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VEHICLE")]
    public void RejectsComplaintsWhoseProductTypeIsNotVehicle(string productType)
    {
        Assert.False(Mapper().IsVehicleComplaint(Record((ComplaintFields.ProductType, productType))));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(52)]
    [InlineData(0)]
    public void RefusesRecordsWithTheWrongFieldCount(int fieldCount)
    {
        var fields = new string[fieldCount];
        Array.Fill(fields, string.Empty);

        Assert.Throws<ComplaintMappingException>(() => Mapper().Map(fields));
        Assert.Throws<ComplaintMappingException>(() => Mapper().IsVehicleComplaint(fields));
    }

    [Fact]
    public void MapsEveryPropertyOfAWellFormedRecord()
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.ComplaintId, "1234567"),
            (ComplaintFields.OdiNumber, "11000123"),
            (ComplaintFields.ManufacturerName, "  Volkswagen Group of America, Inc.  "),
            (ComplaintFields.MakeText, "VW    "),
            (ComplaintFields.ModelText, "  JETTA "),
            (ComplaintFields.YearText, "2015"),
            (ComplaintFields.Crash, "Y"),
            (ComplaintFields.FailureDate, "20160312"),
            (ComplaintFields.Fire, "N"),
            (ComplaintFields.Injured, "2"),
            (ComplaintFields.Deaths, "1"),
            (ComplaintFields.ComponentDescription, "POWER TRAIN:AUTOMATIC TRANSMISSION:LEVER AND LINKAGE"),
            (ComplaintFields.DateAdded, "20160401"),
            (ComplaintFields.ReceivedDate, "20160325"),
            (ComplaintFields.Miles, "52000"),
            (ComplaintFields.ComplaintDescription, "  THE TRANSMISSION SLIPPED OUT OF GEAR.  "),
            (ComplaintFields.PoliceReport, "Y"),
            (ComplaintFields.Cylinders, "4"),
            (ComplaintFields.DriveTrain, "fwd "),
            (ComplaintFields.FuelType, "gas"),
            (ComplaintFields.TransmissionType, "auto"),
            (ComplaintFields.MedicalAttention, "Y"),
            (ComplaintFields.VehicleTowed, "Y")));

        Assert.Equal(1234567, complaint.Id);
        Assert.Equal(11000123, complaint.OdiNumber);
        Assert.Equal("Volkswagen Group of America, Inc.", complaint.Manufacturer);
        Assert.Equal("VOLKSWAGEN", complaint.Make);
        Assert.Equal("VW", complaint.MakeRaw);
        Assert.Equal("JETTA", complaint.Model);
        Assert.Equal("JETTA", complaint.ModelRaw);
        Assert.Equal(2015, complaint.ModelYear);
        Assert.Equal("POWER TRAIN:AUTOMATIC TRANSMISSION:LEVER AND LINKAGE", complaint.Component.Raw);
        Assert.Equal("POWER TRAIN", complaint.Component.Group);
        Assert.Equal(
            new[] { "POWER TRAIN", "AUTOMATIC TRANSMISSION", "LEVER AND LINKAGE" },
            complaint.Component.Levels);
        Assert.Equal(52000, complaint.MilesAtFailure);
        Assert.Equal("THE TRANSMISSION SLIPPED OUT OF GEAR.", complaint.Description);
        Assert.Equal(new DateOnly(2016, 3, 12), complaint.FailureDate);
        Assert.Equal(new DateOnly(2016, 3, 25), complaint.ReceivedDate);
        Assert.True(complaint.Crash);
        Assert.False(complaint.Fire);
        Assert.Equal(2, complaint.Injured);
        Assert.Equal(1, complaint.Deaths);
        Assert.True(complaint.PoliceReport);
        Assert.True(complaint.VehicleTowed);
        Assert.True(complaint.MedicalAttention);
        Assert.Equal("FWD", complaint.DriveTrain);
        Assert.Equal("GAS", complaint.FuelType);
        Assert.Equal("AUTO", complaint.TransmissionType);
        Assert.Equal(4, complaint.Cylinders);
        Assert.Equal("volkswagen|jetta|2015", complaint.VehicleKey);
    }

    [Theory]
    [InlineData("9999")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UNKNOWN")]
    [InlineData("20 15")]
    [InlineData("1899")]
    [InlineData("2036")]
    [InlineData("0")]
    public void LeavesTheModelYearNullWhenItIsUnusable(string yearText)
    {
        Assert.Null(Mapper().Map(Record((ComplaintFields.YearText, yearText))).ModelYear);
    }

    [Theory]
    [InlineData("1900", 1900)]
    [InlineData("2035", 2035)]
    [InlineData("2015", 2015)]
    [InlineData("  2015  ", 2015)]
    public void KeepsModelYearsInsideTheAcceptedRange(string yearText, int expected)
    {
        Assert.Equal(expected, Mapper().Map(Record((ComplaintFields.YearText, yearText))).ModelYear);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MANY")]
    [InlineData("52,000")]
    [InlineData("-5")]
    public void LeavesTheMileageNullWhenItIsUnusable(string miles)
    {
        Assert.Null(Mapper().Map(Record((ComplaintFields.Miles, miles))).MilesAtFailure);
    }

    [Theory]
    [InlineData("1000000")]
    [InlineData("9999999")]
    [InlineData("999999999999")]
    public void RejectsAbsurdMileageInsteadOfClampingIt(string miles)
    {
        Assert.Null(Mapper().Map(Record((ComplaintFields.Miles, miles))).MilesAtFailure);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("52000", 52000)]
    [InlineData("  52000  ", 52000)]
    [InlineData("999999", 999999)]
    public void KeepsMileageUpToTheHighestPlausibleValue(string miles, int expected)
    {
        Assert.Equal(expected, Mapper().Map(Record((ComplaintFields.Miles, miles))).MilesAtFailure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("20130229")]
    [InlineData("19950000")]
    [InlineData("20161332")]
    [InlineData("2016-03-12")]
    [InlineData("03122016")]
    [InlineData("NOTADATE")]
    public void LeavesTheFailureDateNullWhenItIsNotARealDate(string failureDate)
    {
        Assert.Null(Mapper().Map(Record((ComplaintFields.FailureDate, failureDate))).FailureDate);
    }

    [Fact]
    public void ParsesTheFailureDateStrictlyAsYearMonthDay()
    {
        var complaint = Mapper().Map(Record((ComplaintFields.FailureDate, "20130228")));

        Assert.Equal(new DateOnly(2013, 2, 28), complaint.FailureDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("20130229")]
    [InlineData("NOTADATE")]
    public void FallsBackToTheDateAddedWhenTheReceivedDateIsUnusable(string receivedDate)
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.ReceivedDate, receivedDate),
            (ComplaintFields.DateAdded, "20160401")));

        Assert.Equal(new DateOnly(2016, 4, 1), complaint.ReceivedDate);
    }

    [Fact]
    public void PrefersTheReceivedDateOverTheDateAdded()
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.ReceivedDate, "20160325"),
            (ComplaintFields.DateAdded, "20160401")));

        Assert.Equal(new DateOnly(2016, 3, 25), complaint.ReceivedDate);
    }

    [Fact]
    public void ThrowsWhenNeitherTheReceivedDateNorTheDateAddedIsValid()
    {
        var fields = Record(
            (ComplaintFields.ComplaintId, "987654"),
            (ComplaintFields.ReceivedDate, "19950000"),
            (ComplaintFields.DateAdded, ""));

        var exception = Assert.Throws<ComplaintMappingException>(() => Mapper().Map(fields));

        Assert.Equal("987654", exception.ComplaintId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC123")]
    [InlineData("12.5")]
    [InlineData("-7")]
    public void ThrowsWhenTheComplaintIdIsNotNumeric(string complaintId)
    {
        var fields = Record((ComplaintFields.ComplaintId, complaintId));

        var exception = Assert.Throws<ComplaintMappingException>(() => Mapper().Map(fields));

        Assert.Equal(complaintId.Trim(), exception.ComplaintId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC123")]
    [InlineData("-7")]
    public void ThrowsWhenTheOdiNumberIsNotNumeric(string odiNumber)
    {
        var fields = Record(
            (ComplaintFields.ComplaintId, "987654"),
            (ComplaintFields.OdiNumber, odiNumber));

        var exception = Assert.Throws<ComplaintMappingException>(() => Mapper().Map(fields));

        Assert.Equal("987654", exception.ComplaintId);
    }

    [Theory]
    [InlineData("Y")]
    [InlineData("y")]
    [InlineData("Y  ")]
    public void ReadsEverySeverityFlagAsTrueForY(string flag)
    {
        var complaint = Mapper().Map(RecordWithSeverityFlags(flag));

        Assert.True(complaint.Crash);
        Assert.True(complaint.Fire);
        Assert.True(complaint.PoliceReport);
        Assert.True(complaint.MedicalAttention);
        Assert.True(complaint.VehicleTowed);
    }

    [Theory]
    [InlineData("N")]
    [InlineData("n")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("X")]
    [InlineData("YES")]
    [InlineData("1")]
    public void ReadsEverySeverityFlagAsFalseForAnythingButY(string flag)
    {
        var complaint = Mapper().Map(RecordWithSeverityFlags(flag));

        Assert.False(complaint.Crash);
        Assert.False(complaint.Fire);
        Assert.False(complaint.PoliceReport);
        Assert.False(complaint.MedicalAttention);
        Assert.False(complaint.VehicleTowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NONE")]
    [InlineData("-1")]
    [InlineData("-12")]
    public void TreatsUnusableCasualtyCountsAsZero(string count)
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.Injured, count),
            (ComplaintFields.Deaths, count)));

        Assert.Equal(0, complaint.Injured);
        Assert.Equal(0, complaint.Deaths);
    }

    [Fact]
    public void ParsesCasualtyCounts()
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.Injured, "3"),
            (ComplaintFields.Deaths, " 2 ")));

        Assert.Equal(3, complaint.Injured);
        Assert.Equal(2, complaint.Deaths);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SIX")]
    [InlineData("-4")]
    public void LeavesTheCylinderCountNullWhenItIsUnusable(string cylinders)
    {
        Assert.Null(Mapper().Map(Record((ComplaintFields.Cylinders, cylinders))).Cylinders);
    }

    [Fact]
    public void ParsesTheCylinderCount()
    {
        Assert.Equal(6, Mapper().Map(Record((ComplaintFields.Cylinders, " 6 "))).Cylinders);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void LeavesDrivetrainDetailsNullWhenTheyAreEmpty(string value)
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.DriveTrain, value),
            (ComplaintFields.FuelType, value),
            (ComplaintFields.TransmissionType, value)));

        Assert.Null(complaint.DriveTrain);
        Assert.Null(complaint.FuelType);
        Assert.Null(complaint.TransmissionType);
    }

    [Theory]
    [InlineData("awd", "AWD")]
    [InlineData("  4WD  ", "4WD")]
    [InlineData("Fwd", "FWD")]
    public void TrimsAndUppercasesDrivetrainDetails(string value, string expected)
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.DriveTrain, value),
            (ComplaintFields.FuelType, value),
            (ComplaintFields.TransmissionType, value)));

        Assert.Equal(expected, complaint.DriveTrain);
        Assert.Equal(expected, complaint.FuelType);
        Assert.Equal(expected, complaint.TransmissionType);
    }

    [Theory]
    [InlineData("VW", "VOLKSWAGEN")]
    [InlineData("VOLKSWAGEN", "VOLKSWAGEN")]
    [InlineData("MERCEDES BENZ", "MERCEDES-BENZ")]
    [InlineData("MERCEDES", "MERCEDES-BENZ")]
    [InlineData("FORD MOTOR CO, INC", "FORD")]
    public void CanonicalizesTheMakeEndToEnd(string makeText, string expectedMake)
    {
        var complaint = Mapper().Map(Record((ComplaintFields.MakeText, makeText)));

        Assert.Equal(expectedMake, complaint.Make);
        Assert.Equal(makeText, complaint.MakeRaw);
    }

    [Fact]
    public void KeepsTheUntouchedOriginalInTheRawMakeAndModel()
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.MakeText, "  mercedes benz   "),
            (ComplaintFields.ModelText, "  c 300  ")));

        Assert.Equal("mercedes benz", complaint.MakeRaw);
        Assert.Equal("MERCEDES-BENZ", complaint.Make);
        Assert.Equal("c 300", complaint.ModelRaw);
        Assert.Equal("C 300", complaint.Model);
    }

    [Fact]
    public void BuildsTheVehicleKeyFromCanonicalSlugsAndModelYear()
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.MakeText, "AUDI"),
            (ComplaintFields.ModelText, "A3"),
            (ComplaintFields.YearText, "2015")));

        Assert.Equal("audi|a3|2015", complaint.VehicleKey);
    }

    [Fact]
    public void MarksTheVehicleKeyYearAsUnknownWhenTheModelYearIsMissing()
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.MakeText, "AUDI"),
            (ComplaintFields.ModelText, "A3"),
            (ComplaintFields.YearText, "9999")));

        Assert.Equal("audi|a3|unknown", complaint.VehicleKey);
    }

    [Fact]
    public void MapsTheComponentPathIntoItsCanonicalGroupAndLevels()
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.ComponentDescription, "  ENGINE AND ENGINE COOLING:ENGINE:GASKET  ")));

        Assert.Equal("ENGINE AND ENGINE COOLING:ENGINE:GASKET", complaint.Component.Raw);
        Assert.Equal("ENGINE", complaint.Component.Group);
        Assert.Equal(new[] { "ENGINE AND ENGINE COOLING", "ENGINE", "GASKET" }, complaint.Component.Levels);
    }

    [Fact]
    public void FallsBackToTheOtherGroupForAnUnmappedComponent()
    {
        var complaint = Mapper().Map(Record((ComplaintFields.ComponentDescription, "TELEPORTER:FLUX CAPACITOR")));

        Assert.Equal(ComponentCanonicalizer.FallbackGroup, complaint.Component.Group);
        Assert.Equal(new[] { "TELEPORTER", "FLUX CAPACITOR" }, complaint.Component.Levels);
    }

    [Fact]
    public void TrimsThePaddingFromTheDescription()
    {
        var complaint = Mapper().Map(Record(
            (ComplaintFields.ComplaintDescription, "   THE BRAKES FAILED WITHOUT WARNING.    ")));

        Assert.Equal("THE BRAKES FAILED WITHOUT WARNING.", complaint.Description);
    }

    [Theory]
    [InlineData("dealer")]
    [InlineData("vin")]
    [InlineData("city")]
    [InlineData("operator")]
    public void ExposesNoPersonalDataPropertyOnTheComplaintModel(string forbiddenFragment)
    {
        foreach (var property in typeof(Complaint).GetProperties())
        {
            Assert.DoesNotContain(forbiddenFragment, property.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NeverCopiesPersonalDataFieldsIntoTheMappedComplaint()
    {
        var fields = Record();
        foreach (var index in ComplaintFields.PersonalDataFields)
        {
            fields[index] = PersonalDataMarker;
        }

        var complaint = Mapper().Map(fields);

        foreach (var text in TextValuesOf(complaint))
        {
            Assert.DoesNotContain(PersonalDataMarker, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CanonicalizesThroughTheRepositoryMapsThatShipWithTheApplication()
    {
        var mapper = new ComplaintMapper(
            MakeCanonicalizer.Load(TestPaths.DataFile("make-aliases.json")),
            new ModelCanonicalizer(),
            ComponentCanonicalizer.Load(TestPaths.DataFile("component-groups.json")));

        var complaint = mapper.Map(Record(
            (ComplaintFields.MakeText, "VW    "),
            (ComplaintFields.ModelText, "JETTA "),
            (ComplaintFields.YearText, "2015"),
            (ComplaintFields.ComponentDescription, "POWER TRAIN:AUTOMATIC TRANSMISSION")));

        Assert.Equal("VOLKSWAGEN", complaint.Make);
        Assert.Equal("POWER TRAIN", complaint.Component.Group);
        Assert.Equal("volkswagen|jetta|2015", complaint.VehicleKey);
        Assert.Equal("MERCEDES-BENZ", mapper.Map(Record((ComplaintFields.MakeText, "MERCEDES BENZ"))).Make);
    }

    private static string[] RecordWithSeverityFlags(string flag)
    {
        var fields = Record();
        foreach (var index in SeverityFlagFields)
        {
            fields[index] = flag;
        }

        return fields;
    }

    private static IEnumerable<string> TextValuesOf(Complaint complaint)
    {
        foreach (var property in typeof(Complaint).GetProperties())
        {
            if (property.GetValue(complaint) is string text)
            {
                yield return text;
            }
        }

        yield return complaint.Component.Raw;
        yield return complaint.Component.Group;

        foreach (var level in complaint.Component.Levels)
        {
            yield return level;
        }
    }
}
