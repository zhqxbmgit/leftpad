using System.Drawing;

namespace PcDs4Server;

public static class RadialSelectionEngine
{
    private const double FullCircleDegrees = 360.0;
    private const double TieTolerance = 0.000000001;

    public static int GetSelectedSlot(
        LayoutDefinition layout,
        Point anchor,
        Point cursor,
        double deadZoneRadius) =>
        GetSelectedSlot(
            layout,
            cursor.X - (double)anchor.X,
            cursor.Y - (double)anchor.Y,
            deadZoneRadius);

    public static int GetSelectedSlot(
        LayoutDefinition layout,
        double deltaX,
        double deltaY,
        double deadZoneRadius)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!double.IsFinite(deltaX)) throw new ArgumentOutOfRangeException(nameof(deltaX));
        if (!double.IsFinite(deltaY)) throw new ArgumentOutOfRangeException(nameof(deltaY));
        if (!double.IsFinite(deadZoneRadius) || deadZoneRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(deadZoneRadius));

        double distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (distanceSquared <= deadZoneRadius * deadZoneRadius) return 0;

        double angle = Math.Atan2(deltaX, -deltaY) * (FullCircleDegrees / (2.0 * Math.PI));
        angle = NormalizeDegrees(angle);

        // Stabilize exact mathematical boundaries produced through sin/cos test inputs.
        angle = Math.Round(angle, 10, MidpointRounding.AwayFromZero);
        if (angle >= FullCircleDegrees) angle -= FullCircleDegrees;

        RadialSlotDefinition? selected = null;
        double selectedDistance = double.PositiveInfinity;
        double selectedClockwiseDistance = double.PositiveInfinity;
        foreach (RadialSlotDefinition slot in layout.Slots)
        {
            double clockwiseDistance = NormalizeDegrees(slot.AngleDegrees - angle);
            double counterClockwiseDistance = FullCircleDegrees - clockwiseDistance;
            double distance = Math.Min(clockwiseDistance, counterClockwiseDistance);

            bool isCloser = distance < selectedDistance - TieTolerance;
            bool isClockwiseTie = Math.Abs(distance - selectedDistance) <= TieTolerance &&
                clockwiseDistance < selectedClockwiseDistance;
            if (!isCloser && !isClockwiseTie) continue;

            selected = slot;
            selectedDistance = distance;
            selectedClockwiseDistance = clockwiseDistance;
        }

        return selected?.Id ?? throw new InvalidDataException(
            $"Layout profile '{layout.ProfileId}' does not contain any radial slots.");
    }

    private static double NormalizeDegrees(double angle)
    {
        double normalized = angle % FullCircleDegrees;
        return normalized < 0.0 ? normalized + FullCircleDegrees : normalized;
    }
}
