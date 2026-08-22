using System.Drawing;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialSelectionEngineTests
{
    private static readonly LayoutDefinition Radial6 = RadialVisualPackDefinition.Load(
        RadialVisualPackDefinition.DefaultDirectory).LayoutDefinition;

    private static readonly LayoutDefinition Radial8 = CreateLayout(
        "radial-8",
        0d,
        45d,
        90d,
        135d,
        180d,
        225d,
        270d,
        315d);

    [Fact]
    public void CenterAndDeadZoneBoundary_ReturnNoSelection()
    {
        var anchor = new Point(100, 100);

        Assert.Equal(0, RadialSelectionEngine.GetSelectedSlot(Radial6, anchor, anchor, 28));
        Assert.Equal(0, RadialSelectionEngine.GetSelectedSlot(
            Radial6,
            anchor,
            new Point(100, 127),
            28));
        Assert.Equal(0, RadialSelectionEngine.GetSelectedSlot(
            Radial6,
            anchor,
            new Point(100, 128),
            28));
    }

    [Theory]
    [InlineData(0, -29, 1)]
    [InlineData(29, -29, 2)]
    [InlineData(29, 29, 3)]
    [InlineData(0, 29, 4)]
    [InlineData(-29, 29, 5)]
    [InlineData(-29, -29, 6)]
    public void Radial6_DirectionsMapClockwiseFromUp(
        int deltaX,
        int deltaY,
        int expectedSlot)
    {
        Assert.Equal(
            expectedSlot,
            RadialSelectionEngine.GetSelectedSlot(Radial6, deltaX, deltaY, 28));
    }

    [Fact]
    public void FarOutsideVisualRadius_StillSelectsByDirection()
    {
        Assert.Equal(5, RadialSelectionEngine.GetSelectedSlot(Radial6, -2500, 2500, 28));
    }

    [Theory]
    [InlineData(29.999, 1)]
    [InlineData(30.0, 2)]
    [InlineData(89.999, 2)]
    [InlineData(90.0, 3)]
    [InlineData(149.999, 3)]
    [InlineData(150.0, 4)]
    [InlineData(209.999, 4)]
    [InlineData(210.0, 5)]
    [InlineData(269.999, 5)]
    [InlineData(270.0, 6)]
    [InlineData(329.999, 6)]
    [InlineData(330.0, 1)]
    [InlineData(359.0, 1)]
    [InlineData(360.0, 1)]
    [InlineData(0.0, 1)]
    [InlineData(1.0, 1)]
    public void Radial6_SectorBoundariesPreserveStableHalfOpenIntervals(
        double angleDegrees,
        int expectedSlot)
    {
        AssertSelectedAtAngle(Radial6, angleDegrees, expectedSlot);
    }

    [Theory]
    [InlineData(0d, 1)]
    [InlineData(45d, 2)]
    [InlineData(90d, 3)]
    [InlineData(135d, 4)]
    [InlineData(180d, 5)]
    [InlineData(225d, 6)]
    [InlineData(270d, 7)]
    [InlineData(315d, 8)]
    public void Radial8_CenterAnglesSelectDeclaredSlots(
        double angleDegrees,
        int expectedSlot)
    {
        AssertSelectedAtAngle(Radial8, angleDegrees, expectedSlot);
    }

    [Theory]
    [InlineData(22.499, 1)]
    [InlineData(22.5, 2)]
    [InlineData(67.499, 2)]
    [InlineData(67.5, 3)]
    [InlineData(112.499, 3)]
    [InlineData(112.5, 4)]
    [InlineData(157.499, 4)]
    [InlineData(157.5, 5)]
    [InlineData(202.499, 5)]
    [InlineData(202.5, 6)]
    [InlineData(247.499, 6)]
    [InlineData(247.5, 7)]
    [InlineData(292.499, 7)]
    [InlineData(292.5, 8)]
    [InlineData(337.499, 8)]
    [InlineData(337.5, 1)]
    public void Radial8_BoundariesUseClockwiseHalfOpenTieBreak(
        double angleDegrees,
        int expectedSlot)
    {
        AssertSelectedAtAngle(Radial8, angleDegrees, expectedSlot);
    }

    [Fact]
    public void EverySampledAngle_SelectsExactlyOneDeclaredSlot()
    {
        foreach (LayoutDefinition layout in new[] { Radial6, Radial8 })
        {
            for (int sample = 0; sample < 3600; sample++)
            {
                double angle = sample / 10d;
                int selectedSlot = SelectAtAngle(layout, angle);

                Assert.InRange(selectedSlot, 1, layout.SlotCount);
                Assert.Equal(1, layout.Slots.Count(slot => slot.Id == selectedSlot));
            }
        }
    }

    private static void AssertSelectedAtAngle(
        LayoutDefinition layout,
        double angleDegrees,
        int expectedSlot) =>
        Assert.Equal(expectedSlot, SelectAtAngle(layout, angleDegrees));

    private static int SelectAtAngle(LayoutDefinition layout, double angleDegrees)
    {
        const double radius = 1000.0;
        double radians = angleDegrees * (Math.PI / 180.0);
        double deltaX = Math.Sin(radians) * radius;
        double deltaY = -Math.Cos(radians) * radius;
        return RadialSelectionEngine.GetSelectedSlot(layout, deltaX, deltaY, 28);
    }

    private static LayoutDefinition CreateLayout(string profileId, params double[] angles) =>
        new(
            profileId,
            "radial",
            angles.Length,
            new LayoutCanvasDefinition(1254, 1254, "RGBA"),
            new LayoutPointDefinition(627d, 627d),
            "AngleSelection",
            "canonical-transform",
            angles.Select((angle, index) => new RadialSlotDefinition(
                index + 1,
                angle,
                new LayoutPointDefinition(627d, 627d),
                new LayoutPointDefinition(627d, 627d))));
}
