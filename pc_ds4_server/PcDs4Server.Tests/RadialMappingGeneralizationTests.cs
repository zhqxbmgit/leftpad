using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialMappingGeneralizationTests
{
    [Fact]
    public void LegacySlotMappings_MigrateLosslesslyToRadial6Profile()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "slotMappings": [
                { "kind": "keyboardKey", "key": "F1" },
                { "kind": "keyboardShortcut", "key": "K", "ctrl": true },
                { "kind": "ds4Button", "ds4Button": "cross" },
                { "kind": "none" },
                { "kind": "keyboardKey", "key": "Escape" },
                { "kind": "ds4Button", "ds4Button": "dpad_down" }
              ]
            }
            """);

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.True(result.Settings.MappingsByProfile.ContainsKey(
            LayoutProfileRegistry.Radial6ProfileId));
        RadialSlotMappings migrated = result.Settings.GetProfileMappings(
            LayoutProfileRegistry.Radial6ProfileId);
        Assert.Equal(6, migrated.Count);
        Assert.Equal(KeyboardKey.F1, migrated[0].Key);
        Assert.Equal(KeyboardKey.K, migrated[1].Key);
        Assert.True(migrated[1].Ctrl);
        Assert.Equal("cross", migrated[2].Ds4Button);
        Assert.Equal(RadialActionKind.None, migrated[3].Kind);
        Assert.Equal(KeyboardKey.Escape, migrated[4].Key);
        Assert.Equal("dpad_down", migrated[5].Ds4Button);
        Assert.Null(result.Settings.LegacySlotMappings);
    }

    [Fact]
    public void Radial6Mappings_SaveAndLoadByProfile()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings expected = RadialMenuSettings.Default.SetProfileMappings(
            LayoutProfileRegistry.Radial6ProfileId,
            RadialSlotMappings.Create(6).WithSlot(6, Keyboard(KeyboardKey.F1)));

        Assert.True(store.TrySave(expected, out string error), error);
        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(KeyboardKey.F1, result.Settings.GetProfileMappings("radial-6")[5].Key);
        string json = File.ReadAllText(temporary.FilePath);
        Assert.Contains("\"mappingsByProfile\"", json);
        Assert.Contains("\"radial-6\"", json);
        Assert.DoesNotContain("\"slotMappings\"", json);
    }

    [Fact]
    public void Radial8Mappings_SaveAndLoadByProfile()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialSlotMappings radial8 = RadialSlotMappings.Create(8)
            .WithSlot(7, Keyboard(KeyboardKey.F2))
            .WithSlot(8, Ds4("cross"));
        RadialMenuSettings expected = RadialMenuSettings.Default.SetProfileMappings(
            LayoutProfileRegistry.Radial8ProfileId,
            radial8);

        Assert.True(store.TrySave(expected, out string error), error);
        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        RadialSlotMappings loaded = result.Settings.GetProfileMappings("radial-8");
        Assert.Equal(8, loaded.Count);
        Assert.Equal(KeyboardKey.F2, loaded[6].Key);
        Assert.Equal("cross", loaded[7].Ds4Button);
    }

    [Fact]
    public void ProfileMappings_AreIsolated()
    {
        RadialMenuSettings settings = RadialMenuSettings.Default
            .SetProfileMappings(
                "radial-6",
                RadialSlotMappings.Create(6).WithSlot(1, Keyboard(KeyboardKey.F1)))
            .SetProfileMappings(
                "radial-8",
                RadialSlotMappings.Create(8)
                    .WithSlot(1, Keyboard(KeyboardKey.Escape))
                    .WithSlot(8, Ds4("cross")));

        RadialSlotMappings radial6 = settings.GetProfileMappings("radial-6");
        RadialSlotMappings radial8 = settings.GetProfileMappings("radial-8");
        Assert.Equal(6, radial6.Count);
        Assert.Equal(8, radial8.Count);
        Assert.Equal(KeyboardKey.F1, radial6[0].Key);
        Assert.Equal(KeyboardKey.Escape, radial8[0].Key);
        Assert.Equal("cross", radial8[7].Ds4Button);
    }

    [Fact]
    public void ActionResolver_ResolvesRadial8Slot7AndSlot8()
    {
        RadialMenuSettings settings = RadialMenuSettings.Default.SetProfileMappings(
            "radial-8",
            RadialSlotMappings.Create(8)
                .WithSlot(7, Keyboard(KeyboardKey.F2))
                .WithSlot(8, Ds4("cross")));

        RadialSlotMapping slot7 = RadialActionResolver.GetMapping(settings, "radial-8", 7);
        RadialSlotMapping slot8 = RadialActionResolver.GetMapping(settings, "radial-8", 8);

        Assert.Equal(KeyboardKey.F2, slot7.Key);
        Assert.Equal("cross", slot8.Ds4Button);
    }

    [Fact]
    public void MissingAndUnknownProfiles_FallBackToNoneMappings()
    {
        RadialMenuSettings settings = RadialMenuSettings.Default;

        RadialSlotMappings missingKnown = settings.GetProfileMappings("radial-8");
        RadialSlotMappings unknown = settings.GetProfileMappings("future-radial");

        Assert.Equal(8, missingKnown.Count);
        Assert.All(missingKnown, mapping => Assert.Equal(RadialSlotMapping.None, mapping));
        Assert.Empty(unknown);
        Assert.Equal(
            RadialSlotMapping.None,
            RadialActionResolver.GetMapping(settings, "future-radial", 1));
    }

    [Fact]
    public void SettingsUiRowCountModel_UsesLayoutSlotCount()
    {
        LayoutDefinition radial6 = CreateLayout("radial-6");
        LayoutDefinition radial8 = CreateLayout("radial-8");

        Assert.Equal(6, RadialMenuSettingsForm.GetMappingRowCount(radial6));
        Assert.Equal(8, RadialMenuSettingsForm.GetMappingRowCount(radial8));
    }

    private static RadialSlotMapping Keyboard(KeyboardKey key) => new()
    {
        Kind = RadialActionKind.KeyboardKey,
        Key = key
    };

    private static RadialSlotMapping Ds4(string button) => new()
    {
        Kind = RadialActionKind.Ds4Button,
        Ds4Button = button
    };

    private static LayoutDefinition CreateLayout(string profileId)
    {
        LayoutProfileRegistration profile = LayoutProfileRegistry.GetRequired(profileId);
        return new LayoutDefinition(
            profile.ProfileId,
            profile.Family,
            profile.SlotCount,
            new LayoutCanvasDefinition(280, 280, "fixed"),
            new LayoutPointDefinition(140, 140),
            profile.SelectionModel,
            "rotateSelected",
            profile.ExpectedAngles.Select((angle, index) => new RadialSlotDefinition(
                index + 1,
                angle,
                new LayoutPointDefinition(140, 40),
                new LayoutPointDefinition(140, 60))));
    }

    private sealed class TemporarySettingsPath : IDisposable
    {
        public TemporarySettingsPath()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "LeftPad.Tests",
                Guid.NewGuid().ToString("N"));
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
