using System.Drawing;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialMenuControllerTests
{
    [Fact]
    public void ToggleAt_OpensAtCurrentPointClosesAndCanReopenAtNewPoint()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        var pointA = new Point(100, 200);
        var pointB = new Point(700, 500);

        controller.ToggleAt(pointA);

        Assert.True(controller.IsOpen);
        Assert.Equal(1, overlay.ShowCalls);
        Assert.Equal(0, overlay.HideCalls);
        Assert.Equal(pointA, overlay.AnchorPoints.Single());

        controller.ToggleAt(pointB);

        Assert.False(controller.IsOpen);
        Assert.Equal(1, overlay.ShowCalls);
        Assert.Equal(1, overlay.HideCalls);

        controller.ToggleAt(pointB);

        Assert.True(controller.IsOpen);
        Assert.Equal(2, overlay.ShowCalls);
        Assert.Equal(new[] { pointA, pointB }, overlay.AnchorPoints);
    }

    [Fact]
    public void Close_HidesAnOpenOverlayAndIsOtherwiseIdle()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);

        controller.Close();
        controller.ToggleAt(new Point(100, 200));
        controller.Close();
        controller.Close();

        Assert.False(controller.IsOpen);
        Assert.Equal(1, overlay.ShowCalls);
        Assert.Equal(1, overlay.HideCalls);
    }

    [Fact]
    public void Dispose_HidesAndDisposesTheOverlay()
    {
        var overlay = new FakeRadialMenuOverlay();
        var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.ToggleAt(new Point(100, 200));

        controller.Dispose();
        controller.Dispose();

        Assert.Equal(1, overlay.HideCalls);
        Assert.Equal(1, overlay.DisposeCalls);
        Assert.False(controller.IsOpen);
    }

    [Fact]
    public void ApplySettings_ChangesTheSettingsUsedByTheNextOpen()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        RadialMenuSettings applied = RadialMenuSettings.Default with { ScalePercent = 90 };

        controller.ApplySettings(applied);
        controller.ToggleAt(new Point(300, 400));

        Assert.Equal(applied, controller.ActiveSettings);
        Assert.Equal(applied, overlay.Settings.Single());
    }

    [Fact]
    public void Preview_DoesNotReplaceActiveSettings()
    {
        var overlay = new FakeRadialMenuOverlay();
        RadialMenuSettings active = RadialMenuSettings.Default;
        RadialMenuSettings preview = active with { ScalePercent = 80 };
        using var controller = new RadialMenuController(overlay, active);

        controller.PreviewAt(new Point(100, 200), preview);
        controller.ClosePreview();
        controller.ToggleAt(new Point(700, 500));

        Assert.Equal(active, controller.ActiveSettings);
        Assert.Equal(new[] { preview, active }, overlay.Settings);
        Assert.Equal(1, overlay.HideCalls);
    }

    [Fact]
    public void LivePreview_ReusesOriginalAnchorAndStopsUpdatingAfterClose()
    {
        var overlay = new FakeRadialMenuOverlay();
        RadialMenuSettings active = RadialMenuSettings.Default;
        RadialMenuSettings previewA = active with { ScalePercent = 80, DoubleTapWindowMs = 300 };
        RadialMenuSettings previewB = active with { ScalePercent = 90, DoubleTapWindowMs = 400 };
        RadialMenuSettings previewC = active with { ScalePercent = 110 };
        var anchor = new Point(1000, 500);
        using var controller = new RadialMenuController(overlay, active);

        controller.PreviewAt(anchor, previewA);
        controller.UpdatePreview(previewB);

        Assert.True(controller.IsPreviewActive);
        Assert.Equal(anchor, controller.PreviewAnchor);
        Assert.Equal(new[] { anchor, anchor }, overlay.AnchorPoints);
        Assert.Equal(new[] { previewA, previewB }, overlay.Settings);
        Assert.Equal(active, controller.ActiveSettings);

        controller.ClosePreview();
        controller.UpdatePreview(previewC);

        Assert.False(controller.IsPreviewActive);
        Assert.Null(controller.PreviewAnchor);
        Assert.Equal(2, overlay.ShowCalls);
        Assert.Equal(1, overlay.HideCalls);
    }

    [Fact]
    public void ClosePreview_DoesNotCloseAnUnrelatedNormalMenu()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.ToggleAt(new Point(300, 400));

        controller.ClosePreview();

        Assert.True(controller.IsOpen);
        Assert.False(controller.IsPreviewActive);
        Assert.Equal(0, overlay.HideCalls);
    }

    [Fact]
    public void ApplySettings_DuringPreviewKeepsAnchorAndPreviewSession()
    {
        var overlay = new FakeRadialMenuOverlay();
        RadialMenuSettings applied = RadialMenuSettings.Default with { ScalePercent = 90 };
        var anchor = new Point(1000, 500);
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.PreviewAt(anchor, RadialMenuSettings.Default with { ScalePercent = 80 });

        controller.ApplySettings(applied);

        Assert.True(controller.IsPreviewActive);
        Assert.Equal(anchor, controller.PreviewAnchor);
        Assert.Equal(anchor, overlay.AnchorPoints.Last());
        Assert.Equal(applied, overlay.Settings.Last());
        Assert.Equal(applied, controller.ActiveSettings);
    }

    [Fact]
    public void ApplySettings_RejectsInvalidDoubleTapWindowWithoutChangingActiveSettings()
    {
        var overlay = new FakeRadialMenuOverlay();
        RadialMenuSettings active = RadialMenuSettings.Default;
        using var controller = new RadialMenuController(overlay, active);

        Assert.Throws<ArgumentException>(() =>
            controller.ApplySettings(active with { DoubleTapWindowMs = 79 }));
        Assert.Equal(active, controller.ActiveSettings);
        Assert.Empty(overlay.Settings);
    }

    private sealed class FakeRadialMenuOverlay : IRadialMenuOverlay
    {
        public bool IsVisible { get; private set; }
        public int ShowCalls { get; private set; }
        public int HideCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public List<Point> AnchorPoints { get; } = new();
        public List<RadialMenuSettings> Settings { get; } = new();

        public void ShowAt(Point screenPoint, RadialMenuSettings settings)
        {
            ShowCalls++;
            AnchorPoints.Add(screenPoint);
            Settings.Add(settings);
            IsVisible = true;
        }

        public void Hide()
        {
            HideCalls++;
            IsVisible = false;
        }

        public void Dispose()
        {
            DisposeCalls++;
            IsVisible = false;
        }
    }
}
