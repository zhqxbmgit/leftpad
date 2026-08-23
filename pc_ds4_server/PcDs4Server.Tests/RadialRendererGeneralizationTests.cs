using System.Drawing;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialRendererGeneralizationTests
{
    private static readonly double[] Radial6Angles = { 0d, 60d, 120d, 180d, 240d, 300d };
    private static readonly double[] Radial8Angles = { 0d, 45d, 90d, 135d, 180d, 225d, 270d, 315d };

    [Fact]
    public void Radial6_SelectedCacheUsesAllDeclaredSlotsAndAngles()
    {
        RadialVisualPackDefinition pack = RadialVisualPackDefinition.Load(
            RadialVisualPackDefinition.DefaultDirectory);
        using var cache = new RadialVisualPackCache(pack, targetSize: 280);

        Assert.Equal(6, cache.SelectedSlotCount);
        AssertSelectedAngles(cache, Radial6Angles);
    }

    [Fact]
    public void Radial8_SelectedCacheUsesAllDeclaredSlotsAndAngles()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        RadialVisualPackDefinition pack = RadialVisualPackDefinition.Parse(
            temporary.AddRadial8Pack());
        using var cache = new RadialVisualPackCache(pack, targetSize: 280);

        Assert.Equal(8, cache.SelectedSlotCount);
        AssertSelectedAngles(cache, Radial8Angles);
    }

    [Fact]
    public void FailedRebuildInput_PreservesExistingSelectedAndBaseCaches()
    {
        RadialVisualPackDefinition pack = RadialVisualPackDefinition.Load(
            RadialVisualPackDefinition.DefaultDirectory);
        using var cache = new RadialVisualPackCache(pack, targetSize: 280);
        Bitmap originalBase = cache.ScaledBase;
        Bitmap originalSelected = cache.GetSelectedSlot(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Rebuild(0));

        Assert.Equal(280, cache.TargetSize);
        Assert.Same(originalBase, cache.ScaledBase);
        Assert.Same(originalSelected, cache.GetSelectedSlot(1));
        Assert.Equal(6, cache.SelectedSlotCount);
    }

    [Fact]
    public void Radial6_DynamicContentRendersEveryDeclaredSlot()
    {
        RadialVisualPackDefinition pack = RadialVisualPackDefinition.Load(
            RadialVisualPackDefinition.DefaultDirectory);
        using var cache = new RadialDynamicContentCache(
            pack,
            WindowsUiFontResolver.ResolveUiFontFamily());

        Assert.True(cache.Ensure(RadialMenuSettings.Default, targetSize: 280));
        Assert.Equal(6, cache.RenderedSlotCount);
    }

    [Fact]
    public void Radial8_DynamicContentRendersEveryDeclaredSlotWithoutExpandingMappings()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        RadialVisualPackDefinition pack = RadialVisualPackDefinition.Parse(
            temporary.AddRadial8Pack());
        using var cache = new RadialDynamicContentCache(
            pack,
            WindowsUiFontResolver.ResolveUiFontFamily());

        Assert.True(cache.Ensure(RadialMenuSettings.Default, targetSize: 280));
        Assert.Equal(8, cache.RenderedSlotCount);
        Assert.Equal(6, RadialMenuSettings.Default.SlotMappings.Count);
    }

    [Fact]
    public void Radial8_RuntimeSessionUsesEightSlotCaches()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        RadialVisualPackDefinition pack = RadialVisualPackDefinition.Load(
            temporary.AddRadial8Pack());

        using var session = new RadialVisualPackSession(
            pack,
            RadialMenuSettings.Default,
            targetSize: 280);

        Assert.Equal(8, session.AssetCache.SelectedSlotCount);
        Assert.Equal(8, session.Mappings.Count);
        Assert.Equal(8, session.DynamicContent.RenderedSlotCount);
    }

    [Fact]
    public void OverlaySlotBoundsUseBoundLayoutDefinition()
    {
        LayoutDefinition radial6 = RadialVisualPackDefinition.Load(
            RadialVisualPackDefinition.DefaultDirectory).LayoutDefinition;
        using var temporary = new RadialVisualPackTestDirectory();
        LayoutDefinition radial8 = RadialVisualPackDefinition.Parse(
            temporary.AddRadial8Pack()).LayoutDefinition;

        RadialMenuOverlay.ValidateSelectedSlot(6, radial6);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RadialMenuOverlay.ValidateSelectedSlot(7, radial6));

        RadialMenuOverlay.ValidateSelectedSlot(8, radial8);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RadialMenuOverlay.ValidateSelectedSlot(9, radial8));
    }

    private static void AssertSelectedAngles(
        RadialVisualPackCache cache,
        IReadOnlyList<double> expectedAngles)
    {
        Assert.Equal(expectedAngles.Count, cache.SelectedSlotCount);
        for (int index = 0; index < expectedAngles.Count; index++)
        {
            PointF centroid = AlphaCentroid(cache.GetSelectedSlot(index + 1));
            double angle = Math.Atan2(
                centroid.X - (cache.TargetSize / 2d),
                (cache.TargetSize / 2d) - centroid.Y) * (180d / Math.PI);
            if (angle < 0d) angle += 360d;

            double delta = Math.Abs(angle - expectedAngles[index]);
            delta = Math.Min(delta, 360d - delta);
            Assert.InRange(delta, 0d, 1.5d);
        }
    }

    private static PointF AlphaCentroid(Bitmap bitmap)
    {
        double weightedX = 0d;
        double weightedY = 0d;
        long totalAlpha = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                int alpha = bitmap.GetPixel(x, y).A;
                weightedX += x * alpha;
                weightedY += y * alpha;
                totalAlpha += alpha;
            }
        }

        Assert.True(totalAlpha > 0);
        return new PointF(
            (float)(weightedX / totalAlpha),
            (float)(weightedY / totalAlpha));
    }
}
