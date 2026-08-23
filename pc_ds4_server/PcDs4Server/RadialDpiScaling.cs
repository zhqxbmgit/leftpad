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

    public static Point CenterAt(Point physicalCenter, int physicalSize)
    {
        if (physicalSize <= 0) throw new ArgumentOutOfRangeException(nameof(physicalSize));
        return new Point(
            physicalCenter.X - (physicalSize / 2),
            physicalCenter.Y - (physicalSize / 2));
    }

    private static int NormalizeDpi(int dpi) => dpi > 0 ? dpi : DefaultDpi;
}
