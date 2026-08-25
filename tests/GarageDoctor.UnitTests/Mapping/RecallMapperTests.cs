using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Domain.Parsing;

namespace GarageDoctor.UnitTests.Mapping;

public sealed class RecallMapperTests
{
    private static readonly Dictionary<string, string> MakeAliases = new()
    {
        ["MERCEDES BENZ"] = "MERCEDES-BENZ",
        ["MERCEDES"] = "MERCEDES-BENZ",
        ["VW"] = "VOLKSWAGEN"
    };

    private static readonly Dictionary<string, string> ComponentGroups = new()
    {
        ["ELECTRICAL SYSTEM"] = "ELECTRICAL",
        ["ENGINE AND ENGINE COOLING"] = "ENGINE",
        ["AIR BAGS"] = "AIR BAGS"
    };

    private static RecallMapper Mapper() => new(
        MakeCanonicalizer.FromAliases(MakeAliases),
        new ModelCanonicalizer(),
        ComponentCanonicalizer.FromGroups(ComponentGroups));

    private static string[] EmptyRecord()
    {
        var fields = new string[RecallFields.FieldCount];
        Array.Fill(fields, string.Empty);
        return fields;
    }

    private static string[] VehicleRecord()
    {
        var fields = EmptyRecord();
        fields[RecallFields.RecordId] = "244499";
        fields[RecallFields.CampaignNumber] = "24V123000";
        fields[RecallFields.MakeText] = "MERCEDES BENZ";
        fields[RecallFields.ModelText] = "C300";
        fields[RecallFields.YearText] = "2019";
        fields[RecallFields.ComponentName] = "ELECTRICAL SYSTEM:WIRING";
        fields[RecallFields.ManufacturerName] = "Mercedes-Benz USA, LLC";
        fields[RecallFields.RecallType] = "V";
        fields[RecallFields.PotentiallyAffected] = "12345";
        fields[RecallFields.OwnersNotifiedDate] = "20240301";
        fields[RecallFields.ReportReceivedDate] = "20240215";
        fields[RecallFields.DefectDescription] = "The wiring harness may chafe.";
        fields[RecallFields.Consequence] = "A short circuit may cause a fire.";
        fields[RecallFields.CorrectiveAction] = "Dealers will replace the harness.";
        fields[RecallFields.DoNotDrive] = "No";
        fields[RecallFields.ParkOutside] = "No";
        return fields;
    }

    [Fact]
    public void MapsEveryPropertyOfAVehicleRecall()
    {
        var fields = VehicleRecord();
        fields[RecallFields.MakeText] = "  MERCEDES BENZ  ";
        fields[RecallFields.ModelText] = " C300 ";
        fields[RecallFields.ParkOutside] = "Yes";

        var campaign = Mapper().Map(fields);

        Assert.Equal(244499, campaign.Id);
        Assert.Equal("24V123000", campaign.CampaignNumber);
        Assert.Equal("Mercedes-Benz USA, LLC", campaign.Manufacturer);
        Assert.Equal("MERCEDES-BENZ", campaign.Make);
        Assert.Equal("MERCEDES BENZ", campaign.MakeRaw);
        Assert.Equal("C300", campaign.Model);
        Assert.Equal("C300", campaign.ModelRaw);
        Assert.Equal(2019, campaign.ModelYear);
        Assert.Equal("ELECTRICAL SYSTEM:WIRING", campaign.ComponentName);
        Assert.Equal("ELECTRICAL", campaign.ComponentGroup);
        Assert.Equal("V", campaign.RecallType);
        Assert.Equal(12345, campaign.PotentiallyAffected);
        Assert.Equal(new DateOnly(2024, 3, 1), campaign.OwnersNotifiedDate);
        Assert.Equal(new DateOnly(2024, 2, 15), campaign.ReportReceivedDate);
        Assert.Equal("The wiring harness may chafe.", campaign.DefectDescription);
        Assert.Equal("A short circuit may cause a fire.", campaign.Consequence);
        Assert.Equal("Dealers will replace the harness.", campaign.CorrectiveAction);
        Assert.False(campaign.DoNotDrive);
        Assert.True(campaign.ParkOutside);
        Assert.Equal("mercedes-benz|c300|2019", campaign.VehicleKey);
    }

