using System.Drawing;

namespace PcDs4Server.Tests;

// Applying settings deliberately does not publish a layout. Tests control that boundary.
internal sealed class MutableLayoutOverlay : IRadialMenuOverlay, IRadialLayoutProvider
{
    public LayoutDefinition? ActiveLayoutDefinition { get; set; }
    public bool IsVisible { get; private set; }
    public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
        IsVisible = true;
    public void Hide() => IsVisible = false;
    public void Dispose() => IsVisible = false;
}

internal static class CrossProfileSettingsScenario
{
    internal static RadialMenuSettings Original => (RadialMenuSettings.Default with
    {
        VisualPackId = "radial-v5",
        MappingProfileId = "radial-6"
    }).SetProfileMappings("radial-6", RadialSlotMappings.Create(6).WithSlot(1, new()
    {
        Kind = RadialActionKind.KeyboardKey,
        Key = KeyboardKey.F1
    })).SetProfileMappings("radial-8", RadialSlotMappings.Create(8).WithSlot(8, new()
    {
        Kind = RadialActionKind.KeyboardKey,
        Key = KeyboardKey.F8
    })).NormalizeMappings();

    internal static RadialMenuSettings Candidate => Original with
    {
        VisualPackId = "radial-8-minimal-v1",
        MappingProfileId = "radial-8",
        ScalePercent = 117,
        FontSize = 19.5f,
        DoubleTapWindowMs = 321,
        SelectionDeadZone = 37,
        ReceiverUiScalePercent = 125
    };

    internal static LayoutDefinition Layout(string visualPackId) =>
        new RadialVisualPackCatalog().Discover().Packs
            .Single(pack => pack.Id == visualPackId).LayoutDefinition;

    internal static MutableLayoutOverlay PendingOverlay() => new()
    {
        ActiveLayoutDefinition = Layout("radial-v5")
    };
}
