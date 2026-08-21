using System.Text.Json;
using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Tests;

namespace GarageDoctor.UnitTests.Canonicalization;

public sealed class MakeCanonicalizerTests
{
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["MERCEDES"] = "MERCEDES-BENZ",
        ["MERCEDES BENZ"] = "MERCEDES-BENZ",
        ["MERCEDES-BENZ"] = "MERCEDES-BENZ",
        ["VW"] = "VOLKSWAGEN",
        ["VOLKSWAGEN"] = "VOLKSWAGEN",
        ["CHEVY"] = "CHEVROLET",
        ["CHEVROLET"] = "CHEVROLET",
        ["CHEVROLET TRUCK"] = "CHEVROLET",
        ["LANDROVER"] = "LAND ROVER",
        ["LAND ROVER"] = "LAND ROVER",
        ["MACK TRUCKS"] = "MACK",
        ["AM TRAN"] = "AMTRAN"
    };

    private static MakeCanonicalizer Canonicalizer() => MakeCanonicalizer.FromAliases(Aliases);

    [Theory]
    [InlineData("MERCEDES")]
    [InlineData("MERCEDES BENZ")]
    [InlineData("MERCEDES-BENZ")]
    [InlineData("MERCEDES-BENz")]
    [InlineData("  mercedes   benz  ")]
    public void CollapsesMercedesVariantsToASingleCanonicalName(string raw)
    {
        var canonical = Canonicalizer().Canonicalize(raw);

        Assert.Equal("MERCEDES-BENZ", canonical.Name);
        Assert.Equal("mercedes-benz", canonical.Slug);
    }

    [Theory]
    [InlineData("VW", "VOLKSWAGEN", "volkswagen")]
    [InlineData("VOLKSWAGEN", "VOLKSWAGEN", "volkswagen")]
    [InlineData("CHEVY", "CHEVROLET", "chevrolet")]
    [InlineData("CHEVROLET", "CHEVROLET", "chevrolet")]
    [InlineData("CHEVROLET TRUCK", "CHEVROLET", "chevrolet")]
    [InlineData("LANDROVER", "LAND ROVER", "land-rover")]
    [InlineData("LAND ROVER", "LAND ROVER", "land-rover")]
    public void AppliesTheRequiredAliases(string raw, string expectedName, string expectedSlug)
    {
        var canonical = Canonicalizer().Canonicalize(raw);

        Assert.Equal(expectedName, canonical.Name);
        Assert.Equal(expectedSlug, canonical.Slug);
    }

    [Theory]
    [InlineData("ELECTRIC TRANSIT, INC.", "ELECTRIC TRANSIT")]
    [InlineData("ELECTRIC TRANSIT INC", "ELECTRIC TRANSIT")]
    [InlineData("ELECTRIC TRANSIT L.L.C.", "ELECTRIC TRANSIT")]
    [InlineData("ALUMINUM TRAILER CO", "ALUMINUM TRAILER")]
    [InlineData("ALUMINUM TRAILER CO.", "ALUMINUM TRAILER")]
    [InlineData("T.A. PELSUE COMPANY", "T.A. PELSUE")]
    [InlineData("EAGLE BUS CORPORATION", "EAGLE BUS")]
    [InlineData("EAGLE BUS CORP", "EAGLE BUS")]
    [InlineData("EAGLE BUS LTD.", "EAGLE BUS")]
    public void StripsTrailingCorporateSuffixes(string raw, string expectedName)
    {
        Assert.Equal(expectedName, Canonicalizer().Canonicalize(raw).Name);
    }

    [Fact]
    public void StripsChainedCorporateSuffixes()
    {
        Assert.Equal("EAGLE BUS", Canonicalizer().Canonicalize("EAGLE BUS CO., LTD.").Name);
    }

    [Theory]
    [InlineData("GENERAL MOTORS")]
    [InlineData("AMERICAN MOTORS")]
    [InlineData("BATTLE MOTORS")]
    [InlineData("INTERNATIONAL MOTORS")]
    public void NeverStripsMotorsBecauseItCarriesTheBrandIdentity(string raw)
    {
        Assert.Equal(raw, Canonicalizer().Canonicalize(raw).Name);
    }

    [Theory]
    [InlineData("INC")]
    [InlineData("CO")]
    [InlineData("LTD")]
    public void NeverStripsASuffixThatIsTheWholeName(string raw)
    {
        Assert.Equal(raw, Canonicalizer().Canonicalize(raw).Name);
    }

    [Fact]
    public void AppliesAliasesAfterSuffixStripping()
    {
        var canonicalizer = Canonicalizer();

        Assert.Equal("MACK", canonicalizer.Canonicalize("MACK TRUCKS INC.").Name);
        Assert.Equal("AMTRAN", canonicalizer.Canonicalize("AM TRAN CORP").Name);
    }

    [Theory]
    [InlineData("   FORD   ", "FORD")]
    [InlineData("LAND\t\tROVER", "LAND ROVER")]
    [InlineData("HARLEY  -  DAVIDSON", "HARLEY - DAVIDSON")]
    public void TrimsAndCollapsesRepeatedWhitespace(string raw, string expectedName)
    {
        Assert.Equal(expectedName, Canonicalizer().Canonicalize(raw).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(".")]
    public void MapsEmptyAndPunctuationOnlyInputToUnknown(string raw)
    {
        var canonical = Canonicalizer().Canonicalize(raw);

        Assert.Equal("UNKNOWN", canonical.Name);
        Assert.Equal("unknown", canonical.Slug);
    }

    [Fact]
    public void KeepsUnmappedMakesAndSlugsThem()
    {
        var canonical = Canonicalizer().Canonicalize("harley-davidson");

        Assert.Equal("HARLEY-DAVIDSON", canonical.Name);
        Assert.Equal("harley-davidson", canonical.Slug);
    }

    [Fact]
    public void ResolvesAliasChainsToTheFinalTarget()
    {
        var canonicalizer = MakeCanonicalizer.FromAliases(new Dictionary<string, string>
        {
            ["CHEVY"] = "CHEVROLET TRUCK",
            ["CHEVROLET TRUCK"] = "CHEVROLET"
        });

        Assert.Equal("CHEVROLET", canonicalizer.Canonicalize("CHEVY").Name);
    }

    [Fact]
    public void ToleratesSelfReferencingAliases()
    {
        var canonicalizer = MakeCanonicalizer.FromAliases(new Dictionary<string, string>
        {
            ["FORD"] = "FORD"
        });

        Assert.Equal("FORD", canonicalizer.Canonicalize("FORD").Name);
    }

    [Fact]
    public void BuildsTheVehicleKeyExpectedByTheProfileCollection()
    {
        var make = Canonicalizer().Canonicalize("Audi");
        var model = new ModelCanonicalizer().Canonicalize("A3");

        Assert.Equal("audi|a3|2015", VehicleKey.Create(make.Slug, model.Slug, 2015));
    }

    [Fact]
    public void LoadsTheRepositoryAliasMapAndAppliesTheRequiredMerges()
    {
        var path = TestPaths.DataFile("make-aliases.json");
        Assert.True(File.Exists(path), $"Missing alias map at '{path}'.");

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Assert.NotNull(raw);
        Assert.NotEmpty(raw);

        var canonicalizer = MakeCanonicalizer.Load(path);

        Assert.Equal("MERCEDES-BENZ", canonicalizer.Canonicalize("MERCEDES").Name);
        Assert.Equal("MERCEDES-BENZ", canonicalizer.Canonicalize("MERCEDES BENZ").Name);
        Assert.Equal("MERCEDES-BENZ", canonicalizer.Canonicalize("MERCEDES-BENZ").Name);
        Assert.Equal("VOLKSWAGEN", canonicalizer.Canonicalize("VW").Name);
        Assert.Equal("VOLKSWAGEN", canonicalizer.Canonicalize("VOLKSWAGEN").Name);
        Assert.Equal("CHEVROLET", canonicalizer.Canonicalize("CHEVY").Name);
        Assert.Equal("CHEVROLET", canonicalizer.Canonicalize("CHEVROLET").Name);
        Assert.Equal("LAND ROVER", canonicalizer.Canonicalize("LANDROVER").Name);
        Assert.Equal("LAND ROVER", canonicalizer.Canonicalize("LAND ROVER").Name);
    }
}
