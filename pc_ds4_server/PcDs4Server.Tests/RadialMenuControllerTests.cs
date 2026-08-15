using System.Drawing;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialMenuControllerTests
{
    private static readonly RadialTriggerSource CrossSource = RadialTriggerSource.ForAction("cross");
    private static readonly RadialTriggerSource CircleSource = RadialTriggerSource.ForAction("circle");

    [Fact]
    public void OpenAt_OpensAtCurrentPointAndIgnoresAnotherOpenUntilClosed()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        var pointA = new Point(100, 200);
        var pointB = new Point(700, 500);

        controller.OpenAt(pointA, CrossSource);

        Assert.True(controller.IsOpen);
        Assert.True(controller.IsNormalMenuOpen);
        Assert.Equal(pointA, controller.NormalAnchor);
        Assert.Equal(CrossSource, controller.OpenSource);
        Assert.Equal(0, controller.SelectedSlot);
        Assert.Equal(1, overlay.ShowCalls);
        Assert.Equal(0, overlay.HideCalls);
        Assert.Equal(pointA, overlay.AnchorPoints.Single());

        controller.OpenAt(pointB, CircleSource);

        Assert.True(controller.IsOpen);
        Assert.Equal(1, overlay.ShowCalls);
        Assert.Equal(0, overlay.HideCalls);
        Assert.Equal(CrossSource, controller.OpenSource);

        controller.Close();
        controller.OpenAt(pointB, CircleSource);

        Assert.True(controller.IsOpen);
        Assert.Equal(2, overlay.ShowCalls);
        Assert.Equal(new[] { pointA, pointB }, overlay.AnchorPoints);
        Assert.Equal(CircleSource, controller.OpenSource);
    }

    [Fact]
    public void Close_HidesAnOpenOverlayAndIsOtherwiseIdle()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);

        controller.Close();
        controller.OpenAt(new Point(100, 200), CrossSource);
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
        controller.OpenAt(new Point(100, 200), CrossSource);

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
        controller.OpenAt(new Point(300, 400), CrossSource);

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
        controller.OpenAt(new Point(700, 500), CrossSource);

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
        controller.OpenAt(new Point(300, 400), CrossSource);

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

    [Fact]
    public void SelectionUpdates_RenderOnlyWhenSlotChangesAndResetOnClose()
    {
        var overlay = new FakeRadialMenuOverlay();
        var anchor = new Point(500, 500);
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);

        controller.OpenAt(anchor, CrossSource);
        Assert.Equal(0, controller.SelectedSlot);
        Assert.Equal(new[] { 0 }, overlay.SelectedSlots);

        Assert.True(controller.UpdateSelectionForCursor(new Point(500, 470)));
        Assert.Equal(1, controller.SelectedSlot);
        Assert.Equal(2, overlay.ShowCalls);

        Assert.False(controller.UpdateSelectionForCursor(new Point(500, 400)));
        Assert.Equal(2, overlay.ShowCalls);

        Assert.True(controller.UpdateSelectionForCursor(new Point(550, 450)));
        Assert.Equal(2, controller.SelectedSlot);
        Assert.Equal(3, overlay.ShowCalls);

        Assert.True(controller.UpdateSelectionForCursor(anchor));
        Assert.Equal(0, controller.SelectedSlot);
        Assert.Equal(4, overlay.ShowCalls);

        controller.Close();
        Assert.False(controller.IsNormalMenuOpen);
        Assert.Null(controller.NormalAnchor);
        Assert.Null(controller.OpenSource);
        Assert.Equal(0, controller.SelectedSlot);

        var newAnchor = new Point(700, 300);
        controller.OpenAt(newAnchor, CrossSource);
        Assert.True(controller.IsNormalMenuOpen);
        Assert.Equal(newAnchor, controller.NormalAnchor);
        Assert.Equal(0, controller.SelectedSlot);
        Assert.Equal(5, overlay.ShowCalls);
    }

    [Fact]
    public void PreviewNeverEntersNormalSelectionLifecycle()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);

        controller.PreviewAt(new Point(1000, 500), RadialMenuSettings.Default);
        bool rendered = controller.UpdateSelectionForCursor(new Point(1000, 400));

        Assert.True(controller.IsPreviewActive);
        Assert.False(controller.IsNormalMenuOpen);
        Assert.Equal(0, controller.SelectedSlot);
        Assert.False(rendered);
        Assert.Equal(1, overlay.ShowCalls);
        Assert.Equal(new[] { 0 }, overlay.SelectedSlots);

        controller.ClosePreview();
        Assert.False(controller.IsPreviewActive);
        Assert.False(controller.IsOpen);
    }

    [Fact]
    public void StartingPreviewStopsAnOpenNormalSelectionSession()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.OpenAt(new Point(500, 500), CrossSource);
        controller.UpdateSelectionForCursor(new Point(500, 450));

        controller.PreviewAt(new Point(1000, 500), RadialMenuSettings.Default);

        Assert.False(controller.IsNormalMenuOpen);
        Assert.True(controller.IsPreviewActive);
        Assert.Null(controller.NormalAnchor);
        Assert.Equal(0, controller.SelectedSlot);
        Assert.Equal(0, overlay.SelectedSlots.Last());
    }

    [Fact]
    public void CompleteFromMatchingAction_ReturnsSelectedSlotAndClearsSession()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.OpenAt(new Point(500, 500), CrossSource);
        controller.UpdateSelectionForCursor(new Point(550, 450));

        bool completed = controller.TryCompleteFrom(CrossSource, out RadialMenuCompletion completion);

        Assert.True(completed);
        Assert.Equal(2, completion.SelectedSlot);
        Assert.False(completion.IsCancelled);
        Assert.False(controller.IsNormalMenuOpen);
        Assert.Equal(0, controller.SelectedSlot);
        Assert.Null(controller.NormalAnchor);
        Assert.Null(controller.OpenSource);
        Assert.Equal(1, overlay.HideCalls);
    }

    [Fact]
    public void CompleteFromDifferentAction_IsIgnoredAndMenuStaysOpen()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.OpenAt(new Point(500, 500), CrossSource);

        bool completed = controller.TryCompleteFrom(CircleSource, out RadialMenuCompletion completion);

        Assert.False(completed);
        Assert.Equal(default, completion);
        Assert.True(controller.IsNormalMenuOpen);
        Assert.Equal(CrossSource, controller.OpenSource);
        Assert.Equal(0, overlay.HideCalls);
    }

    [Fact]
    public void CompleteFromMatchingSourceWithoutSelection_CancelsAndCloses()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.OpenAt(new Point(500, 500), CrossSource);

        bool completed = controller.TryCompleteFrom(CrossSource, out RadialMenuCompletion completion);

        Assert.True(completed);
        Assert.True(completion.IsCancelled);
        Assert.Equal(0, completion.SelectedSlot);
        Assert.False(controller.IsNormalMenuOpen);
        Assert.Null(controller.OpenSource);
    }

    [Fact]
    public void PreviewHasNoOpenSourceAndCannotComplete()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.PreviewAt(new Point(1000, 500), RadialMenuSettings.Default);

        bool completed = controller.TryCompleteFrom(CrossSource, out RadialMenuCompletion completion);

        Assert.False(completed);
        Assert.Equal(default, completion);
        Assert.True(controller.IsPreviewActive);
        Assert.Null(controller.OpenSource);
        Assert.Equal(0, overlay.HideCalls);
    }

    [Fact]
    public void OpenDuringPreview_ClosesPreviewWithoutStartingNormalSession()
    {
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        controller.PreviewAt(new Point(1000, 500), RadialMenuSettings.Default);

        controller.OpenAt(new Point(500, 500), CrossSource);

        Assert.False(controller.IsOpen);
        Assert.False(controller.IsPreviewActive);
        Assert.False(controller.IsNormalMenuOpen);
        Assert.Null(controller.OpenSource);
        Assert.Equal(1, overlay.ShowCalls);
        Assert.Equal(1, overlay.HideCalls);
    }

    private sealed class FakeRadialMenuOverlay : IRadialMenuOverlay
    {
        public bool IsVisible { get; private set; }
        public int ShowCalls { get; private set; }
        public int HideCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public List<Point> AnchorPoints { get; } = new();
        public List<RadialMenuSettings> Settings { get; } = new();
        public List<int> SelectedSlots { get; } = new();

        public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot)
        {
            ShowCalls++;
            AnchorPoints.Add(screenPoint);
            Settings.Add(settings);
            SelectedSlots.Add(selectedSlot);
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
