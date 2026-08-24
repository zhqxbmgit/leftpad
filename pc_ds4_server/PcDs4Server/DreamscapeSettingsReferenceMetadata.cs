using System.Drawing;

namespace PcDs4Server;

internal static class DreamscapeSettingsReferenceMetadata
{
    public const int ReferenceWidth = 1672;
    public const int ReferenceHeight = 941;
    public const string ReferenceSha256 =
        "BE2ACF99E4DDF5CA2FD8544E3ED3271BDD397CF22728D3D65C0ADB6B8B8BDAB8";

    public static Size ReferenceSize => new(ReferenceWidth, ReferenceHeight);

    public static IReadOnlyList<DreamscapeReferenceElement> Elements { get; } =
    [
        new("Navigation rail", 0, 0, 267, 941),
        new("Brand prism", 77, 29, 103, 112),
        new("Brand", 49, 158, 179, 91),
        new("Page title", 352, 68, 153, 74),
        new("Connection status", 1387, 93, 191, 64),
        new("Basic tab", 332, 187, 160, 67),
        new("Advanced tab", 492, 191, 139, 57),
        new("Mappings tab", 631, 191, 180, 57),
        new("Settings surface", 306, 247, 1234, 658),
        new("Left column", 350, 299, 555, 438),
        new("Right column", 963, 299, 501, 374),
        new("Visual pack row", 350, 300, 555, 55),
        new("Receiver scale row", 350, 385, 555, 55),
        new("Overall size row", 350, 477, 545, 61),
        new("Double tap row", 350, 573, 545, 61),
        new("Dead zone row", 350, 669, 545, 61),
        new("Highlight row", 963, 300, 501, 65),
        new("Petal opacity row", 963, 401, 501, 65),
        new("Border opacity row", 963, 501, 501, 65),
        new("Text opacity row", 963, 601, 501, 68),
        new("Preview button", 351, 786, 202, 82),
        new("Hide preview button", 593, 786, 208, 82),
        new("Apply button", 852, 786, 221, 82),
        new("Restore button", 1119, 786, 201, 82),
        new("Right architecture", 1539, 105, 133, 703),
        new("Bottom-left architecture", 0, 548, 306, 393),
        new("Bottom-right portal", 1295, 715, 243, 226),
        new("Moon", 910, 28, 83, 91)
    ];
}
