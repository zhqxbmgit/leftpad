namespace PcDs4Server;

internal static class RadialDpiScaling
{
    public const int DefaultDpi = 96;

    public static double GetScale(int dpi) => NormalizeDpi(dpi) / (double)DefaultDpi;

    public static int ToPhysicalPixels(int logicalPixels, int dpi)
    {
        if (logicalPixels <= 0) throw new ArgumentOutOfRangeException(nameof(logicalPixels));
        return checked((int)Math.Round(
            logicalPixels * GetScale(dpi),
            MidpointRounding.AwayFromZero));
    }

    public static int LogicalEdgeToPhysical(double logicalValue, int dpi)
    {
        if (!double.IsFinite(logicalValue))
            throw new ArgumentOutOfRangeException(nameof(logicalValue));
        return checked((int)Math.Round(
            logicalValue * GetScale(dpi),
            MidpointRounding.AwayFromZero));
    }

    public static Size LogicalSizeToPhysical(double logicalWidth, double logicalHeight, int dpi)
    {
        if (!double.IsFinite(logicalWidth) || logicalWidth <= 0d ||
            !double.IsFinite(logicalHeight) || logicalHeight <= 0d)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        return new Size(LogicalEdgeToPhysical(logicalWidth, dpi),
            LogicalEdgeToPhysical(logicalHeight, dpi));
    }

    public static Rectangle LogicalRectToPhysical(
        double left, double top, double right, double bottom, int dpi)
    {
        if (right < left || bottom < top) throw new ArgumentOutOfRangeException(nameof(right));
        int physicalLeft = LogicalEdgeToPhysical(left, dpi);
        int physicalTop = LogicalEdgeToPhysical(top, dpi);
        int physicalRight = LogicalEdgeToPhysical(right, dpi);
        int physicalBottom = LogicalEdgeToPhysical(bottom, dpi);
        return Rectangle.FromLTRB(physicalLeft, physicalTop, physicalRight, physicalBottom);
    }

    public static Point CenterAt(Point physicalCenter, int physicalSize)
    {
        if (physicalSize <= 0) throw new ArgumentOutOfRangeException(nameof(physicalSize));
        return new Point(
            physicalCenter.X - (physicalSize / 2),
            physicalCenter.Y - (physicalSize / 2));
    }

    private static int NormalizeDpi(int dpi) => dpi > 0 ? dpi : DefaultDpi;
}
