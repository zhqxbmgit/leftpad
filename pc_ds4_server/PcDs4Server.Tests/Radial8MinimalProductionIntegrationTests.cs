using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class Radial8MinimalProductionIntegrationTests
{
    private const string ProductionPackId = "radial-8-minimal-v1";
    private const string ProductionPackName = "Radial 8 Minimal V1";
    private const string ExpectedBaseSha =
        "86236F861751A1A8944191E5F8BDC49B3096133DAF23CD588340163CCDF7DFF8";
    private const string ExpectedSelectedSha =
        "2516E30C60E8727AC6F173B8828708B454A9CC79A973BF6664180FB36D75A301";
    private const string ExpectedLayoutSha =
        "4028D56EC789E0CED3514DAFD357FD160223BCDEF7082F1F2B3B31D2219CE4E1";

    [Fact]
    public void ProductionCatalog_ContainsOnlyTheReleasedRadialPacks()
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();

        Assert.Empty(snapshot.Issues);
        Assert.Equal(
            new[] { RadialVisualPackContract.DefaultVisualPackId, ProductionPackId },
            snapshot.Packs.Select(pack => pack.Id));
        Assert.Equal(
            new[] { "Tactical HUD V5", ProductionPackName },
            snapshot.Packs.Select(pack => pack.Name));
        Assert.Null(snapshot.Find("radial-8-minimal-test"));
        Assert.Null(snapshot.Find("radial-8-orange-v2"));
        Assert.Null(snapshot.Find("radial-8-orange-v3"));
    }

    [Fact]
    public void ProductionAssets_MatchTheFrozenManifestLayoutAndHashes()
    {
        RadialVisualPackDefinition definition = LoadProductionDefinition();

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
        Assert.Equal(ExpectedLayoutSha, Sha256(definition.LayoutPath));
    }

    [Fact]
    public void ProductionSession_UsesEightCachesDynamicSlotsAndProfileMappings()
    {
        RadialSlotMappings mappings = RadialSlotMappings.Create(8)
            .WithSlot(7, Keyboard(KeyboardKey.F7))
            .WithSlot(8, Ds4("cross"));
        RadialMenuSettings settings = ProductionSettings(mappings);
        using var session = new RadialVisualPackSession(
            LoadProductionDefinition(),
            settings,
            targetSize: 280);

        Assert.Equal(ProductionPackId, session.PackId);
        Assert.Equal(8, session.LayoutDefinition.SlotCount);
        Assert.Equal(8, session.AssetCache.SelectedSlotCount);
        Assert.Equal(8, session.DynamicContent.RenderedSlotCount);
        Assert.Equal(8, session.Mappings.Count);
        Assert.Same(settings.GetProfileMappings(LayoutProfileRegistry.Radial8ProfileId),
            session.Mappings);
        Assert.Equal(KeyboardKey.F7, session.Mappings[6].Key);
        Assert.Equal("cross", session.Mappings[7].Ds4Button);
    }

    [Fact]
    public void ProductionSettingsUi_ListsThePackAndBuildsEightNumberedRows()
    {
        RunInSta(() =>
        {
            RadialVisualPackDefinition definition = LoadProductionDefinition();
            RadialMenuSettings settings = ProductionSettings(RadialSlotMappings.Create(8));
            using var overlay = new LayoutOverlay(definition.LayoutDefinition);
            using var controller = new RadialMenuController(overlay, settings);
            using var temporary = new TemporarySettingsPath();
            using var form = new RadialMenuSettingsForm(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { });
            form.PerformLayout();

            var visualPack = Assert.IsType<ComboBox>(Assert.Single(
                form.Controls.Find("visualPack", searchAllChildren: true)));
            Assert.Equal(
                new[] { "Tactical HUD V5", ProductionPackName },
                visualPack.Items.Cast<RadialVisualPackCatalogEntry>()
                    .Select(pack => pack.Name));
            Assert.Equal(
                ProductionPackId,
                Assert.IsType<RadialVisualPackCatalogEntry>(visualPack.SelectedItem).Id);

            var mappingTable = Assert.IsType<TableLayoutPanel>(Assert.Single(
                form.Controls.Find("mappingTable", searchAllChildren: true)));
            Assert.Equal(8, mappingTable.RowCount);
            Assert.Equal(
                Enumerable.Range(1, 8).Select(slot => $"Slot {slot}"),
                Enumerable.Range(1, 8).Select(slot =>
                    Assert.IsType<Label>(Assert.Single(
                        form.Controls.Find($"slot{slot}Label", searchAllChildren: true))).Text));
        });
    }

    [Fact]
    public void ProductionThemeSwitch_RadialV5ToMinimalAndBackSwapsAtomically()
    {
        using var runtime = new RadialVisualPackRuntime(new RadialVisualPackCatalog());

        RadialVisualPackSession radialV5 = runtime.Ensure(
            RadialMenuSettings.Default,
            targetSize: 280)!;
        RadialVisualPackSession minimal = runtime.Ensure(
            ProductionSettings(RadialSlotMappings.Create(8)),
            targetSize: 280)!;

        Assert.Equal(ProductionPackId, runtime.ActivePackId);
        Assert.Same(minimal, runtime.Active);
        Assert.True(radialV5.IsDisposed);
        Assert.False(minimal.IsDisposed);

        RadialVisualPackSession radialV5Again = runtime.Ensure(
            RadialMenuSettings.Default,
            targetSize: 280)!;

        Assert.Equal(RadialVisualPackContract.DefaultVisualPackId, runtime.ActivePackId);
        Assert.Same(radialV5Again, runtime.Active);
        Assert.True(minimal.IsDisposed);
        Assert.False(radialV5Again.IsDisposed);
        Assert.Equal(3, runtime.InstallCount);
    }

    [Fact]
    public void ProductionSlotsSevenAndEight_SelectCompleteResolveAndExecute()
    {
        RadialSlotMappings mappings = RadialSlotMappings.Create(8)
            .WithSlot(7, Keyboard(KeyboardKey.F7))
            .WithSlot(8, Ds4("cross"));
        RadialMenuSettings settings = ProductionSettings(mappings);
        var directFactory = new RecordingDirectDs4Factory();
        var keyboardOutput = new RecordingKeyboardOutput();
        using var service = new Ds4Service(
            directFactory,
            keyboardOutput,
            new MemoryBindingStore(),
            radialDs4Delay: _ => { });
        Assert.True(service.Initialize());
        using var overlay = new LayoutOverlay(LoadProductionDefinition().LayoutDefinition);
        using var controller = new RadialMenuController(overlay, settings);

        RadialMenuCompletion slot7 = CompleteAt(controller, deltaX: -100, deltaY: 0);
        Assert.Equal(7, slot7.SelectedSlot);
        Assert.Equal(KeyboardKey.F7,
            RadialActionResolver.GetMapping(
                controller.ActiveSettings,
                LayoutProfileRegistry.Radial8ProfileId,
                slot7.SelectedSlot).Key);
        Assert.Equal(
            "[环形菜单] 已执行：Slot 7（键盘 F7）",
            RadialActionCompletionHandler.Handle(service, controller.ActiveSettings, slot7));
        Assert.Equal(new[] { "F7 down", "F7 up" }, keyboardOutput.Events);

        RadialMenuCompletion slot8 = CompleteAt(controller, deltaX: -100, deltaY: -100);
        Assert.Equal(8, slot8.SelectedSlot);
        Assert.Equal("cross",
            RadialActionResolver.GetMapping(
                controller.ActiveSettings,
                LayoutProfileRegistry.Radial8ProfileId,
                slot8.SelectedSlot).Ds4Button);
        Assert.Equal(
            "[环形菜单] 已执行：Slot 8（DS4 CROSS）",
            RadialActionCompletionHandler.Handle(service, controller.ActiveSettings, slot8));
        Assert.Equal(2, directFactory.Session.SetButtonCalls);
        Assert.Equal(2, directFactory.Session.SubmitCalls);
    }

    [Fact]
    public void ProductionVisualPackId_PersistsAndRestoresWithoutChangingDefaultPolicy()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings expected = ProductionSettings(RadialSlotMappings.Create(8)
            .WithSlot(8, Keyboard(KeyboardKey.F8)));

        Assert.True(store.TrySave(expected, out string saveError), saveError);
        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(ProductionPackId, result.Settings.VisualPackId);
        Assert.Equal(LayoutProfileRegistry.Radial8ProfileId, result.Settings.MappingProfileId);
        Assert.Equal(KeyboardKey.F8,
            result.Settings.GetProfileMappings(LayoutProfileRegistry.Radial8ProfileId)[7].Key);
        Assert.Equal(
            RadialVisualPackContract.DefaultVisualPackId,
            RadialMenuSettings.Default.VisualPackId);
    }

    private static string ProductionPackDirectory => Path.Combine(
        RadialVisualPackContract.DiscoveryRoot,
        ProductionPackId);

    private static RadialVisualPackDefinition LoadProductionDefinition() =>
        RadialVisualPackDefinition.Load(ProductionPackDirectory);

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static RadialMenuSettings ProductionSettings(RadialSlotMappings mappings) =>
        (RadialMenuSettings.Default with
        {
            VisualPackId = ProductionPackId,
            MappingProfileId = LayoutProfileRegistry.Radial8ProfileId
        }).SetProfileMappings(LayoutProfileRegistry.Radial8ProfileId, mappings);

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

    private static RadialMenuCompletion CompleteAt(
        RadialMenuController controller,
        int deltaX,
        int deltaY)
    {
        var anchor = new Point(500, 500);
        RadialTriggerSource source = RadialTriggerSource.ForAction("cross");
        controller.OpenAt(anchor, source);
        Assert.True(controller.UpdateSelectionForCursor(
            new Point(anchor.X + deltaX, anchor.Y + deltaY)));
        Assert.True(controller.TryCompleteFrom(source, out RadialMenuCompletion completion));
        return completion;
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class LayoutOverlay : IRadialMenuOverlay, IRadialLayoutProvider
    {
        public LayoutOverlay(LayoutDefinition layoutDefinition)
        {
            ActiveLayoutDefinition = layoutDefinition;
        }

        public bool IsVisible { get; private set; }
        public LayoutDefinition ActiveLayoutDefinition { get; }

        public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
            IsVisible = true;

        public void Hide() => IsVisible = false;
        public void Dispose() => IsVisible = false;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<string> Events { get; } = new();

        public void SetKeyState(KeyboardKey key, bool isPressed) =>
            Events.Add($"{key} {(isPressed ? "down" : "up")}");
    }

    private sealed class RecordingDirectDs4Factory : IDirectDs4Factory
    {
        public RecordingDirectDs4Session Session { get; } = new();

        public IDirectDs4Session Create() => Session;
    }

    private sealed class RecordingDirectDs4Session : IDirectDs4Session
    {
        public int SetButtonCalls { get; private set; }
        public int SubmitCalls { get; private set; }

        public void SetButton(DualShock4Button button, bool pressed) => SetButtonCalls++;
        public void SetDPadDirection(DualShock4DPadDirection direction) { }
        public void SetTrigger(DualShock4Slider trigger, byte value) { }
        public void SetLeftStick(byte x, byte y) { }
        public void SubmitReport() => SubmitCalls++;
        public void Dispose() { }
    }

    private sealed class MemoryBindingStore : IKeyboardBindingStore
    {
        public KeyboardBindings Load() => new();
        public void Save(KeyboardBindings bindings) { }
    }

    private sealed class TemporarySettingsPath : IDisposable
    {
        public TemporarySettingsPath()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "LeftPad.Radial8MinimalProduction.Tests",
                Guid.NewGuid().ToString("N"));
            FilePath = Path.Combine(DirectoryPath, "radial-menu-settings.json");
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
