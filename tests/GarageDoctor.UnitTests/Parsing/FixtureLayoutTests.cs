using GarageDoctor.Domain.Parsing;
using GarageDoctor.Tests;

namespace GarageDoctor.UnitTests.Parsing;

public sealed class FixtureLayoutTests
{
    [Fact]
    public void ComplaintFieldConstantsSitAtTheZeroBasedIndexesOfTheOfficialSpecification()
    {
        Assert.Equal(3, ComplaintFields.MakeText);
        Assert.Equal(11, ComplaintFields.ComponentDescription);
        Assert.Equal(17, ComplaintFields.Miles);
        Assert.Equal(19, ComplaintFields.ComplaintDescription);
        Assert.Equal(45, ComplaintFields.ProductType);
        Assert.Equal(51, ComplaintFields.FieldCount);
    }

    [Fact]
    public async Task ComplaintFieldConstantsSelectTheExpectedColumnsOfTheValidFixture()
    {
        var first = await FirstRecordAsync("complaints-valid.tsv", FlatFileParser.ForComplaints());

        Assert.Equal("NORDVIK", first[ComplaintFields.MakeText]);
        Assert.Equal("FJORDLINE", first[ComplaintFields.ModelText]);
        Assert.Equal("41250", first[ComplaintFields.Miles]);
        Assert.Equal(ComplaintFields.VehicleProductType, first[ComplaintFields.ProductType]);
        Assert.Equal("ELECTRICAL SYSTEM", first[ComplaintFields.ComponentDescription]);
        Assert.StartsWith("THE DASHBOARD DISPLAY WENT BLANK", first[ComplaintFields.ComplaintDescription], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryValidFixtureRowCarriesAMakeAMileageReadingAndAVehicleProductType()
    {
        var records = await ReadAllAsync("complaints-valid.tsv", FlatFileParser.ForComplaints());

        Assert.All(records, record => Assert.NotEmpty(record[ComplaintFields.MakeText]));
        Assert.All(records, record => Assert.True(int.TryParse(record[ComplaintFields.Miles], out _)));
        Assert.All(records, record => Assert.Equal(ComplaintFields.VehicleProductType, record[ComplaintFields.ProductType]));
        Assert.All(records, record => Assert.NotEmpty(record[ComplaintFields.ComponentDescription]));
        Assert.All(records, record => Assert.NotEmpty(record[ComplaintFields.ComplaintDescription]));
    }

    [Fact]
    public void PersonalDataFieldsCoverTheDealerColumnsTheVinTheCityAndTheVehicleOperator()
    {
        Assert.Contains(ComplaintFields.DealerName, ComplaintFields.PersonalDataFields);
        Assert.Contains(ComplaintFields.DealerTelephone, ComplaintFields.PersonalDataFields);
        Assert.Contains(ComplaintFields.DealerCity, ComplaintFields.PersonalDataFields);
        Assert.Contains(ComplaintFields.DealerState, ComplaintFields.PersonalDataFields);
        Assert.Contains(ComplaintFields.DealerZip, ComplaintFields.PersonalDataFields);
        Assert.Contains(ComplaintFields.Vin, ComplaintFields.PersonalDataFields);
        Assert.Contains(ComplaintFields.City, ComplaintFields.PersonalDataFields);
        Assert.Contains(ComplaintFields.VehicleOperator, ComplaintFields.PersonalDataFields);
    }

    [Fact]
    public async Task EveryPersonalDataColumnOfTheValidFixtureHoldsAnObviousPlaceholder()
    {
        var records = await ReadAllAsync("complaints-valid.tsv", FlatFileParser.ForComplaints());

        Assert.All(records, record => Assert.StartsWith("FAKE-CITY-", record[ComplaintFields.City], StringComparison.Ordinal));
        Assert.All(records, record => Assert.StartsWith("FAKEVIN", record[ComplaintFields.Vin], StringComparison.Ordinal));
        Assert.All(records, record => Assert.StartsWith("FAKE DEALER ", record[ComplaintFields.DealerName], StringComparison.Ordinal));
        Assert.All(records, record => Assert.StartsWith("555-", record[ComplaintFields.DealerTelephone], StringComparison.Ordinal));
        Assert.All(records, record => Assert.StartsWith("FAKE-DEALER-CITY-", record[ComplaintFields.DealerCity], StringComparison.Ordinal));
        Assert.All(records, record => Assert.StartsWith("FAKE-OPERATOR-", record[ComplaintFields.VehicleOperator], StringComparison.Ordinal));
        Assert.All(records, record => Assert.All(ComplaintFields.PersonalDataFields, index => Assert.NotEmpty(record[index])));
    }

    [Fact]
    public async Task EdgeFixtureCoversEveryProductTypeCodeIncludingAnEmptyOne()
    {
        var records = await ReadAllAsync("complaints-edge.tsv", FlatFileParser.ForComplaints());
        var productTypes = records.Select(record => record[ComplaintFields.ProductType]).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("V", productTypes);
        Assert.Contains("T", productTypes);
        Assert.Contains("C", productTypes);
        Assert.Contains("E", productTypes);
        Assert.Contains("", productTypes);
    }

    [Fact]
    public async Task EdgeFixtureCoversComponentDescriptionsFromOneToFourLevels()
    {
        var records = await ReadAllAsync("complaints-edge.tsv", FlatFileParser.ForComplaints());
        var depths = records
            .Select(record => record[ComplaintFields.ComponentDescription])
            .Where(description => description.Length > 0)
            .Select(description => description.Split(':').Length)
            .ToHashSet();

        Assert.Contains(1, depths);
        Assert.Contains(2, depths);
        Assert.Contains(3, depths);
        Assert.Contains(4, depths);
    }

    [Fact]
    public async Task EdgeFixtureCoversTheMakeSpellingsThatCanonicalizationHasToCollapse()
    {
        var records = await ReadAllAsync("complaints-edge.tsv", FlatFileParser.ForComplaints());
        var makes = records.Select(record => record[ComplaintFields.MakeText]).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MERCEDES BENZ", makes);
        Assert.Contains("MERCEDES-BENZ", makes);
        Assert.Contains("VW", makes);
        Assert.Contains("CHEVY", makes);
        Assert.Contains("FORD MOTOR CO, INC", makes);
        Assert.Contains("  ford   motor  co  ", makes);
    }

    [Fact]
    public async Task EdgeFixtureCoversTheModelYearAndMileageSentinels()
    {
        var records = await ReadAllAsync("complaints-edge.tsv", FlatFileParser.ForComplaints());
        var years = records.Select(record => record[ComplaintFields.YearText]).ToHashSet(StringComparer.Ordinal);
        var mileages = records.Select(record => record[ComplaintFields.Miles]).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("9999", years);
        Assert.Contains("", years);
        Assert.Contains("N/A", years);
        Assert.Contains("0", mileages);
        Assert.Contains("", mileages);
        Assert.Contains("9999999", mileages);
    }

    [Fact]
    public async Task EdgeFixtureCoversEveryYesNoAndEmptyStateOfTheCrashFlag()
    {
        var records = await ReadAllAsync("complaints-edge.tsv", FlatFileParser.ForComplaints());
        var crashFlags = records.Select(record => record[ComplaintFields.Crash]).ToHashSet(StringComparer.Ordinal);
        var fireFlags = records.Select(record => record[ComplaintFields.Fire]).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(3, crashFlags.Count);
        Assert.Equal(3, fireFlags.Count);
        Assert.Contains("Y", crashFlags);
        Assert.Contains("N", crashFlags);
        Assert.Contains("", crashFlags);
    }

    [Fact]
    public async Task NoFixtureNarrativeContainsATabCharacter()
    {
        var records = await ReadAllAsync("complaints-edge.tsv", FlatFileParser.ForComplaints());
        var narrativeWithSpacedCodes = records
            .Select(record => record[ComplaintFields.ComplaintDescription])
            .Single(description => description.Contains("P0301", StringComparison.Ordinal));

        Assert.DoesNotContain("\t", narrativeWithSpacedCodes, StringComparison.Ordinal);
        Assert.Contains("  P0302  ", narrativeWithSpacedCodes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecallFieldConstantsSelectTheExpectedColumnsOfTheRecallsFixture()
    {
        var records = await ReadAllAsync("recalls-sample.tsv", FlatFileParser.ForRecalls());
        var first = records[0];

        Assert.Equal(29, RecallFields.FieldCount);
        Assert.Equal("25V001000", first[RecallFields.CampaignNumber]);
        Assert.Equal("NORDVIK", first[RecallFields.MakeText]);
        Assert.Equal("FJORDLINE", first[RecallFields.ModelText]);
        Assert.Equal(RecallFields.VehicleRecallType, first[RecallFields.RecallType]);
        Assert.Equal("48210", first[RecallFields.PotentiallyAffected]);
        Assert.Contains(records, record => record[RecallFields.PotentiallyAffected].Length == 0);
        Assert.Contains(records, record => record[RecallFields.DoNotDrive] == "Yes");
        Assert.Contains(records, record => record[RecallFields.ParkOutside] == "Yes");
    }

    [Fact]
    public async Task EdgeFixtureCoversEmptyAndMalformedDates()
    {
        var records = await ReadAllAsync("complaints-edge.tsv", FlatFileParser.ForComplaints());
        var failureDates = records.Select(record => record[ComplaintFields.FailureDate]).ToHashSet(StringComparer.Ordinal);
        var addedDates = records.Select(record => record[ComplaintFields.DateAdded]).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("", failureDates);
        Assert.Contains("20221345", failureDates);
        Assert.Contains("", addedDates);
        Assert.Contains("2020-06-09", addedDates);
    }

    [Fact]
    public async Task TrailingSpacePaddingReachesTheCallerUntrimmed()
    {
        var records = await ReadAllAsync("complaints-edge.tsv", FlatFileParser.ForComplaints());

        Assert.Equal("FAKE-CITY-105    ", records[4][ComplaintFields.City]);
        Assert.Equal("TOWMASTER 200  ", records[8][ComplaintFields.ModelText]);
        Assert.Equal("  ford   motor  co  ", records[15][ComplaintFields.MakeText]);
    }

    private static async Task<string[]> FirstRecordAsync(string fixture, FlatFileParser parser)
    {
        var records = await ReadAllAsync(fixture, parser);
        return records[0];
    }

    private static async Task<List<string[]>> ReadAllAsync(string fixture, FlatFileParser parser)
    {
        var records = new List<string[]>();

        await foreach (var record in parser.ReadRecordsAsync(TestPaths.Fixture(fixture)))
        {
            records.Add(record);
        }

        return records;
    }
}
