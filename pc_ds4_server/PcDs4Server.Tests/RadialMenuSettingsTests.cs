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
        Assert.Equal(150, settings.DoubleTapWindowMs);
        Assert.Equal(28, settings.SelectionDeadZone);
        Assert.Equal(80, settings.HighlightAlpha);
        Assert.Equal(16, settings.SelectionPollIntervalMs);
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
            TextAlpha = 220,
            DoubleTapWindowMs = 275,
            SelectionDeadZone = 36,
            HighlightAlpha = 120,
            SelectionPollIntervalMs = 24
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
    public void LegacyJsonWithoutDoubleTapWindow_PreservesVisualSettingsAndUsesDefaultWindow()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "scalePercent": 90,
              "baseCanvasSize": 320,
              "hubRadius": 34,
              "petalInnerRadius": 45,
              "petalOuterRadius": 110,
              "textRadius": 75,
              "petalGapDegrees": 5.5,
              "fontSize": 16,
              "fillAlpha": 190,
              "borderAlpha": 90,
              "textAlpha": 230
            }
            """);
        var store = new RadialMenuSettingsStore(temporary.FilePath);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(90, result.Settings.ScalePercent);
        Assert.Equal(320, result.Settings.BaseCanvasSize);
        Assert.Equal(34, result.Settings.HubRadius);
        Assert.Equal(190, result.Settings.FillAlpha);
        Assert.Equal(150, result.Settings.DoubleTapWindowMs);
    }

    [Fact]
    public void LegacyJsonWithoutSelectionFields_PreservesExistingSettingsAndAddsSelectionDefaults()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "scalePercent": 90,
              "baseCanvasSize": 320,
              "hubRadius": 34,
              "petalInnerRadius": 45,
              "petalOuterRadius": 110,
              "textRadius": 75,
              "petalGapDegrees": 5.5,
              "fontSize": 16,
              "fillAlpha": 190,
              "borderAlpha": 90,
              "textAlpha": 230,
              "doubleTapWindowMs": 275
            }
            """);
        var store = new RadialMenuSettingsStore(temporary.FilePath);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(90, result.Settings.ScalePercent);
        Assert.Equal(190, result.Settings.FillAlpha);
        Assert.Equal(275, result.Settings.DoubleTapWindowMs);
        Assert.Equal(28, result.Settings.SelectionDeadZone);
        Assert.Equal(80, result.Settings.HighlightAlpha);
        Assert.Equal(16, result.Settings.SelectionPollIntervalMs);
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

    [Theory]
    [InlineData(80)]
    [InlineData(500)]
    public void DoubleTapWindowBoundaries_AreValid(int milliseconds)
    {
        Assert.True((RadialMenuSettings.Default with { DoubleTapWindowMs = milliseconds }).TryValidate(out _));
    }

    [Theory]
    [InlineData(79)]
    [InlineData(501)]
    public void DoubleTapWindowOutsideBoundaries_IsRejected(int milliseconds)
    {
        Assert.False((RadialMenuSettings.Default with { DoubleTapWindowMs = milliseconds }).TryValidate(out _));
    }

    [Fact]
    public void SelectionSettingBoundaries_AreValid()
    {
        Assert.True((RadialMenuSettings.Default with { SelectionDeadZone = 8 }).TryValidate(out _));
        Assert.True((RadialMenuSettings.Default with { SelectionDeadZone = 80 }).TryValidate(out _));
        Assert.True((RadialMenuSettings.Default with { HighlightAlpha = 0 }).TryValidate(out _));
        Assert.True((RadialMenuSettings.Default with { HighlightAlpha = 255 }).TryValidate(out _));
        Assert.True((RadialMenuSettings.Default with { SelectionPollIntervalMs = 8 }).TryValidate(out _));
        Assert.True((RadialMenuSettings.Default with { SelectionPollIntervalMs = 50 }).TryValidate(out _));
    }

    [Fact]
    public void SelectionSettingsOutsideBoundaries_AreRejected()
    {
        Assert.False((RadialMenuSettings.Default with { SelectionDeadZone = 7 }).TryValidate(out _));
        Assert.False((RadialMenuSettings.Default with { SelectionDeadZone = 81 }).TryValidate(out _));
        Assert.False((RadialMenuSettings.Default with { HighlightAlpha = -1 }).TryValidate(out _));
        Assert.False((RadialMenuSettings.Default with { HighlightAlpha = 256 }).TryValidate(out _));
        Assert.False((RadialMenuSettings.Default with { SelectionPollIntervalMs = 7 }).TryValidate(out _));
        Assert.False((RadialMenuSettings.Default with { SelectionPollIntervalMs = 51 }).TryValidate(out _));
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
