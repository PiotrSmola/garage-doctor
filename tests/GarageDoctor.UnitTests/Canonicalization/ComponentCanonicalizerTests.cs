using System.Text.Json;
using GarageDoctor.Domain.Canonicalization;
using GarageDoctor.Tests;

namespace GarageDoctor.UnitTests.Canonicalization;

public sealed class ComponentCanonicalizerTests
{
    private static readonly Dictionary<string, string> Groups = new()
    {
        ["ENGINE"] = "ENGINE",
        ["ENGINE AND ENGINE COOLING"] = "ENGINE",
        ["SERVICE BRAKES"] = "BRAKES",
        ["SERVICE BRAKES, HYDRAULIC"] = "BRAKES",
        ["SERVICE BRAKES, AIR"] = "BRAKES",
        ["FUEL SYSTEM, GASOLINE"] = "FUEL SYSTEM",
        ["FUEL SYSTEM, DIESEL"] = "FUEL SYSTEM",
        ["FUEL/PROPULSION SYSTEM"] = "FUEL SYSTEM",
        ["POWER TRAIN"] = "POWER TRAIN",
        ["AIR BAGS"] = "AIR BAGS"
    };

    private static ComponentCanonicalizer Canonicalizer() => ComponentCanonicalizer.FromGroups(Groups);

    [Fact]
    public void SplitsASingleLevelDescription()
    {
        var path = Canonicalizer().Canonicalize("POWER TRAIN");

        Assert.Equal("POWER TRAIN", path.Raw);
        Assert.Equal("POWER TRAIN", path.Group);
        Assert.Equal(new[] { "POWER TRAIN" }, path.Levels);
    }

    [Fact]
    public void SplitsTwoLevels()
    {
        var path = Canonicalizer().Canonicalize("SERVICE BRAKES, HYDRAULIC:ANTILOCK/TRACTION");

        Assert.Equal("BRAKES", path.Group);
        Assert.Equal(new[] { "SERVICE BRAKES, HYDRAULIC", "ANTILOCK/TRACTION" }, path.Levels);
    }

    [Fact]
    public void SplitsThreeLevels()
    {
        var path = Canonicalizer().Canonicalize("ENGINE AND ENGINE COOLING:COOLING SYSTEM:RADIATOR ASSEMBLY");

        Assert.Equal("ENGINE", path.Group);
        Assert.Equal(new[] { "ENGINE AND ENGINE COOLING", "COOLING SYSTEM", "RADIATOR ASSEMBLY" }, path.Levels);
    }

    [Fact]
    public void SplitsFourLevels()
    {
        var path = Canonicalizer().Canonicalize("AIR BAGS:FRONTAL:DRIVER SIDE:INFLATOR MODULE");

        Assert.Equal("AIR BAGS", path.Group);
        Assert.Equal(new[] { "AIR BAGS", "FRONTAL", "DRIVER SIDE", "INFLATOR MODULE" }, path.Levels);
    }

    [Fact]
    public void TrimsEveryLevelAndDropsEmptyOnes()
    {
        var path = Canonicalizer().Canonicalize("  POWER TRAIN : : AUTOMATIC TRANSMISSION :  ");

        Assert.Equal("POWER TRAIN", path.Group);
        Assert.Equal(new[] { "POWER TRAIN", "AUTOMATIC TRANSMISSION" }, path.Levels);
    }

    [Theory]
    [InlineData("ENGINE", "ENGINE")]
    [InlineData("ENGINE AND ENGINE COOLING", "ENGINE")]
    [InlineData("SERVICE BRAKES", "BRAKES")]
    [InlineData("SERVICE BRAKES, HYDRAULIC", "BRAKES")]
    [InlineData("SERVICE BRAKES, AIR", "BRAKES")]
    [InlineData("FUEL SYSTEM, GASOLINE", "FUEL SYSTEM")]
    [InlineData("FUEL SYSTEM, DIESEL", "FUEL SYSTEM")]
    [InlineData("FUEL/PROPULSION SYSTEM", "FUEL SYSTEM")]
    [InlineData("POWER TRAIN", "POWER TRAIN")]
    public void AppliesTheDocumentedGroupMerges(string topLevel, string expectedGroup)
    {
        Assert.Equal(expectedGroup, Canonicalizer().Canonicalize(topLevel).Group);
    }

