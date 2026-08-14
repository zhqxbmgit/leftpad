using System.Drawing;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialSelectionEngineTests
{
    [Fact]
    public void CenterAndDeadZoneBoundary_ReturnNoSelection()
    {
        var anchor = new Point(100, 100);

        Assert.Equal(0, RadialSelectionEngine.GetSelectedSlot(anchor, anchor, 28));
        Assert.Equal(0, RadialSelectionEngine.GetSelectedSlot(anchor, new Point(100, 127), 28));
        Assert.Equal(0, RadialSelectionEngine.GetSelectedSlot(anchor, new Point(100, 128), 28));
    }

    [Theory]
    [InlineData(0, -29, 1)]
    [InlineData(29, -29, 2)]
    [InlineData(29, 29, 3)]
    [InlineData(0, 29, 4)]
    [InlineData(-29, 29, 5)]
    [InlineData(-29, -29, 6)]
    public void SixDirections_MapClockwiseFromUp(int deltaX, int deltaY, int expectedSlot)
    {
        Assert.Equal(expectedSlot, RadialSelectionEngine.GetSelectedSlot(deltaX, deltaY, 28));
    }

    [Fact]
    public void FarOutsideVisualRadius_StillSelectsByDirection()
    {
        Assert.Equal(5, RadialSelectionEngine.GetSelectedSlot(-2500, 2500, 28));
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
    public void SectorBoundaries_UseStableHalfOpenIntervals(double angleDegrees, int expectedSlot)
    {
        const double radius = 1000.0;
        double radians = angleDegrees * (Math.PI / 180.0);
        double deltaX = Math.Sin(radians) * radius;
        double deltaY = -Math.Cos(radians) * radius;

        Assert.Equal(expectedSlot, RadialSelectionEngine.GetSelectedSlot(deltaX, deltaY, 28));
    }
}
