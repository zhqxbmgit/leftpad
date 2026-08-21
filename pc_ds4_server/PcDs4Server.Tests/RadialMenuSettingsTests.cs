using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialMenuSettingsTests
{
    [Fact]
    public void Default_MatchesTheAcceptedV1Visual()
    {
        RadialMenuSettings settings = RadialMenuSettings.Default;

        Assert.Equal(RadialVisualPackContract.DefaultVisualPackId, settings.VisualPackId);
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
        Assert.Equal(RadialSlotMappings.SlotCount, settings.SlotMappings.Count);
        Assert.All(settings.SlotMappings,
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
        Assert.True(settings.TryValidate(out _));
    }

    [Fact]
    public void LegacyJsonWithoutVisualPackId_UsesRadialV5AndPreservesOtherFields()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "scalePercent": 90,
              "doubleTapWindowMs": 275,
              "selectionDeadZone": 36,
              "slotMappings": [
                { "kind": "keyboardKey", "key": "F1" }
              ]
            }
            """);

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(RadialVisualPackContract.DefaultVisualPackId, result.Settings.VisualPackId);
        Assert.Equal(90, result.Settings.ScalePercent);
        Assert.Equal(275, result.Settings.DoubleTapWindowMs);
        Assert.Equal(36, result.Settings.SelectionDeadZone);
        Assert.Equal(KeyboardKey.F1, result.Settings.SlotMappings[0].Key);
    }

    [Fact]
    public void JsonStore_RoundTripsUnknownVisualPackIdWithoutChangingOtherSettings()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings expected = RadialMenuSettings.Default with
        {
            VisualPackId = "future-theme",
            ScalePercent = 120,
            SelectionDeadZone = 40
        };

        Assert.True(store.TrySave(expected, out string saveError), saveError);
        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("future-theme", result.Settings.VisualPackId);
        Assert.Equal(120, result.Settings.ScalePercent);
        Assert.Equal(40, result.Settings.SelectionDeadZone);
        Assert.Contains("\"visualPackId\": \"future-theme\"", File.ReadAllText(temporary.FilePath));
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
            SelectionPollIntervalMs = 24,
            SlotMappings = RadialSlotMappings.Create(new RadialSlotMapping[]
            {
                RadialSlotMapping.None,
                new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F1 },
                new()
                {
                    Kind = RadialActionKind.KeyboardShortcut,
                    Key = KeyboardKey.K,
                    Ctrl = true,
                    Shift = true
                },
                new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "cross" }
            })
        };

        Assert.True(store.TrySave(expected, out string saveError), saveError);
        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(expected, result.Settings);
        string json = File.ReadAllText(temporary.FilePath);
        Assert.Contains("\"kind\": \"keyboardKey\"", json);
        Assert.Contains("\"key\": \"F1\"", json);
        Assert.Contains("\"kind\": \"keyboardShortcut\"", json);
        Assert.Contains("\"ds4Button\": \"cross\"", json);
    }

    [Fact]
    public void JsonStore_RoundTripsDPadDownMapping()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings expected = RadialMenuSettings.Default with
        {
            SlotMappings = RadialMenuSettings.Default.SlotMappings.WithSlot(4, new RadialSlotMapping
            {
                Kind = RadialActionKind.Ds4Button,
                Ds4Button = "dpad_down"
            })
        };

        Assert.True(store.TrySave(expected, out string saveError), saveError);
        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(expected, result.Settings);
        Assert.Contains("\"ds4Button\": \"dpad_down\"", File.ReadAllText(temporary.FilePath));
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
        Assert.All(result.Settings.SlotMappings,
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
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
        Assert.All(result.Settings.SlotMappings,
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
    }

    [Fact]
    public void LegacyJsonWithoutMappings_PreservesEveryExistingSetting()
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
              "doubleTapWindowMs": 275,
              "selectionDeadZone": 36,
              "highlightAlpha": 120,
              "selectionPollIntervalMs": 24
            }
            """);

        RadialMenuSettingsLoadResult result = new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(90, result.Settings.ScalePercent);
        Assert.Equal(320, result.Settings.BaseCanvasSize);
        Assert.Equal(34, result.Settings.HubRadius);
        Assert.Equal(45, result.Settings.PetalInnerRadius);
        Assert.Equal(110, result.Settings.PetalOuterRadius);
        Assert.Equal(75, result.Settings.TextRadius);
        Assert.Equal(5.5f, result.Settings.PetalGapDegrees);
        Assert.Equal(16f, result.Settings.FontSize);
        Assert.Equal(190, result.Settings.FillAlpha);
        Assert.Equal(90, result.Settings.BorderAlpha);
        Assert.Equal(230, result.Settings.TextAlpha);
        Assert.Equal(275, result.Settings.DoubleTapWindowMs);
        Assert.Equal(36, result.Settings.SelectionDeadZone);
        Assert.Equal(120, result.Settings.HighlightAlpha);
        Assert.Equal(24, result.Settings.SelectionPollIntervalMs);
        Assert.All(result.Settings.SlotMappings,
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
    }

    [Fact]
    public void PartialMappings_PreserveSettingsAndFillMissingSlotsWithNone()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "scalePercent": 90,
              "slotMappings": [
                { "kind": "keyboardKey", "key": "F1" },
                { "kind": "keyboardShortcut", "key": "K", "ctrl": true },
                { "kind": "ds4Button", "ds4Button": "cross" }
              ]
            }
            """);

        RadialMenuSettingsLoadResult result = new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(90, result.Settings.ScalePercent);
        Assert.Equal(RadialActionKind.KeyboardKey, result.Settings.SlotMappings[0].Kind);
        Assert.Equal(RadialActionKind.KeyboardShortcut, result.Settings.SlotMappings[1].Kind);
        Assert.Equal(RadialActionKind.Ds4Button, result.Settings.SlotMappings[2].Kind);
        Assert.All(result.Settings.SlotMappings.Skip(3),
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
    }

    [Fact]
    public void InvalidOneSlot_OnlySanitizesThatSlotAndPreservesVisualSettings()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "fillAlpha": 177,
              "selectionDeadZone": 44,
              "slotMappings": [
                { "kind": "keyboardKey", "key": "F1" },
                { "kind": "keyboardShortcut", "key": "K", "ctrl": true },
                { "kind": "none" },
                { "kind": "ds4Button", "ds4Button": "invalid" },
                { "kind": "ds4Button", "ds4Button": "CROSS" },
                { "kind": "keyboardKey", "key": "Escape" }
              ]
            }
            """);

        RadialMenuSettingsLoadResult result = new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(177, result.Settings.FillAlpha);
        Assert.Equal(44, result.Settings.SelectionDeadZone);
        Assert.Equal(KeyboardKey.F1, result.Settings.SlotMappings[0].Key);
        Assert.Equal(RadialActionKind.KeyboardShortcut, result.Settings.SlotMappings[1].Kind);
        Assert.Equal(RadialActionKind.None, result.Settings.SlotMappings[3].Kind);
        Assert.Equal("cross", result.Settings.SlotMappings[4].Ds4Button);
        Assert.Equal(KeyboardKey.Escape, result.Settings.SlotMappings[5].Key);
    }

    [Fact]
    public void LegacyExcludedRadialButtons_ClearOnlyTheirSlots()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "fillAlpha": 177,
              "selectionDeadZone": 44,
              "slotMappings": [
                { "kind": "ds4Button", "ds4Button": "r1" },
                { "kind": "ds4Button", "ds4Button": "cross" },
                { "kind": "ds4Button", "ds4Button": "l2" },
                { "kind": "ds4Button", "ds4Button": "dpad_down" },
                { "kind": "ds4Button", "ds4Button": "r2" },
                { "kind": "keyboardKey", "key": "F1" }
              ]
            }
            """);

        RadialMenuSettingsLoadResult result = new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(177, result.Settings.FillAlpha);
        Assert.Equal(44, result.Settings.SelectionDeadZone);
        Assert.Equal(RadialActionKind.None, result.Settings.SlotMappings[0].Kind);
        Assert.Equal("cross", result.Settings.SlotMappings[1].Ds4Button);
        Assert.Equal(RadialActionKind.None, result.Settings.SlotMappings[2].Kind);
        Assert.Equal("dpad_down", result.Settings.SlotMappings[3].Ds4Button);
        Assert.Equal(RadialActionKind.None, result.Settings.SlotMappings[4].Kind);
        Assert.Equal(KeyboardKey.F1, result.Settings.SlotMappings[5].Key);
    }

    [Fact]
    public void UnknownKindAndExtraMappings_AreSafelyNormalizedToSixSlots()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "slotMappings": [
                { "kind": "futureAction", "key": "F1" },
                { "kind": "none" }, { "kind": "none" },
                { "kind": "none" }, { "kind": "none" },
                { "kind": "none" },
                { "kind": "keyboardKey", "key": "F2" }
              ]
            }
            """);

        RadialMenuSettingsLoadResult result = new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(RadialSlotMappings.SlotCount, result.Settings.SlotMappings.Count);
        Assert.All(result.Settings.SlotMappings,
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
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
