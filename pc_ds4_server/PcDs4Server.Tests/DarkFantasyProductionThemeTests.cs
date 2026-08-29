using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class DarkFantasyProductionThemeTests
{
    private const string ThemeId = "dark-fantasy-radial8-v1";
    private const string ThemeName = "Dark Fantasy Radial 8";
    private const string SpikeDirectoryName = "dark-fantasy-radial8-v2-dynamic";

    private static readonly IReadOnlyDictionary<int, string> SetAHashes =
        new Dictionary<int, string>
        {
            [0] = "33608F5E69F671F57E915453937AD056BDD51140A9FAF04DB54228D164C0ED04",
            [1] = "39342554BF97147DEDCA8ECBB663885FE92719A26128B9877C79AEA16054E93E",
            [2] = "E1D80B47AF03A405B1974F02C80908CC582E0FE76C22CCCFECBFA50C78186A79",
            [3] = "06B0C3305E6A2C7C9346B671E6D4971E9BF40347F72D73FC3BDBECC2A3B022C8",
            [5] = "C14AE5188422E89BA526B48D25EE14707CC61DF0B14EDB9170FF6CC5113F1D7F",
            [8] = "16E1307DB5DE3824D1AAFC3CBDD063A4EE58AA29E74BCC966D826C6FBA51D0FC",
        };

    private static readonly IReadOnlyDictionary<int, string> SetBHashes =
        new Dictionary<int, string>
        {
            [0] = "EE5EE43D7FE0C41C2C3646DD9434A5A6898880D36806D139D9DA7C45DBCDA7C6",
            [1] = "EED441A05F6F0817E699C36A9368520F0E16CA847BB7A23074D0E018C0584FF9",
            [3] = "36B7F4E77AE20882631DA2490096D95200C980662575284EB99A937D4718FBA7",
            [6] = "48EFD848B39B84515205D110A90779E8E476D3F49A79CD9BE70EA75A2C652CA9",
        };

    [Fact]
    public void ProductionPackage_IsValidMinimalAndByteIdenticalToFrozenSpike02()
    {
        string productionSource = ProductionSourceDirectory;
        string spikePackage = SpikePackageDirectory;
        Assert.True(Directory.Exists(productionSource));
        Assert.Equal(
            new[] { "manifest.json" }.Concat(StateNames.Select(name => $"states/{name}")),
            Directory.GetFiles(productionSource, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(productionSource, path).Replace('\\', '/'))
                .OrderBy(path => path == "manifest.json" ? 0 : 1)
                .ThenBy(path => path, StringComparer.Ordinal));

        foreach (string name in StateNames)
        {
            string frozen = Path.Combine(spikePackage, "states", name);
            string production = Path.Combine(productionSource, "states", name);
            byte[] frozenBytes = File.ReadAllBytes(frozen);
            byte[] productionBytes = File.ReadAllBytes(production);
            Assert.Equal(frozenBytes.Length, productionBytes.Length);
            Assert.Equal(Sha256(frozenBytes), Sha256(productionBytes));
            Assert.Equal(frozenBytes, productionBytes);
        }

        JsonObject spike = ParseObject(Path.Combine(spikePackage, "manifest.json"));
        JsonObject productionManifest = ParseObject(Path.Combine(productionSource, "manifest.json"));
        Assert.Equal(2, productionManifest["protocolVersion"]!.GetValue<int>());
        Assert.Equal(1, productionManifest["packageRevision"]!.GetValue<int>());
        Assert.Equal(ThemeId, productionManifest["id"]!.GetValue<string>());
        Assert.Equal(ThemeName, productionManifest["name"]!.GetValue<string>());
        Assert.False(productionManifest["exampleOnly"]!.GetValue<bool>());
        Assert.Equal("mixed", productionManifest["authoring"]!["method"]!.GetValue<string>());

        foreach (string metadata in new[] { "id", "name", "description" })
        {
            spike.Remove(metadata);
            productionManifest.Remove(metadata);
        }
        Assert.True(JsonNode.DeepEquals(spike, productionManifest));
    }

    [Fact]
    public void ProductionCatalog_LoadsExactIdentityPlanCapabilitiesAndAssetsWithoutIssues()
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();
        Assert.Empty(snapshot.Issues);
        RadialVisualPackCatalogEntry entry = Assert.IsType<RadialVisualPackCatalogEntry>(
            snapshot.Find(ThemeId));

        Assert.Equal(ThemeId, entry.Id);
        Assert.Equal(ThemeName, entry.Name);
        Assert.True(entry.IsV2);
        Assert.Equal(2, entry.Version);
        Assert.Equal("radial-8", entry.LayoutDefinition.ProfileId);
        Assert.NotNull(snapshot.Find(RadialVisualPackContract.DefaultVisualPackId));
        Assert.NotNull(snapshot.Find("radial-8-minimal-v1"));
        Assert.Same(entry, snapshot.ResolveSelection(ThemeId));

        NormalizedRenderPlan plan = entry.Plan;
        FullStateFrameRenderPlan model = Assert.IsType<FullStateFrameRenderPlan>(plan.RenderModel);
        Assert.Equal(1254, plan.ReferenceCanvas.Width);
        Assert.Equal(1254, plan.ReferenceCanvas.Height);
        Assert.Equal(420, plan.ReferenceScale.LogicalWidth);
        Assert.Equal(420, plan.ReferenceScale.LogicalHeight);
        Assert.Equal(210, plan.Placement.ActivationAnchor.X);
        Assert.Equal(210, plan.Placement.ActivationAnchor.Y);
        Assert.Equal(9, model.States.Count);
        Assert.Equal(19, model.OrderedLayers.Count);
        Assert.Equal(18, plan.DynamicTheme!.Anchors.Count);
        Assert.Equal(
            new[] { "fullStateFrame", "dynamicAnchors", "instantTransitions" },
            plan.RequiredRuntimeCapabilities);
        Assert.DoesNotContain(plan.RequiredRuntimeCapabilities, capability => capability is
            "layeredState" or "themeGlyphAssets" or "maskAssets" or "occlusionLayers" or
            "safeDynamicSurfaces" or "futureStates");
        Assert.DoesNotContain(model.OrderedLayers, layer => layer.Kind == "staticAsset");
        Assert.Single(model.OrderedLayers, layer => layer.Kind == "stateAsset");
        Assert.Equal(9, model.States.Values.SelectMany(state => state.Assets.Values)
            .Select(asset => asset.PackagePath).Distinct(StringComparer.Ordinal).Count());

        JsonObject manifest = ParseObject(Path.Combine(ProductionOutputDirectory, "manifest.json"));
        JsonObject hashes = Assert.IsType<JsonObject>(manifest["assetHashes"]);
        Assert.Equal(9, hashes.Count);
        foreach ((string path, JsonNode? expected) in hashes)
            Assert.Equal(expected!.GetValue<string>(), Sha256(File.ReadAllBytes(
                Path.Combine(ProductionOutputDirectory, path.Replace('/', Path.DirectorySeparatorChar)))));
    }

    [Fact]
    public void DynamicSemantics_PreserveKeyboardLabelsDs4SymbolsNoneAndSelectedOwnership()
    {
        NormalizedRenderPlan plan = LoadProductionEntry().Plan;
        NormalizedDynamicThemeModel dynamic = Assert.IsType<NormalizedDynamicThemeModel>(plan.DynamicTheme);
        NormalizedGlyphRole role = dynamic.GlyphRoles["darkFantasyAction"];
        ThemeMappingSnapshot mappings = ThemeMappingSnapshot.Capture(SetA(), "radial-8");
        var symbols = new LeftPadRuntimeSymbolProvider();

        ResolvedDynamicContent keyboard = ThemeDynamicContentResolver.Resolve(
            "slot1ActionGlyph", 0, mappings);
        Assert.Equal("E", keyboard.LabelText);
        Assert.Null(ThemeGlyphResolver.Resolve(role, keyboard, symbols));
        Assert.Equal("E", ThemeDynamicContentResolver.Resolve(
            "slot1ActionLabel", 0, mappings).LabelText);

        ResolvedDynamicContent shortcut = ThemeDynamicContentResolver.Resolve(
            "slot2ActionGlyph", 0, mappings);
        Assert.Equal("Ctrl+Shift+K", shortcut.LabelText);
        Assert.Null(ThemeGlyphResolver.Resolve(role, shortcut, symbols));
        Assert.Equal("Ctrl+Shift+K", ThemeDynamicContentResolver.Resolve(
            "slot2ActionLabel", 0, mappings).LabelText);

        ResolvedDynamicContent ds4 = ThemeDynamicContentResolver.Resolve(
            "slot3ActionGlyph", 0, mappings);
        ResolvedGlyphSource ds4Source = Assert.IsType<ResolvedGlyphSource>(
            ThemeGlyphResolver.Resolve(role, ds4, symbols));
        Assert.Equal("runtimeSymbol", ds4Source.Type);
        Assert.Equal("leftpad-ds4", ds4Source.SymbolSet);
        Assert.Equal("CROSS", ds4.GlyphId);
        Assert.Equal("CROSS", ThemeDynamicContentResolver.Resolve(
            "slot3ActionLabel", 0, mappings).LabelText);

        Assert.True(ThemeDynamicContentResolver.Resolve(
            "slot5ActionLabel", 0, mappings).IsEmpty);
        Assert.True(ThemeDynamicContentResolver.Resolve(
            "selectedActionGlyph", 5, mappings).IsEmpty);
        Assert.Equal("E", ThemeDynamicContentResolver.Resolve(
            "selectedActionLabel", 1, mappings).LabelText);
        Assert.Equal("CROSS", ThemeDynamicContentResolver.Resolve(
            "selectedActionGlyph", 3, mappings).GlyphId);
        Assert.Equal("selectedActionLabel",
            dynamic.OwnershipByLayer["selectedLabelLayer"].ContentKey);
        Assert.Equal("selectedActionGlyph",
            dynamic.OwnershipByLayer["selectedGlyphLayer"].ContentKey);
    }

    [Fact]
    public void ProductionRenderAt192Dpi_MatchesAllTenFrozenSpike02RawPixelHashes()
    {
        NormalizedRenderPlan plan = LoadProductionEntry().Plan;
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.Build(plan, SetA(), 280, 192);
        FullStateFrameCache cache = bundle.FullStateCache;

        Assert.True(bundle.IsFullStateFrame);
        Assert.Equal(new Size(840, 840), bundle.PhysicalSurfaceSize);
        Assert.Equal(9, cache.DecodedAssetCount);
        Assert.Equal(9, cache.AuthoredLayerCount);
        Assert.Equal(1, cache.DynamicBuildCount);
        Assert.Equal(0, cache.MappingRebuildCount);
        AssertHashes(bundle, SetAHashes);

        int decoded = cache.DecodedAssetCount;
        int authored = cache.AuthoredLayerCount;
        bundle.EnsureDynamicContent(SetB());

        Assert.Equal(decoded, cache.DecodedAssetCount);
        Assert.Equal(authored, cache.AuthoredLayerCount);
        Assert.Equal(2, cache.DynamicBuildCount);
        Assert.Equal(1, cache.MappingRebuildCount);
        AssertHashes(bundle, SetBHashes);
    }

    [Fact]
    public void ProductionDraftPreview_UsesRealIdleHwndWithoutApplyingOrSavingSettings()
    {
        Exception? failure = null;
        bool visible = false;
        bool hidden = false;
        int dpi = 0;
        int selectedSlot = -1;
        string[] labels = Array.Empty<string>();
        var thread = new Thread(() =>
        {
            try
            {
                RadialVisualPackCatalogEntry entry = LoadProductionEntry();
                RadialMenuSettings draft = SetA();
                ThemeMappingSnapshot mappings = ThemeMappingSnapshot.Capture(draft, "radial-8");
                labels = Enumerable.Range(1, 8).Select(slot =>
                    ThemeDynamicContentResolver.Resolve(
                        $"slot{slot}ActionLabel", 0, mappings).LabelText).ToArray();

                using var overlay = new RadialMenuOverlay(new RadialVisualPackCatalog());
                using var controller = new RadialMenuController(
                    overlay, RadialMenuSettings.Default, entry.LayoutDefinition);
                controller.PreviewAt(new Point(700, 500), draft);
                visible = controller.IsPreviewActive && overlay.IsVisible && overlay.IsHandleCreated;
                selectedSlot = controller.SelectedSlot;
                dpi = overlay.ActiveDpi;
                controller.ClosePreview();
                hidden = !controller.IsPreviewActive && !overlay.IsVisible;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(20)),
            "Production draft HWND preview timed out.");
        Assert.Null(failure);
        Assert.True(visible);
        Assert.True(hidden);
        Assert.True(dpi > 0);
        Assert.Equal(0, selectedSlot);
        Assert.Equal(
            new[] { "E", "Ctrl+Shift+K", "CROSS", "Tab", "", "TRIANGLE", "F1", "D-Pad Down" },
            labels);
    }

    private static void AssertHashes(RuntimeRenderBundle bundle,
        IReadOnlyDictionary<int, string> expected)
    {
        foreach ((int slot, string hash) in expected)
        {
            Bitmap bitmap = bundle.GetFinalState(slot);
            Assert.Equal(new Size(840, 840), bitmap.Size);
            Assert.Equal(PixelFormat.Format32bppPArgb, bitmap.PixelFormat);
            Assert.Equal(hash, RawPixelSha(bitmap));
        }
    }

    private static RadialVisualPackCatalogEntry LoadProductionEntry()
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();
        Assert.Empty(snapshot.Issues);
        return Assert.IsType<RadialVisualPackCatalogEntry>(snapshot.Find(ThemeId));
    }

    private static RadialMenuSettings SetA() => Settings(new[]
    {
        Keyboard(KeyboardKey.E),
        Shortcut(KeyboardKey.K, ctrl: true, shift: true),
        Ds4("cross"),
        Keyboard(KeyboardKey.Tab),
        RadialSlotMapping.None,
        Ds4("triangle"),
        Keyboard(KeyboardKey.F1),
        Ds4("dpad_down"),
    });

    private static RadialMenuSettings SetB() => Settings(new[]
    {
        Keyboard(KeyboardKey.F2),
        Shortcut(KeyboardKey.K, ctrl: true, shift: true),
        Ds4("square"),
        Keyboard(KeyboardKey.Tab),
        RadialSlotMapping.None,
        Ds4("circle"),
        Keyboard(KeyboardKey.F1),
        Ds4("dpad_down"),
    });

    private static RadialMenuSettings Settings(IEnumerable<RadialSlotMapping> mappings) =>
        (RadialMenuSettings.Default with
        {
            VisualPackId = ThemeId,
            MappingProfileId = "radial-8"
        }).SetProfileMappings("radial-8", RadialSlotMappings.Create(8, mappings));

    private static RadialSlotMapping Keyboard(KeyboardKey key) => new()
    {
        Kind = RadialActionKind.KeyboardKey,
        Key = key
    };

    private static RadialSlotMapping Shortcut(KeyboardKey key, bool ctrl, bool shift) => new()
    {
        Kind = RadialActionKind.KeyboardShortcut,
        Key = key,
        Ctrl = ctrl,
        Shift = shift
    };

    private static RadialSlotMapping Ds4(string button) => new()
    {
        Kind = RadialActionKind.Ds4Button,
        Ds4Button = button
    };

    private static string RawPixelSha(Bitmap bitmap)
    {
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(
            rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return Sha256(bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static JsonObject ParseObject(string path) =>
        Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllBytes(path)));

    private static string ProductionOutputDirectory => Path.Combine(
        UiThemeV2Contract.DiscoveryRoot, ThemeId);

    private static string ProductionSourceDirectory => Path.Combine(
        RepositoryRoot, "pc_ds4_server", "PcDs4Server", "Assets", "UIThemes", ThemeId);

    private static string SpikePackageDirectory => Path.Combine(
        RepositoryRoot, "pc_ds4_server", "visual_prototypes", "reference_fidelity_spikes",
        SpikeDirectoryName, "package");

    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "pc_ds4_server")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Repository root was not found from the test output.");
        }
    }

    private static IReadOnlyList<string> StateNames { get; } =
        new[] { "idle.png" }.Concat(Enumerable.Range(1, 8)
            .Select(slot => $"selected-{slot}.png")).ToArray();
}
