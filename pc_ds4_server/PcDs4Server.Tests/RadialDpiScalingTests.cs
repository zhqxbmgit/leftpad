using System.Drawing;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialDpiScalingTests
{
    [Theory]
    [InlineData(96, 280)]
    [InlineData(144, 420)]
    [InlineData(192, 560)]
    public void LogicalCanvas_ConvertsToExpectedPhysicalBacking(int dpi, int expected)
    {
        Assert.Equal(expected, RadialDpiScaling.ToPhysicalPixels(280, dpi));
    }

    [Theory]
    [InlineData(96, 28)]
    [InlineData(144, 42)]
    [InlineData(192, 56)]
    public void LogicalDeadZone_ConvertsWithTheSameDpiScale(int dpi, int expected)
    {
        Assert.Equal(expected, RadialDpiScaling.ToPhysicalPixels(28, dpi));
    }

    [Theory]
    [InlineData(96, 280)]
    [InlineData(144, 420)]
    [InlineData(192, 560)]
    public void PhysicalPlacement_PreservesTheRequestedCenter(int dpi, int physicalSize)
    {
        var anchor = new Point(1200, 700);

        Point topLeft = RadialDpiScaling.CenterAt(anchor, physicalSize);
        var reconstructedCenter = new Point(
            topLeft.X + (physicalSize / 2),
            topLeft.Y + (physicalSize / 2));

        Assert.Equal(RadialDpiScaling.ToPhysicalPixels(280, dpi), physicalSize);
        Assert.Equal(anchor, reconstructedCenter);
    }

    [Fact]
    public void PhysicalSession_CachesAndDynamicContentMatchTheBackingSize()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        RadialVisualPackDefinition definition = RadialVisualPackDefinition.Load(
            temporary.AddRadial8Pack());
        int physicalSize = RadialDpiScaling.ToPhysicalPixels(280, dpi: 192);

        using var session = new RadialVisualPackSession(
            definition,
            RadialMenuSettings.Default,
            physicalSize);

        Assert.Equal(new Size(560, 560), session.AssetCache.ScaledBase.Size);
        Assert.Equal(8, session.AssetCache.SelectedSlotCount);
        for (int slot = 1; slot <= 8; slot++)
            Assert.Equal(new Size(560, 560), session.AssetCache.GetSelectedSlot(slot).Size);
        Assert.Equal(new Size(560, 560), session.DynamicContent.Content.Size);
    }

    [Fact]
    public void WheelCenter_ScalesDirectlyFromMasterToPhysicalBacking()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        RadialVisualPackDefinition definition = RadialVisualPackDefinition.Load(
            temporary.AddRadial8Pack());

        PointF center = definition.ScalePoint(
            definition.LayoutDefinition.WheelCenter,
            targetSize: 560);

        Assert.Equal(new PointF(280f, 280f), center);
    }

    [Fact]
    public void Controller_UsesPhysicalDeadZoneWithoutChangingRadialSelection()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        LayoutDefinition layout = RadialVisualPackDefinition.Load(
            temporary.AddRadial8Pack()).LayoutDefinition;
        using var overlay = new DpiAwareOverlay(layout, dpi: 192);
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        RadialTriggerSource source = RadialTriggerSource.ForAction("cross");
        var anchor = new Point(1000, 700);
        controller.OpenAt(anchor, source);

        Assert.False(controller.UpdateSelectionForCursor(new Point(anchor.X, anchor.Y - 56)));
        Assert.Equal(0, controller.SelectedSlot);
        Assert.True(controller.UpdateSelectionForCursor(new Point(anchor.X, anchor.Y - 57)));
        Assert.Equal(1, controller.SelectedSlot);
    }

    private sealed class DpiAwareOverlay : IRadialMenuOverlay, IRadialLayoutProvider,
        IRadialDpiProvider
    {
        public DpiAwareOverlay(LayoutDefinition layout, int dpi)
        {
            ActiveLayoutDefinition = layout;
            ActiveDpi = dpi;
        }

        public bool IsVisible { get; private set; }
        public LayoutDefinition ActiveLayoutDefinition { get; }
        public int ActiveDpi { get; }

        public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
            IsVisible = true;

        public void Hide() => IsVisible = false;
        public void Dispose() => IsVisible = false;
    }
}
