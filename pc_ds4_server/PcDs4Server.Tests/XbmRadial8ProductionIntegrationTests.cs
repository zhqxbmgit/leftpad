using System.Security.Cryptography;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class XbmRadial8ProductionIntegrationTests
{
    private const string ProductionPackId = "xbm-radial8-v1";
    private const string ProductionPackName = "XBM Cyan Radial 8";
    private const string ExpectedBaseSha =
        "B9A335596A6786CF22DC55DD5D3CA0BD3C80FD7547FC47966AE90101011192C9";
    private const string ExpectedSelectedSha =
        "DAFC62D2679AF1A9DD6EF80B4E119168A7EDB2E7E2DB04EE022EEBA77A85F8AB";
    private const string ExpectedLayoutSha =
        "9F5D7E26BB0BEB195376EED1385A8297D215CD2F702628A842C6ED50B7E938FC";

    [Fact]
    public void ProductionPack_ExistsLoadsAndMatchesTheFrozenAuthority()
    {
        Assert.True(Directory.Exists(ProductionPackDirectory));
        Assert.Equal(
            new[]
            {
                "manifest.json",
                "radial-base.png",
                "radial-layout.json",
                "radial-selected-card.png"
            },
            Directory.EnumerateFiles(ProductionPackDirectory)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal));

        RadialVisualPackDefinition definition =
            RadialVisualPackDefinition.Load(ProductionPackDirectory);

        Assert.Equal(ProductionPackId, definition.Manifest.Id);
        Assert.Equal(ProductionPackName, definition.Manifest.Name);
        Assert.Equal(1, definition.Manifest.Version);
        Assert.Equal(LayoutProfileRegistry.Radial8ProfileId, definition.Manifest.LayoutProfile);
        Assert.Equal(8, definition.Manifest.SlotCount);
        Assert.Equal("canonical-transform", definition.Manifest.SelectionAssetMode);
        Assert.Equal(LayoutProfileRegistry.Radial8ProfileId, definition.LayoutDefinition.ProfileId);
        Assert.Equal(8, definition.LayoutDefinition.SlotCount);
        Assert.Equal(
            new[] { 0d, 45d, 90d, 135d, 180d, 225d, 270d, 315d },
            definition.LayoutDefinition.Slots.Select(slot => slot.AngleDegrees));
        Assert.Equal(ExpectedBaseSha, Sha256(definition.BasePath));
        Assert.Equal(ExpectedSelectedSha, Sha256(definition.SelectedPath));
        Assert.Equal(ExpectedLayoutSha, CanonicalAssetHash.JsonSha256(definition.LayoutPath));
    }

    [Fact]
    public void ProductionCatalog_ResolvesXbmWithoutIssues()
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();

        Assert.Empty(snapshot.Issues);
        RadialVisualPackCatalogEntry? xbm = snapshot.Find(ProductionPackId);
        Assert.NotNull(xbm);
        Assert.Equal(ProductionPackName, xbm!.Name);
        Assert.Equal(1, xbm.Version);
    }

    [Fact]
    public void DefaultPolicy_RemainsRadial8Minimal()
    {
        Assert.Equal("radial-8-minimal-v1", RadialVisualPackContract.DefaultVisualPackId);
        Assert.Equal("radial-8", RadialVisualPackContract.DefaultMappingProfileId);
        Assert.Equal("radial-8-minimal-v1", RadialMenuSettings.Default.VisualPackId);
        Assert.Equal("radial-8", RadialMenuSettings.Default.MappingProfileId);
        Assert.NotEqual(ProductionPackId, RadialMenuSettings.Default.VisualPackId);
    }

    private static string ProductionPackDirectory => Path.Combine(
        RadialVisualPackContract.DiscoveryRoot,
        ProductionPackId);

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