    [Fact]
    public void MatchesTopLevelsCaseInsensitively()
    {
        Assert.Equal("ENGINE", Canonicalizer().Canonicalize("engine and engine cooling:cooling system").Group);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":")]
    [InlineData(" : : ")]
    public void MapsEmptyDescriptionToUnknownWithNoLevels(string raw)
    {
        var path = Canonicalizer().Canonicalize(raw);

        Assert.Equal("UNKNOWN", path.Group);
        Assert.Empty(path.Levels);
    }

    [Fact]
    public void ReportsAnEmptyRawForBlankInput()
    {
        Assert.Equal(string.Empty, Canonicalizer().Canonicalize("   ").Raw);
    }

    [Fact]
    public void FallsBackToOtherForUnmappedTopLevels()
    {
        var path = Canonicalizer().Canonicalize("HOVERBOARD SUBSYSTEM:THRUSTER");

        Assert.Equal("OTHER", path.Group);
        Assert.Equal(new[] { "HOVERBOARD SUBSYSTEM", "THRUSTER" }, path.Levels);
    }

    [Fact]
    public void CountsEveryUnmappedTopLevelOccurrence()
    {
        var canonicalizer = Canonicalizer();

        canonicalizer.Canonicalize("HOVERBOARD SUBSYSTEM:THRUSTER");
        canonicalizer.Canonicalize("HOVERBOARD SUBSYSTEM:COILS");
        canonicalizer.Canonicalize("TELEPORTER");
        canonicalizer.Canonicalize("POWER TRAIN:AUTOMATIC TRANSMISSION");

        Assert.Equal(2, canonicalizer.UnmappedTopLevels.Count);
        Assert.Equal(2, canonicalizer.UnmappedTopLevels["HOVERBOARD SUBSYSTEM"]);
        Assert.Equal(1, canonicalizer.UnmappedTopLevels["TELEPORTER"]);
        Assert.DoesNotContain("POWER TRAIN", canonicalizer.UnmappedTopLevels.Keys);
    }

    [Fact]
    public void KeepsUnmappedBookkeepingEmptyWhenEverythingResolves()
    {
        var canonicalizer = Canonicalizer();

        canonicalizer.Canonicalize("ENGINE:ENGINE BLOCK");
        canonicalizer.Canonicalize("");

        Assert.Empty(canonicalizer.UnmappedTopLevels);
    }

    [Fact]
    public void LoadsTheRepositoryGroupMapAndAppliesTheRequiredMerges()
    {
        var path = TestPaths.DataFile("component-groups.json");
        Assert.True(File.Exists(path), $"Missing component group map at '{path}'.");

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Assert.NotNull(raw);

        var canonicalizer = ComponentCanonicalizer.Load(path);

        Assert.Equal("ENGINE", canonicalizer.Canonicalize("ENGINE").Group);
        Assert.Equal("ENGINE", canonicalizer.Canonicalize("ENGINE AND ENGINE COOLING").Group);
        Assert.Equal("BRAKES", canonicalizer.Canonicalize("SERVICE BRAKES").Group);
        Assert.Equal("BRAKES", canonicalizer.Canonicalize("SERVICE BRAKES, HYDRAULIC").Group);
        Assert.Equal("BRAKES", canonicalizer.Canonicalize("SERVICE BRAKES, AIR").Group);
        Assert.Equal("FUEL SYSTEM", canonicalizer.Canonicalize("FUEL SYSTEM, GASOLINE").Group);
        Assert.Equal("FUEL SYSTEM", canonicalizer.Canonicalize("FUEL SYSTEM, DIESEL").Group);
        Assert.Equal("FUEL SYSTEM", canonicalizer.Canonicalize("FUEL/PROPULSION SYSTEM").Group);
        Assert.Equal("POWER TRAIN", canonicalizer.Canonicalize("POWER TRAIN").Group);
        Assert.Equal("POWER TRAIN", canonicalizer.Canonicalize("POWER TRAIN:AUTOMATIC TRANSMISSION").Group);
        Assert.Empty(canonicalizer.UnmappedTopLevels);
    }

    [Fact]
    public void CoversEveryTopLevelCategoryMeasuredInTheComplaintFile()
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(TestPaths.DataFile("component-groups.json")));
        Assert.NotNull(raw);

        Assert.Equal(54, raw.Count);
        Assert.InRange(raw.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count(), 20, 30);
    }
}
