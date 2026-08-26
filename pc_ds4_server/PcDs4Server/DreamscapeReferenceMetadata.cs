using System.Drawing;

namespace PcDs4Server;

internal sealed record DreamscapeReferenceElement(
    string Name,
    int X,
    int Y,
    int Width,
    int Height);

internal static class DreamscapeReferenceMetadata
{
    public const int ReferenceWidth = DreamscapeShellMetrics.DesignWidth;
    public const int ReferenceHeight = DreamscapeShellMetrics.DesignHeight;
    public const string ReferenceSha256 =
        "4E7D2A19E2FBFEC7E11D7D9E2B396F5EF36644ED2EFF56695261AAAA10265938";

    public static Size ReferenceSize => new(ReferenceWidth, ReferenceHeight);

    public static IReadOnlyList<DreamscapeReferenceElement> Elements { get; } =
    [
        new("Navigation rail", 0, 0, 288, 941),
        new("Brand prism", 88, 49, 106, 112),
        new("Brand text", 66, 178, 166, 82),
        new("Overview navigation", 15, 292, 262, 70),
        new("Gamepad navigation", 43, 390, 196, 49),
        new("Settings navigation", 43, 470, 174, 48),
        new("Log navigation", 43, 549, 164, 48),
        new("Page title", 359, 77, 294, 72),
        new("Connection pill (not present in target)", 0, 0, 0, 0),
        new("Status card 1", 374, 202, 262, 307),
        new("Status card 2", 674, 202, 273, 310),
        new("Status card 3", 985, 202, 261, 307),
        new("Status card 4", 1307, 207, 263, 302),
        new("Output panel", 328, 518, 552, 423),
        new("Mapping panel", 864, 518, 720, 375),
        new("Main stair tower", 1137, 21, 221, 284),
        new("Right dome tower", 1366, 20, 140, 251),
        new("Moon", 1026, 41, 86, 87),
        new("Left foreground tower", 26, 638, 202, 299),
        new("Foreground platform", 0, 730, 872, 211),
        new("Output portal", 495, 750, 196, 116),
        new("Output left crystal", 398, 692, 39, 81),
        new("Output right crystal", 724, 710, 47, 86),
        new("Right foreground crystal", 1597, 635, 69, 96)
    ];
}

internal static class DreamscapeCanvasScale
{
    public static double Calculate(double viewportWidth, double viewportHeight)
    {
        if (viewportWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (viewportHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));

        return Math.Min(
            viewportWidth / DreamscapeReferenceMetadata.ReferenceWidth,
            viewportHeight / DreamscapeReferenceMetadata.ReferenceHeight);
    }
}