    [Theory]
    [InlineData("V", true)]
    [InlineData(" V ", true)]
    [InlineData("E", false)]
    [InlineData("C", false)]
    [InlineData("T", false)]
    [InlineData("", false)]
    public void RecognizesOnlyVehicleRecalls(string recallType, bool expected)
    {
        var fields = VehicleRecord();
        fields[RecallFields.RecallType] = recallType;

        Assert.Equal(expected, Mapper().IsVehicleRecall(fields));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12 34")]
    [InlineData("1.0")]
    [InlineData("-7")]
    public void ThrowsWhenTheRecordIdIsNotNumeric(string recordId)
    {
        var fields = VehicleRecord();
        fields[RecallFields.RecordId] = recordId;

        var exception = Assert.Throws<RecallMappingException>(() => Mapper().Map(fields));

        Assert.Equal(recordId, exception.RecordId);
    }

    [Fact]
    public void ThrowsWhenTheRecordIdIsEmpty()
    {
        var fields = VehicleRecord();
        fields[RecallFields.RecordId] = "   ";

        var exception = Assert.Throws<RecallMappingException>(() => Mapper().Map(fields));

        Assert.Equal("unknown", exception.RecordId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ThrowsWhenTheCampaignNumberIsEmpty(string campaignNumber)
    {
        var fields = VehicleRecord();
        fields[RecallFields.CampaignNumber] = campaignNumber;

        var exception = Assert.Throws<RecallMappingException>(() => Mapper().Map(fields));

        Assert.Equal("244499", exception.RecordId);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(30)]
    public void ThrowsWhenTheFieldCountIsWrong(int length)
    {
        var fields = new string[length];
        Array.Fill(fields, string.Empty);
        fields[RecallFields.RecordId] = "244499";

        var mapper = Mapper();

        Assert.Equal("244499", Assert.Throws<RecallMappingException>(() => mapper.Map(fields)).RecordId);
        Assert.Equal("244499", Assert.Throws<RecallMappingException>(() => mapper.IsVehicleRecall(fields)).RecordId);
    }

    [Fact]
    public void ThrowsWithAnUnknownRecordIdWhenTheRecordIsBlank()
    {
        var exception = Assert.Throws<RecallMappingException>(() => Mapper().Map([string.Empty]));

        Assert.Equal("unknown", exception.RecordId);
    }

    [Theory]
    [InlineData("9999")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UNKN")]
    [InlineData("1899")]
    [InlineData("2036")]
    public void LeavesTheModelYearNullWhenYearTextIsNotUsable(string yearText)
    {
        var fields = VehicleRecord();
        fields[RecallFields.YearText] = yearText;

        var campaign = Mapper().Map(fields);

        Assert.Null(campaign.ModelYear);
        Assert.Equal("mercedes-benz|c300|unknown", campaign.VehicleKey);
    }

    [Theory]
    [InlineData("1900", 1900)]
    [InlineData("2019", 2019)]
    [InlineData("2035", 2035)]
    public void KeepsAModelYearInsideTheSupportedRange(string yearText, int expected)
    {
        var fields = VehicleRecord();
        fields[RecallFields.YearText] = yearText;

        Assert.Equal(expected, Mapper().Map(fields).ModelYear);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData("1,200")]
    public void LeavesPotentiallyAffectedNullWhenItIsEmptyOrNotANumber(string potentiallyAffected)
    {
        var fields = VehicleRecord();
        fields[RecallFields.PotentiallyAffected] = potentiallyAffected;

        Assert.Null(Mapper().Map(fields).PotentiallyAffected);
    }

    [Fact]
    public void KeepsAGenuineZeroPotentiallyAffected()
    {
        var fields = VehicleRecord();
        fields[RecallFields.PotentiallyAffected] = "0";

        Assert.Equal(0, Mapper().Map(fields).PotentiallyAffected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("2024")]
    [InlineData("03/01/2024")]
    [InlineData("20240230")]
    [InlineData("20241301")]
    [InlineData("notadate")]
    public void LeavesDatesNullWhenTheyAreNotRealYyyyMmDdDates(string date)
    {
        var fields = VehicleRecord();
        fields[RecallFields.OwnersNotifiedDate] = date;
        fields[RecallFields.ReportReceivedDate] = date;

        var campaign = Mapper().Map(fields);

        Assert.Null(campaign.OwnersNotifiedDate);
        Assert.Null(campaign.ReportReceivedDate);
    }

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData(" Yes ", true)]
    [InlineData("No", false)]
    [InlineData("", false)]
    [InlineData("Y", false)]
    [InlineData("Yes please", false)]
    public void MapsTheConsumerAdvisoryFlags(string flag, bool expected)
    {
        var fields = VehicleRecord();
        fields[RecallFields.DoNotDrive] = flag;
        fields[RecallFields.ParkOutside] = flag;

        var campaign = Mapper().Map(fields);

        Assert.Equal(expected, campaign.DoNotDrive);
        Assert.Equal(expected, campaign.ParkOutside);
    }

    [Fact]
    public void TreatsAnAdvisoryFlagWithATrailingCarriageReturnAsSet()
    {
        var fields = VehicleRecord();
        fields[RecallFields.DoNotDrive] = "Yes\r";
        fields[RecallFields.ParkOutside] = "Yes\r";

        var campaign = Mapper().Map(fields);

        Assert.True(campaign.DoNotDrive);
        Assert.True(campaign.ParkOutside);
    }

    [Fact]
    public void MapsADoNotDriveCampaignThatMayStillBeParkedInside()
    {
        var fields = VehicleRecord();
        fields[RecallFields.DoNotDrive] = "Yes";
        fields[RecallFields.ParkOutside] = "No";

        var campaign = Mapper().Map(fields);

        Assert.True(campaign.DoNotDrive);
        Assert.False(campaign.ParkOutside);
    }

    [Fact]
    public void TrimsTheNarrativeFieldsAndKeepsEmptyOnesAsEmptyStrings()
    {
        var fields = VehicleRecord();
        fields[RecallFields.DefectDescription] = "  The wiring harness may chafe.\r";
        fields[RecallFields.Consequence] = string.Empty;
        fields[RecallFields.CorrectiveAction] = "   ";

        var campaign = Mapper().Map(fields);

        Assert.Equal("The wiring harness may chafe.", campaign.DefectDescription);
        Assert.Equal(string.Empty, campaign.Consequence);
        Assert.Equal(string.Empty, campaign.CorrectiveAction);
    }

    [Fact]
    public void CanonicalizesTheMakeThroughTheMapper()
    {
        var fields = VehicleRecord();
        fields[RecallFields.MakeText] = "MERCEDES BENZ";

        var campaign = Mapper().Map(fields);

        Assert.Equal("MERCEDES-BENZ", campaign.Make);
        Assert.Equal("MERCEDES BENZ", campaign.MakeRaw);
        Assert.Equal("mercedes-benz|c300|2019", campaign.VehicleKey);
    }

    [Fact]
    public void MapsAColonSeparatedComponentNameToItsCanonicalGroup()
    {
        var fields = VehicleRecord();
        fields[RecallFields.ComponentName] = " ENGINE AND ENGINE COOLING:COOLING SYSTEM:RADIATOR ";

        var campaign = Mapper().Map(fields);

        Assert.Equal("ENGINE AND ENGINE COOLING:COOLING SYSTEM:RADIATOR", campaign.ComponentName);
        Assert.Equal("ENGINE", campaign.ComponentGroup);
    }

    [Fact]
    public void MapsAComponentNameWithoutColonsAsItsOwnTopLevel()
    {
        var fields = VehicleRecord();
        fields[RecallFields.ComponentName] = "AIR BAGS";

        var campaign = Mapper().Map(fields);

        Assert.Equal("AIR BAGS", campaign.ComponentName);
        Assert.Equal("AIR BAGS", campaign.ComponentGroup);
    }

    [Fact]
    public void FallsBackToTheOtherGroupForAnUnmappedComponentName()
    {
        var fields = VehicleRecord();
        fields[RecallFields.ComponentName] = "TRAILER HITCHES";

        var campaign = Mapper().Map(fields);

        Assert.Equal("TRAILER HITCHES", campaign.ComponentName);
        Assert.Equal(ComponentCanonicalizer.FallbackGroup, campaign.ComponentGroup);
    }

    [Fact]
    public void MapsAnEmptyComponentNameToTheUnknownGroup()
    {
        var fields = VehicleRecord();
        fields[RecallFields.ComponentName] = "   ";

        var campaign = Mapper().Map(fields);

        Assert.Equal(string.Empty, campaign.ComponentName);
        Assert.Equal(ComponentCanonicalizer.UnknownGroup, campaign.ComponentGroup);
    }

    [Fact]
    public void KeepsOneCampaignNumberAcrossModelYearsWhileTheVehicleKeyDiffers()
    {
        var mapper = Mapper();
        var first = VehicleRecord();
        first[RecallFields.RecordId] = "1";
        first[RecallFields.YearText] = "2019";
        var second = VehicleRecord();
        second[RecallFields.RecordId] = "2";
        second[RecallFields.YearText] = "2020";

        var firstCampaign = mapper.Map(first);
        var secondCampaign = mapper.Map(second);

        Assert.Equal(firstCampaign.CampaignNumber, secondCampaign.CampaignNumber);
        Assert.Equal("mercedes-benz|c300|2019", firstCampaign.VehicleKey);
        Assert.Equal("mercedes-benz|c300|2020", secondCampaign.VehicleKey);
        Assert.NotEqual(firstCampaign.VehicleKey, secondCampaign.VehicleKey);
    }
}
