using System.Text;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class DefaultThemePolicyTests
{
    [Fact]
    public void Authorities_AreIndependentAndFrozen()
    {
        Assert.Equal("radial-8-minimal-v1", RadialVisualPackContract.DefaultVisualPackId);
        Assert.Equal("radial-v5", RadialVisualPackContract.FallbackVisualPackId);
        Assert.Equal("radial-v5", RadialVisualPackContract.LegacyV1DefaultPackId);
        Assert.Equal("radial-8", RadialVisualPackContract.DefaultMappingProfileId);
        Assert.Equal("radial-6", RadialVisualPackContract.FallbackMappingProfileId);
        Assert.NotEqual(
            RadialVisualPackContract.DefaultVisualPackId,
            RadialVisualPackContract.FallbackVisualPackId);
    }

    [Fact]
    public void DefaultAndSafeFallback_AreCoherentIndependentPairs()
    {
        Assert.Equal("radial-8-minimal-v1", RadialMenuSettings.Default.VisualPackId);
        Assert.Equal("radial-8", RadialMenuSettings.Default.MappingProfileId);
        Assert.Equal("radial-v5", RadialMenuSettings.SafeFallback.VisualPackId);
        Assert.Equal("radial-6", RadialMenuSettings.SafeFallback.MappingProfileId);
    }

    [Fact]
    public void LegacyDefaultDirectory_RemainsTheRadialV5V1Directory()
    {
        Assert.Equal(
            Path.Combine(RadialVisualPackContract.DiscoveryRoot, "radial-v5"),
            RadialVisualPackDefinition.DefaultDirectory);
        Assert.DoesNotContain(
            RadialVisualPackContract.DefaultVisualPackId,
            RadialVisualPackDefinition.DefaultDirectory,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFile_IsTrueFirstRunAndDoesNotCreateAFile()
    {
        using var temporary = new TemporarySettingsPath(createDirectory: false);
        var store = new RadialMenuSettingsStore(temporary.FilePath);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Missing, result.Status);
        Assert.Equal(RadialMenuSettings.Default, result.Settings);
        Assert.Equal("radial-8-minimal-v1", result.Settings.VisualPackId);
        Assert.Equal("radial-8", result.Settings.MappingProfileId);
        Assert.False(File.Exists(temporary.FilePath));
        Assert.False(Directory.Exists(temporary.DirectoryPath));
    }

    [Fact]
    public void LegacyJsonWithoutVisualPackId_UsesLegacyPairAndPreservesBothProfiles()
    {
        using var temporary = new TemporarySettingsPath();
        File.WriteAllText(temporary.FilePath, """
            {
              "scalePercent": 91,
              "mappingProfileId": "radial-8",
              "mappingsByProfile": {
                "radial-6": [
                  { "kind": "keyboardKey", "key": "F1" }
                ],
                "radial-8": [
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "keyboardKey", "key": "F7" },
                  { "kind": "keyboardKey", "key": "F8" }
                ]
              }
            }
            """);

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("radial-v5", result.Settings.VisualPackId);
        Assert.Equal("radial-6", result.Settings.MappingProfileId);
        Assert.Equal(91, result.Settings.ScalePercent);
        Assert.Equal(KeyboardKey.F1, result.Settings.GetProfileMappings("radial-6")[0].Key);
        Assert.Equal(KeyboardKey.F7, result.Settings.GetProfileMappings("radial-8")[6].Key);
        Assert.Equal(KeyboardKey.F8, result.Settings.GetProfileMappings("radial-8")[7].Key);
    }

    [Theory]
    [InlineData("visualPackId")]
    [InlineData("VisualPackId")]
    [InlineData("VISUALPACKID")]
    public void ExplicitVisualPackIdDetection_MatchesSerializerCasing(string propertyName)
    {
        using var temporary = new TemporarySettingsPath();
        File.WriteAllText(
            temporary.FilePath,
            $"{{\"{propertyName}\":\"radial-8-minimal-v1\",\"mappingProfileId\":\"radial-8\"}}");

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("radial-8-minimal-v1", result.Settings.VisualPackId);
        Assert.Equal("radial-8", result.Settings.MappingProfileId);
    }

    [Fact]
    public void MalformedJson_UsesSafeFallbackWithoutWriting()
    {
        using var temporary = new TemporarySettingsPath();
        File.WriteAllText(temporary.FilePath, "{not-json");
        byte[] before = File.ReadAllBytes(temporary.FilePath);

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Malformed, result.Status);
        Assert.Equal(RadialMenuSettings.SafeFallback, result.Settings);
        Assert.Equal(before, File.ReadAllBytes(temporary.FilePath));
    }

    [Fact]
    public void InvalidSettings_UseSafeFallbackWithoutWriting()
    {
        using var temporary = new TemporarySettingsPath();
        File.WriteAllText(temporary.FilePath, """
            {
              "visualPackId": "radial-8-minimal-v1",
              "mappingProfileId": "radial-8",
              "hubRadius": 50,
              "petalInnerRadius": 42
            }
            """);
        byte[] before = File.ReadAllBytes(temporary.FilePath);

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Invalid, result.Status);
        Assert.Equal(RadialMenuSettings.SafeFallback, result.Settings);
        Assert.Equal(before, File.ReadAllBytes(temporary.FilePath));
    }

    [Fact]
    public void LoadFailure_UsesSafeFallback()
    {
        var files = new ThrowingReadFileOperations();
        var store = new RadialMenuSettingsStore("settings.json", files);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Failed, result.Status);
        Assert.Equal(RadialMenuSettings.SafeFallback, result.Settings);
        Assert.Equal(0, files.WriteCount);
    }

    [Theory]
    [InlineData("radial-v5", "radial-6")]
    [InlineData("radial-8-minimal-v1", "radial-8")]
    public void ExplicitCurrentThemes_ArePreservedByTheStore(string themeId, string profileId)
    {
        using var temporary = new TemporarySettingsPath();
        File.WriteAllText(
            temporary.FilePath,
            $"{{\"visualPackId\":\"{themeId}\",\"mappingProfileId\":\"{profileId}\"}}");

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(themeId, result.Settings.VisualPackId);
        Assert.Equal(profileId, result.Settings.MappingProfileId);
    }

    [Fact]
    public void CurrentThemeProfileMismatch_IsRepairedInMemoryWithoutWritingOrLosingMappings()
    {
        using var temporary = new TemporarySettingsPath();
        File.WriteAllText(temporary.FilePath, """
            {
              "visualPackId": "radial-8-minimal-v1",
              "mappingProfileId": "radial-6",
              "mappingsByProfile": {
                "radial-6": [
                  { "kind": "keyboardKey", "key": "F1" }
                ],
                "radial-8": [
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "keyboardKey", "key": "F7" },
                  { "kind": "keyboardKey", "key": "F8" }
                ]
              }
            }
            """);
        byte[] before = File.ReadAllBytes(temporary.FilePath);
        RadialMenuSettings loaded = new RadialMenuSettingsStore(temporary.FilePath).Load().Settings;

        RadialSettingsCoherenceResult result = RadialSettingsCoherenceResolver.Resolve(
            loaded,
            new RadialVisualPackCatalog().Discover());

        Assert.True(result.Repaired);
        Assert.Equal("radial-8-minimal-v1", result.Settings.VisualPackId);
        Assert.Equal("radial-8", result.Settings.MappingProfileId);
        Assert.Equal(KeyboardKey.F1, result.Settings.GetProfileMappings("radial-6")[0].Key);
        Assert.Equal(KeyboardKey.F7, result.Settings.GetProfileMappings("radial-8")[6].Key);
        Assert.Equal(KeyboardKey.F8, result.Settings.GetProfileMappings("radial-8")[7].Key);
        Assert.Equal(before, File.ReadAllBytes(temporary.FilePath));
    }

    [Fact]
    public void RetiredDarkFantasyTheme_IsMigratedInMemoryWithoutWritingOrLosingMappings()
    {
        using var temporary = new TemporarySettingsPath();
        File.WriteAllText(temporary.FilePath, """
            {
              "visualPackId": "dark-fantasy-radial8-v1",
              "mappingProfileId": "radial-8",
              "mappingsByProfile": {
                "radial-6": [
                  { "kind": "keyboardKey", "key": "F1" }
                ],
                "radial-8": [
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "none" },
                  { "kind": "keyboardKey", "key": "F7" },
                  { "kind": "keyboardKey", "key": "F8" }
                ]
              }
            }
            """);
        byte[] before = File.ReadAllBytes(temporary.FilePath);

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(RadialVisualPackContract.DefaultVisualPackId, result.Settings.VisualPackId);
        Assert.Equal(RadialVisualPackContract.DefaultMappingProfileId, result.Settings.MappingProfileId);
        Assert.Equal(KeyboardKey.F1, result.Settings.GetProfileMappings("radial-6")[0].Key);
        Assert.Equal(KeyboardKey.F7, result.Settings.GetProfileMappings("radial-8")[6].Key);
        Assert.Equal(KeyboardKey.F8, result.Settings.GetProfileMappings("radial-8")[7].Key);
        Assert.Equal(before, File.ReadAllBytes(temporary.FilePath));
    }

    [Fact]
    public void UnknownExplicitTheme_IsPreservedAndNotCoherenceRepaired()
    {
        RadialMenuSettings requested = RadialMenuSettings.SafeFallback with
        {
            VisualPackId = "does-not-exist"
        };

        RadialSettingsCoherenceResult result = RadialSettingsCoherenceResolver.Resolve(
            requested,
            new RadialVisualPackCatalog().Discover());

        Assert.False(result.Repaired);
        Assert.Equal(requested, result.Settings);
        Assert.Equal("does-not-exist", result.Settings.VisualPackId);
    }

    [Fact]
    public void Catalog_UsesDefaultForNoSelectionAndFallbackForUnknownSelection()
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();

        Assert.Equal("radial-8-minimal-v1", snapshot.ResolveSelection(null)!.Id);
        Assert.Equal("radial-8-minimal-v1", snapshot.ResolveSelection(string.Empty)!.Id);
        Assert.Equal("radial-v5", snapshot.ResolveSelection("does-not-exist")!.Id);
        Assert.Null(snapshot.Find(RadialMenuSettingsStore.RetiredDarkFantasyRadial8VisualPackId));
        Assert.NotNull(snapshot.Find("radial-8-minimal-v1"));
        Assert.NotNull(snapshot.Find("radial-v5"));
        Assert.NotNull(snapshot.Find("xbm-radial8-v1"));
        Assert.Equal(3, snapshot.Packs.Count);
    }

    [Fact]
    public void UnknownStartup_UsesRadialV5WithoutWritingRequestedSettings()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings requested = RadialMenuSettings.SafeFallback with
        {
            VisualPackId = "does-not-exist"
        };
        Assert.True(store.TrySave(requested, out string saveError), saveError);
        byte[] before = File.ReadAllBytes(temporary.FilePath);
        RadialMenuSettings loaded = store.Load().Settings;
        using var runtime = new RadialVisualPackRuntime(new RadialVisualPackCatalog());

        RadialVisualPackSession session = runtime.Ensure(loaded, 280)!;

        Assert.Equal("does-not-exist", loaded.VisualPackId);
        Assert.Equal("radial-v5", session.PackId);
        Assert.Equal(before, File.ReadAllBytes(temporary.FilePath));
    }

    [Fact]
    public void DefaultThemeBundleFailure_FallsBackToRadialV5()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        temporary.AddPack("fallback", "radial-v5", "Fallback");
        string broken = temporary.AddPack(
            "default",
            RadialVisualPackContract.DefaultVisualPackId,
            "Broken Default");
        UiVisualPackManifest manifest = RadialVisualPackDefinition.ReadManifest(broken);
        File.WriteAllText(Path.Combine(broken, manifest.Base), "not a png");
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root));

        RadialVisualPackSession session = runtime.Ensure(RadialMenuSettings.Default, 280)!;

        Assert.Equal("radial-v5", session.PackId);
        Assert.Equal(1, runtime.InstallCount);
    }

    [Fact]
    public void FallbackThemeFailure_TerminatesWithoutLooping()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string broken = temporary.AddPack("fallback", "radial-v5", "Broken Fallback");
        UiVisualPackManifest manifest = RadialVisualPackDefinition.ReadManifest(broken);
        File.WriteAllText(Path.Combine(broken, manifest.Base), "not a png");
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root));

        RadialVisualPackSession? session = runtime.Ensure(RadialMenuSettings.SafeFallback, 280);

        Assert.Null(session);
        Assert.Null(runtime.Active);
        Assert.Equal(0, runtime.InstallCount);
        Assert.NotNull(runtime.LastError);
    }

    [Fact]
    public void ProductionV1Plans_PropagateRadialV5FallbackAuthority()
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();
        RadialVisualPackCatalogEntry v1 = snapshot.Find("radial-v5")!;
        RadialVisualPackCatalogEntry radial8 = snapshot.Find("radial-8-minimal-v1")!;

        Assert.Equal("radial-v5", v1.Plan.Fallback.StartupFallbackThemeId);
        Assert.Equal("radial-v5", radial8.Plan.Fallback.StartupFallbackThemeId);
        Assert.True(v1.Plan.Fallback.RetainActiveOnCandidateFailure);
        Assert.True(radial8.Plan.Fallback.RetainActiveOnCandidateFailure);
    }

    [Fact]
    public void NewUserRadial8Mappings_AreEightIndependentNoneSlots()
    {
        RadialSlotMappings mappings = RadialMenuSettings.Default.GetProfileMappings("radial-8");

        Assert.Equal(8, mappings.Count);
        Assert.All(mappings, mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
        Assert.Equal(RadialActionKind.None, mappings[6].Kind);
        Assert.Equal(RadialActionKind.None, mappings[7].Kind);
        Assert.False(RadialMenuSettings.Default.MappingsByProfile.ContainsKey("radial-8"));
    }

    [Fact]
    public void RestoreDefaults_UsesTheNewCoherentPairWithoutSaving()
    {
        NativeReceiverTestThread.Run(() =>
        {
            using var temporary = new TemporarySettingsPath();
            using var controller = new RadialMenuController(new MutableLayoutOverlay(), RadialMenuSettings.SafeFallback);
            var store = new RadialMenuSettingsStore(temporary.FilePath);
            using var control = new RadialMenuSettingsControl(controller, store, controller.ApplySettings, _ => { });

            NativeReceiverTestThread.Click(control, "restoreDefaultSettingsButton");

            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings draft));
            // The Native editor materializes the currently displayed mapping profile.
            Assert.Equal(RadialMenuSettings.Default.SetProfileMappings("radial-8",
                RadialSlotMappings.Create(8)).NormalizeMappings(), draft);
            Assert.Equal("radial-8-minimal-v1", draft.VisualPackId);
            Assert.Equal("radial-8", draft.MappingProfileId);
            Assert.Equal(RadialMenuSettings.SafeFallback, controller.ConfiguredSettings);
            Assert.False(File.Exists(temporary.FilePath));
        });
    }

    [Fact]
    public void ProductionNoConfig_DefaultResolvesAndBuildsTheRadial8MinimalBundle()
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();
        RadialSettingsCoherenceResult effective = RadialSettingsCoherenceResolver.Resolve(
            RadialMenuSettings.Default,
            snapshot);
        RadialVisualPackCatalogEntry entry = snapshot.ResolveSelection(null)!;

        using var session = new RadialVisualPackSession(
            entry,
            effective.Settings,
            targetSize: 560,
            dpi: 192);

        Assert.False(effective.Repaired);
        Assert.Equal("radial-8-minimal-v1", session.PackId);
        Assert.Equal("radial-8", session.LayoutDefinition.ProfileId);
        Assert.Equal(8, session.LayoutDefinition.SlotCount);
        Assert.Equal(8, session.Mappings.Count);
    }

    private sealed class TemporarySettingsPath : IDisposable
    {
        public TemporarySettingsPath(bool createDirectory = true)
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "LeftPad.DefaultPolicy.Tests",
                Guid.NewGuid().ToString("N"));
            FilePath = Path.Combine(DirectoryPath, "radial-menu-settings.json");
            if (createDirectory) Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private sealed class ThrowingReadFileOperations : IAtomicFileOperations
    {
        public int WriteCount { get; private set; }
        public bool FileExists(string path) => true;
        public string ReadAllText(string path) => throw new IOException("injected read failure");
        public void CreateDirectory(string path) => WriteCount++;
        public void WriteDurable(string path, byte[] content) => WriteCount++;
        public void Replace(string sourcePath, string destinationPath, string? backupPath) =>
            WriteCount++;
        public void Move(string sourcePath, string destinationPath) => WriteCount++;
        public void Delete(string path) => WriteCount++;
    }
}
