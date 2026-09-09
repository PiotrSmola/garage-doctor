using GarageDoctor.Domain.Canonicalization;

namespace GarageDoctor.UnitTests.Canonicalization;

public sealed class ModelCanonicalizerTests
{
    private readonly ModelCanonicalizer _canonicalizer = new();

    [Theory]
    [InlineData("a3", "A3", "a3")]
    [InlineData("  F-150  ", "F-150", "f-150")]
    [InlineData("grand   cherokee", "GRAND CHEROKEE", "grand-cherokee")]
    [InlineData("Silverado\t1500", "SILVERADO 1500", "silverado-1500")]
    public void TrimsUppercasesAndCollapsesWhitespace(string raw, string expectedName, string expectedSlug)
    {
        var canonical = _canonicalizer.Canonicalize(raw);

        Assert.Equal(expectedName, canonical.Name);
        Assert.Equal(expectedSlug, canonical.Slug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t")]
    public void MapsEmptyInputToUnknown(string raw)
    {
        var canonical = _canonicalizer.Canonicalize(raw);

        Assert.Equal("UNKNOWN", canonical.Name);
        Assert.Equal("unknown", canonical.Slug);
    }

    [Fact]
    public void MapsPunctuationOnlyInputToUnknown()
    {
        Assert.Equal(CanonicalName.Unknown, _canonicalizer.Canonicalize("---"));
    }

    [Fact]
    public void AppliesNoAliasMapSoMakeAliasesLeaveModelsUntouched()
    {
        Assert.Equal("CHEVY", _canonicalizer.Canonicalize("chevy").Name);
        Assert.Equal("MERCEDES BENZ", _canonicalizer.Canonicalize("mercedes benz").Name);
    }

    [Fact]
    public void KeepsCorporateSuffixesBecauseTheyAreMeaningfulInModelNames()
    {
        Assert.Equal("EXPRESS CO", _canonicalizer.Canonicalize("Express CO").Name);
    }
}
