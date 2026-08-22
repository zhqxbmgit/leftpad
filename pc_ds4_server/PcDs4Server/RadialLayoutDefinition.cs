namespace PcDs4Server;

public readonly record struct LayoutPointDefinition(double X, double Y);

public sealed record LayoutCanvasDefinition(int Width, int Height, string Mode);

public sealed record RadialSlotDefinition(
    int Id,
    double AngleDegrees,
    LayoutPointDefinition GlyphAnchor,
    LayoutPointDefinition LabelAnchor);

public sealed class LayoutDefinition
{
    internal LayoutDefinition(
        string profileId,
        string family,
        int slotCount,
        LayoutCanvasDefinition canvas,
        LayoutPointDefinition wheelCenter,
        string selectionModel,
        string selectionAssetMode,
        IEnumerable<RadialSlotDefinition> slots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionAssetMode);
        ArgumentNullException.ThrowIfNull(slots);

        RadialSlotDefinition[] slotValues = slots.ToArray();
        if (slotCount <= 0 || slotValues.Length != slotCount)
            throw new ArgumentOutOfRangeException(nameof(slotCount));

        ProfileId = profileId;
        Family = family;
        SlotCount = slotCount;
        Canvas = canvas;
        WheelCenter = wheelCenter;
        SelectionModel = selectionModel;
        SelectionAssetMode = selectionAssetMode;
        Slots = Array.AsReadOnly(slotValues);
    }

    public string ProfileId { get; }
    public string Family { get; }
    public int SlotCount { get; }
    public LayoutCanvasDefinition Canvas { get; }
    public LayoutPointDefinition WheelCenter { get; }
    public string SelectionModel { get; }
    public string SelectionAssetMode { get; }
    public IReadOnlyList<RadialSlotDefinition> Slots { get; }
}

internal sealed class LayoutProfileRegistration
{
    public LayoutProfileRegistration(
        string profileId,
        string family,
        string selectionModel,
        bool runtimeSessionSupported,
        IEnumerable<double> expectedAngles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionModel);
        ArgumentNullException.ThrowIfNull(expectedAngles);

        double[] angleValues = expectedAngles.ToArray();
        if (angleValues.Length == 0)
            throw new ArgumentException("A layout profile must contain at least one angle.", nameof(expectedAngles));

        ProfileId = profileId;
        Family = family;
        SelectionModel = selectionModel;
        RuntimeSessionSupported = runtimeSessionSupported;
        ExpectedAngles = Array.AsReadOnly(angleValues);
    }

    public string ProfileId { get; }
    public string Family { get; }
    public int SlotCount => ExpectedAngles.Count;
    public string SelectionModel { get; }
    public bool RuntimeSessionSupported { get; }
    public IReadOnlyList<double> ExpectedAngles { get; }
}

internal static class LayoutProfileRegistry
{
    public const string Radial6ProfileId = "radial-6";
    public const string Radial8ProfileId = "radial-8";
    public const int Radial6SlotCount = 6;
    public const int Radial8SlotCount = 8;

    private const string RadialFamily = "radial";
    private const string AngleSelectionModel = "AngleSelection";

    private static readonly IReadOnlyDictionary<string, LayoutProfileRegistration> Profiles =
        new Dictionary<string, LayoutProfileRegistration>(StringComparer.Ordinal)
        {
            [Radial6ProfileId] = new(
                Radial6ProfileId,
                RadialFamily,
                AngleSelectionModel,
                runtimeSessionSupported: true,
                new[] { 0d, 60d, 120d, 180d, 240d, 300d }),
            [Radial8ProfileId] = new(
                Radial8ProfileId,
                RadialFamily,
                AngleSelectionModel,
                runtimeSessionSupported: false,
                new[] { 0d, 45d, 90d, 135d, 180d, 225d, 270d, 315d })
        };

    public static LayoutProfileRegistration GetRequired(string? profileId)
    {
        if (profileId != null && Profiles.TryGetValue(profileId, out LayoutProfileRegistration? profile))
            return profile;

        throw new InvalidDataException($"Unsupported layout profile: {profileId}");
    }
}
