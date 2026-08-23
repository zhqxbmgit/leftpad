using System.Drawing;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class Radial8RuntimeIntegrationTests
{
    private const string Radial8PackId = "radial-8-test";

    [Fact]
    public void SessionCreation_UsesOneLayoutDefinitionAndProfileMappingsEverywhere()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        RadialVisualPackDefinition definition = RadialVisualPackDefinition.Load(
            temporary.AddRadial8Pack());
        RadialMenuSettings settings = Radial8Settings().SetProfileMappings(
            LayoutProfileRegistry.Radial8ProfileId,
            RadialSlotMappings.Create(8).WithSlot(8, Keyboard(KeyboardKey.F8)));
        using var session = new RadialVisualPackSession(definition, settings, targetSize: 280);
        using var overlay = new SessionLayoutOverlay(session.LayoutDefinition);
        using var controller = new RadialMenuController(overlay, settings);

        controller.OpenAt(new Point(500, 500), RadialTriggerSource.ForAction("cross"));
        Assert.True(controller.UpdateSelectionForCursor(new Point(429, 429)));

        Assert.Same(definition.LayoutDefinition, session.LayoutDefinition);
        Assert.Same(definition, session.AssetCache.Definition);
        Assert.Same(definition, session.DynamicContent.Definition);
        Assert.Same(session.LayoutDefinition, controller.CurrentLayoutDefinition);
        Assert.Same(settings.GetProfileMappings("radial-8"), session.Mappings);
        Assert.Equal("radial-8", controller.ActiveSettings.MappingProfileId);
        Assert.Equal(8, controller.SelectedSlot);
    }

    [Fact]
    public void SessionCachesAndDynamicContent_UseEightIndependentMappings()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        RadialVisualPackDefinition definition = RadialVisualPackDefinition.Load(
            temporary.AddRadial8Pack());
        KeyboardKey[] keys =
        {
            KeyboardKey.F1,
            KeyboardKey.F2,
            KeyboardKey.F3,
            KeyboardKey.F4,
            KeyboardKey.F5,
            KeyboardKey.F6,
            KeyboardKey.F7,
            KeyboardKey.F8
        };
        RadialSlotMappings mappings = RadialSlotMappings.Create(8, keys.Select(Keyboard));
        RadialMenuSettings settings = Radial8Settings().SetProfileMappings("radial-8", mappings);

        using var session = new RadialVisualPackSession(definition, settings, targetSize: 280);

        Assert.Equal(8, session.LayoutDefinition.SlotCount);
        Assert.Equal(8, session.AssetCache.SelectedSlotCount);
        Assert.Equal(8, session.DynamicContent.RenderedSlotCount);
        Assert.Equal(8, session.Mappings.Count);
        Assert.Equal(keys, session.Mappings.Select(mapping => mapping.Key!.Value));
        Assert.Equal(
            new[] { 0d, 45d, 90d, 135d, 180d, 225d, 270d, 315d },
            session.LayoutDefinition.Slots.Select(slot => slot.AngleDegrees));
    }

    [Fact]
    public void ThemeSwitch_RadialV5ToRadial8AndBackAtomicallySwapsSessions()
    {
        using var temporary = CatalogWithDefaultAndRadial8();
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root));

        RadialVisualPackSession radial6 = runtime.Ensure(RadialMenuSettings.Default, 280)!;
        RadialVisualPackSession radial8 = runtime.Ensure(Radial8Settings(), 280)!;

        Assert.Equal(Radial8PackId, runtime.ActivePackId);
        Assert.Same(radial8, runtime.Active);
        Assert.NotSame(radial6, radial8);
        Assert.True(radial6.IsDisposed);
        Assert.False(radial8.IsDisposed);
        Assert.Equal(8, radial8.LayoutDefinition.SlotCount);

        RadialVisualPackSession radial6Again = runtime.Ensure(RadialMenuSettings.Default, 280)!;

        Assert.Equal(RadialVisualPackContract.DefaultVisualPackId, runtime.ActivePackId);
        Assert.Same(radial6Again, runtime.Active);
        Assert.NotSame(radial8, radial6Again);
        Assert.True(radial8.IsDisposed);
        Assert.False(radial6Again.IsDisposed);
        Assert.Equal(6, radial6Again.LayoutDefinition.SlotCount);
        Assert.Equal(3, runtime.InstallCount);
    }

    [Fact]
    public void ThemeSwitchFailure_PreservesPreviousRadial8Session()
    {
        using var temporary = CatalogWithDefaultAndRadial8();
        string broken = temporary.AddPack("broken", "broken", "Broken");
        File.WriteAllText(Path.Combine(broken, "radial-base-v5.png"), "not a png");
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root));
        _ = runtime.Ensure(RadialMenuSettings.Default, 280);
        RadialVisualPackSession radial8 = runtime.Ensure(Radial8Settings(), 280)!;

        RadialVisualPackSession retained = runtime.Ensure(
            RadialMenuSettings.Default with { VisualPackId = "broken" },
            280)!;

        Assert.Same(radial8, retained);
        Assert.Same(radial8, runtime.Active);
        Assert.Equal(Radial8PackId, runtime.ActivePackId);
        Assert.False(radial8.IsDisposed);
        Assert.Equal(2, runtime.InstallCount);
    }

    [Theory]
    [InlineData(7, -100, 0, KeyboardKey.F7)]
    [InlineData(8, -100, -100, KeyboardKey.F8)]
    public void Slot7AndSlot8_CompleteResolveAndExecute(
        int slot,
        int deltaX,
        int deltaY,
        KeyboardKey key)
    {
        using var temporary = new RadialVisualPackTestDirectory();
        LayoutDefinition layout = RadialVisualPackDefinition.Load(
            temporary.AddRadial8Pack()).LayoutDefinition;
        RadialMenuSettings settings = Radial8Settings().SetProfileMappings(
            "radial-8",
            RadialSlotMappings.Create(8).WithSlot(slot, Keyboard(key)));
        using var overlay = new SessionLayoutOverlay(layout);
        using var controller = new RadialMenuController(overlay, settings);
        var output = new RecordingKeyboardOutput();
        using var service = new Ds4Service(
            keyboardOutput: output,
            bindingStore: new MemoryBindingStore());
        RadialTriggerSource source = RadialTriggerSource.ForAction("cross");
        var anchor = new Point(500, 500);

        controller.OpenAt(anchor, source);
        Assert.True(controller.UpdateSelectionForCursor(
            new Point(anchor.X + deltaX, anchor.Y + deltaY)));
        Assert.Equal(slot, controller.SelectedSlot);
        Assert.True(controller.TryCompleteFrom(source, out RadialMenuCompletion completion));

        string message = RadialActionCompletionHandler.Handle(
            service,
            controller.ActiveSettings,
            completion);

        Assert.Equal($"[环形菜单] 已执行：Slot {slot}（键盘 {key}）", message);
        Assert.Equal(new[] { $"{key} down", $"{key} up" }, output.Events);
    }

    [Fact]
    public void Radial6Regression_StillUsesSixSlotsAndRadial6Mappings()
    {
        RadialVisualPackDefinition definition = RadialVisualPackDefinition.Load(
            RadialVisualPackDefinition.DefaultDirectory);
        using var session = new RadialVisualPackSession(
            definition,
            RadialMenuSettings.Default,
            targetSize: 280);

        Assert.Equal("radial-6", session.LayoutDefinition.ProfileId);
        Assert.Equal(6, session.AssetCache.SelectedSlotCount);
        Assert.Equal(6, session.DynamicContent.RenderedSlotCount);
        Assert.Equal(6, session.Mappings.Count);
        Assert.Equal("radial-v5", session.PackId);
    }

    private static RadialVisualPackTestDirectory CatalogWithDefaultAndRadial8()
    {
        var temporary = new RadialVisualPackTestDirectory();
        temporary.AddPack("default", "radial-v5", "Tactical HUD V5");
        temporary.AddRadial8Pack();
        return temporary;
    }

    private static RadialMenuSettings Radial8Settings() =>
        RadialMenuSettings.Default with
        {
            VisualPackId = Radial8PackId,
            MappingProfileId = LayoutProfileRegistry.Radial8ProfileId
        };

    private static RadialSlotMapping Keyboard(KeyboardKey key) => new()
    {
        Kind = RadialActionKind.KeyboardKey,
        Key = key
    };

    private sealed class SessionLayoutOverlay : IRadialMenuOverlay, IRadialLayoutProvider
    {
        public SessionLayoutOverlay(LayoutDefinition layoutDefinition)
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

    private sealed class MemoryBindingStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();

        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
    }
}
