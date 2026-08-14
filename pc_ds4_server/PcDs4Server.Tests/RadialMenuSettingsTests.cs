using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialMenuSettingsTests
{
    [Fact]
    public void Default_MatchesTheAcceptedV1Visual()
    {
        RadialMenuSettings settings = RadialMenuSettings.Default;

        Assert.Equal(100, settings.ScalePercent);
        Assert.Equal(280, settings.BaseCanvasSize);
        Assert.Equal(35, settings.HubRadius);
        Assert.Equal(42, settings.PetalInnerRadius);
        Assert.Equal(103, settings.PetalOuterRadius);
        Assert.Equal(73, settings.TextRadius);
        Assert.Equal(4f, settings.PetalGapDegrees);
        Assert.Equal(15f, settings.FontSize);
        Assert.Equal(218, settings.FillAlpha);
        Assert.Equal(100, settings.BorderAlpha);
        Assert.Equal(240, settings.TextAlpha);
        Assert.True(settings.TryValidate(out _));
    }

    [Fact]
    public void JsonStore_RoundTripsEquivalentSettings()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings expected = RadialMenuSettings.Default with
        {
            ScalePercent = 90,
            PetalGapDegrees = 5.5f,
            FillAlpha = 180,
            BorderAlpha = 80,
            TextAlpha = 220
        };

        Assert.True(store.TrySave(expected, out string saveError), saveError);
        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(expected, result.Settings);
    }

    [Fact]
    public void MissingFile_LoadsDefaults()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Missing, result.Status);
        Assert.Equal(RadialMenuSettings.Default, result.Settings);
    }

    [Fact]
    public void MalformedJson_LoadsDefaultsWithoutThrowing()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, "{not-json");
        var store = new RadialMenuSettingsStore(temporary.FilePath);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Malformed, result.Status);
        Assert.Equal(RadialMenuSettings.Default, result.Settings);
    }

    [Fact]
    public void InvalidStoredGeometry_LoadsDefaults()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, "{\"hubRadius\":50,\"petalInnerRadius\":42}");
        var store = new RadialMenuSettingsStore(temporary.FilePath);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Invalid, result.Status);
        Assert.Equal(RadialMenuSettings.Default, result.Settings);
    }

    [Fact]
    public void InvalidGeometry_IsRejected()
    {
        Assert.False((RadialMenuSettings.Default with { HubRadius = 42 }).TryValidate(out _));
        Assert.False((RadialMenuSettings.Default with { PetalInnerRadius = 103 }).TryValidate(out _));
        Assert.False((RadialMenuSettings.Default with { PetalOuterRadius = 140 }).TryValidate(out _));
    }

    [Fact]
    public void ScaleBoundaries_AreSafeAndOutOfRangeValuesAreRejected()
    {
        RadialMenuSettings minimum = RadialMenuSettings.Default with { ScalePercent = 60 };
        RadialMenuSettings maximum = RadialMenuSettings.Default with { ScalePercent = 140 };

        Assert.True(minimum.TryValidate(out _));
        Assert.True(maximum.TryValidate(out _));
        Assert.Equal(168, minimum.CreateRenderMetrics().CanvasSize);
        Assert.Equal(392, maximum.CreateRenderMetrics().CanvasSize);
        Assert.False((RadialMenuSettings.Default with { ScalePercent = 59 }).TryValidate(out _));
        Assert.False((RadialMenuSettings.Default with { ScalePercent = 141 }).TryValidate(out _));
    }

    private sealed class TemporarySettingsPath : IDisposable
    {
        public TemporarySettingsPath()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "LeftPad.Tests", Guid.NewGuid().ToString("N"));
            FilePath = Path.Combine(DirectoryPath, "radial-menu-settings.json");
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
