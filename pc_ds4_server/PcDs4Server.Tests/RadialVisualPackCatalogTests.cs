using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialVisualPackCatalogTests
{
    [Fact]
    public void ProductionCatalog_FindsRadialV5AndItsDisplayName()
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();

        RadialVisualPackCatalogEntry pack = Assert.IsType<RadialVisualPackCatalogEntry>(
            snapshot.Find(RadialVisualPackContract.FallbackVisualPackId));
        Assert.Equal(RadialVisualPackContract.FallbackVisualPackId, pack.Id);
        Assert.Equal("Tactical HUD V5", pack.Name);
        Assert.Same(pack, snapshot.ResolveSelection("missing-pack"));
    }

    [Fact]
    public void Discovery_IncludesCompatiblePacksAndSortsDefaultThenNameOrdinal()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        temporary.AddPack("z-default", "radial-v5", "Tactical HUD V5");
        temporary.AddPack("z-beta", "beta-pack", "beta");
        temporary.AddPack("a-alpha", "alpha-pack", "Alpha");

        RadialVisualPackCatalogSnapshot snapshot =
            new RadialVisualPackCatalog(temporary.Root).Discover();

        Assert.Equal(
            new[] { "radial-v5", "alpha-pack", "beta-pack" },
            snapshot.Packs.Select(pack => pack.Id));
        Assert.Equal("Alpha", snapshot.Find("alpha-pack")!.Name);
        Assert.Empty(snapshot.Issues);
    }

    [Theory]
    [InlineData("layoutProfile", "grid-3x2", "Unsupported layout profile")]
    [InlineData("slotCount", 5, "must contain 6 slots")]
    [InlineData("version", 2, "Unsupported visual pack version")]
    [InlineData("selectionAssetMode", "per-slot", "Unsupported selection asset mode")]
    public void Discovery_RejectsIncompatibleManifestValues(
        string property,
        object value,
        string expectedReason)
    {
        using var temporary = new RadialVisualPackTestDirectory();
        temporary.AddPack(
            "invalid",
            "invalid-pack",
            "Invalid",
            manifest => manifest[property] = System.Text.Json.Nodes.JsonValue.Create(value));

        RadialVisualPackCatalogSnapshot snapshot =
            new RadialVisualPackCatalog(temporary.Root).Discover();

        Assert.Empty(snapshot.Packs);
        Assert.Contains(snapshot.Issues, issue =>
            issue.PackId == "invalid-pack" && issue.Message.Contains(expectedReason));
    }

    [Fact]
    public void Discovery_RejectsEveryPackWithADuplicateId()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        temporary.AddPack("first", "duplicate", "First");
        temporary.AddPack("second", "duplicate", "Second");

        RadialVisualPackCatalogSnapshot snapshot =
            new RadialVisualPackCatalog(temporary.Root).Discover();

        Assert.Null(snapshot.Find("duplicate"));
        Assert.Contains(snapshot.Issues, issue =>
            issue.PackId == "duplicate" && issue.Message.Contains("Duplicate visual pack id"));
    }

    [Fact]
    public void Discovery_BrokenCandidateDoesNotHideValidCandidate()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        temporary.AddMalformedManifest("broken-json");
        string missingAsset = temporary.AddPack("missing-asset", "missing", "Missing");
        File.Delete(Path.Combine(missingAsset, "radial-base-v5.png"));
        temporary.AddPack("valid", "valid", "Valid");

        RadialVisualPackCatalogSnapshot snapshot =
            new RadialVisualPackCatalog(temporary.Root).Discover();

        Assert.Equal("valid", Assert.Single(snapshot.Packs).Id);
        Assert.Equal(2, snapshot.Issues.Count);
    }

    [Fact]
    public void Discovery_MissingRootReturnsIssueInsteadOfThrowing()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "LeftPad.Tests",
            Guid.NewGuid().ToString("N"));

        RadialVisualPackCatalogSnapshot snapshot =
            new RadialVisualPackCatalog(missing).Discover();

        Assert.Empty(snapshot.Packs);
        Assert.Single(snapshot.Issues);
    }
}
