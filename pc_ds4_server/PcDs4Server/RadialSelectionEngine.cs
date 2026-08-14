using System.Drawing;

namespace PcDs4Server;

public static class RadialSelectionEngine
{
    private const double DegreesPerSlot = 60.0;
    private const double HalfSlotDegrees = DegreesPerSlot / 2.0;

    public static int GetSelectedSlot(Point anchor, Point cursor, double deadZoneRadius) =>
        GetSelectedSlot(cursor.X - (double)anchor.X, cursor.Y - (double)anchor.Y, deadZoneRadius);

    public static int GetSelectedSlot(double deltaX, double deltaY, double deadZoneRadius)
    {
        if (!double.IsFinite(deltaX)) throw new ArgumentOutOfRangeException(nameof(deltaX));
        if (!double.IsFinite(deltaY)) throw new ArgumentOutOfRangeException(nameof(deltaY));
        if (!double.IsFinite(deadZoneRadius) || deadZoneRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(deadZoneRadius));

        double distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (distanceSquared <= deadZoneRadius * deadZoneRadius) return 0;

        double angle = Math.Atan2(deltaX, -deltaY) * (180.0 / Math.PI);
        if (angle < 0) angle += 360.0;

        // Stabilize exact mathematical boundaries produced through sin/cos test inputs.
        angle = Math.Round(angle, 10, MidpointRounding.AwayFromZero);
        if (angle >= 360.0) angle -= 360.0;

        int slotIndex = (int)Math.Floor((angle + HalfSlotDegrees) / DegreesPerSlot) % 6;
        return slotIndex + 1;
    }
}
