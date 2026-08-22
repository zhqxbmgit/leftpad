using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialLayoutDefinitionTests
{
    private static readonly double[] Radial6Angles = { 0d, 60d, 120d, 180d, 240d, 300d };
    private static readonly double[] Radial8Angles = { 0d, 45d, 90d, 135d, 180d, 225d, 270d, 315d };

    [Fact]
    public void Radial6_CreatesExpectedLayoutDefinition()
    {
        RadialVisualPackDefinition pack = RadialVisualPackDefinition.Load(
            RadialVisualPackDefinition.DefaultDirectory);

        LayoutDefinition definition = pack.LayoutDefinition;
        Assert.Equal("radial-6", definition.ProfileId);
        Assert.Equal("radial", definition.Family);
        Assert.Equal(6, definition.SlotCount);
        Assert.Equal("AngleSelection", definition.SelectionModel);
        Assert.Equal("canonical-transform", definition.SelectionAssetMode);
        Assert.Equal(new LayoutCanvasDefinition(1254, 1254, "RGBA"), definition.Canvas);
        Assert.Equal(new LayoutPointDefinition(627d, 627d), definition.WheelCenter);
        Assert.Equal(Radial6Angles, definition.Slots.Select(slot => slot.AngleDegrees));
        Assert.Equal(Enumerable.Range(1, 6), definition.Slots.Select(slot => slot.Id));
        Assert.Equal(
            new LayoutPointDefinition(
                pack.Layout.Slots[0].GlyphAnchor.X,
                pack.Layout.Slots[0].GlyphAnchor.Y),
            definition.Slots[0].GlyphAnchor);
        Assert.Equal(
            new LayoutPointDefinition(
                pack.Layout.Slots[0].LabelAnchor.X,
                pack.Layout.Slots[0].LabelAnchor.Y),
            definition.Slots[0].LabelAnchor);
    }

    [Fact]
    public void Radial8_ParseCreatesValidatedDefinitionButRuntimeLoadRemainsRejected()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string directory = temporary.AddRadial8Pack();

        RadialVisualPackDefinition pack = RadialVisualPackDefinition.Parse(directory);
        LayoutDefinition definition = pack.LayoutDefinition;

        Assert.Equal("radial-8", definition.ProfileId);
        Assert.Equal("radial", definition.Family);
        Assert.Equal(8, definition.SlotCount);
        Assert.Equal("AngleSelection", definition.SelectionModel);
        Assert.Equal("canonical-transform", definition.SelectionAssetMode);
        Assert.Equal(Radial8Angles, definition.Slots.Select(slot => slot.AngleDegrees));
        Assert.Equal(Enumerable.Range(1, 8), definition.Slots.Select(slot => slot.Id));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RadialVisualPackDefinition.Load(directory));
        Assert.Contains("not Runtime integrated", exception.Message);
    }

    [Fact]
    public void Radial8_ParseOnlyPackIsNotExposedByRuntimeCatalog()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        _ = temporary.AddRadial8Pack();

        RadialVisualPackCatalogSnapshot snapshot =
            new RadialVisualPackCatalog(temporary.Root).Discover();

        Assert.Empty(snapshot.Packs);
        RadialVisualPackCatalogIssue issue = Assert.Single(snapshot.Issues);
        Assert.Equal("radial-8-test", issue.PackId);
        Assert.Contains("not Runtime integrated", issue.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownProfile()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string directory = temporary.AddPack(
            "unknown",
            "unknown-profile-pack",
            "Unknown profile",
            manifest => manifest["layoutProfile"] = "radial-7");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RadialVisualPackDefinition.Parse(directory));
        Assert.Contains("Unsupported layout profile: radial-7", exception.Message);
    }

    [Fact]
    public void Parse_RejectsManifestSlotCountThatDoesNotMatchProfile()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string directory = temporary.AddRadial8Pack(
            editManifest: manifest => manifest["slotCount"] = 7);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RadialVisualPackDefinition.Parse(directory));
        Assert.Contains("must contain 8 slots", exception.Message);
    }

    [Fact]
    public void Parse_RejectsLayoutSlotCountThatDoesNotMatchProfile()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string directory = temporary.AddRadial8Pack(
            editLayout: layout => layout["slotCount"] = 7);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RadialVisualPackDefinition.Parse(directory));
        Assert.Contains("must contain 8 slots", exception.Message);
    }

    [Fact]
    public void Parse_RejectsAnglesThatDoNotMatchRegisteredProfile()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string directory = temporary.AddRadial8Pack(
            editLayout: layout =>
            {
                layout["slotAnglesDegrees"]![1] = 46d;
                layout["slots"]![1]!["angleDegreesClockwiseFromTop"] = 46d;
            });

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RadialVisualPackDefinition.Parse(directory));
        Assert.Contains("Invalid transform mapping for slot 2", exception.Message);
    }

    [Fact]
    public void Registry_RegistersRadial6ForRuntimeAndRadial8ForParseOnly()
    {
        LayoutProfileRegistration radial6 = LayoutProfileRegistry.GetRequired("radial-6");
        LayoutProfileRegistration radial8 = LayoutProfileRegistry.GetRequired("radial-8");

        Assert.Equal(6, radial6.SlotCount);
        Assert.True(radial6.RuntimeSessionSupported);
        Assert.Equal(Radial6Angles, radial6.ExpectedAngles);

        Assert.Equal(8, radial8.SlotCount);
        Assert.False(radial8.RuntimeSessionSupported);
        Assert.Equal(Radial8Angles, radial8.ExpectedAngles);
    }

}
