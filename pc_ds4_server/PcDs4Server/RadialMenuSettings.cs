namespace PcDs4Server;

public sealed record RadialMenuSettings
{
    public const int MinimumScalePercent = 60;
    public const int MaximumScalePercent = 140;
    public const float MinimumGapDegrees = 0f;
    public const float MaximumGapDegrees = 12f;

    public static RadialMenuSettings Default => new();

    public int ScalePercent { get; init; } = 100;
    public int BaseCanvasSize { get; init; } = 280;
    public int HubRadius { get; init; } = 35;
    public int PetalInnerRadius { get; init; } = 42;
    public int PetalOuterRadius { get; init; } = 103;
    public int TextRadius { get; init; } = 73;
    public float PetalGapDegrees { get; init; } = 4f;
    public float FontSize { get; init; } = 15f;
    public int FillAlpha { get; init; } = 218;
    public int BorderAlpha { get; init; } = 100;
    public int TextAlpha { get; init; } = 240;

    public bool TryValidate(out string error)
    {
        if (ScalePercent is < MinimumScalePercent or > MaximumScalePercent)
            return Invalid($"Overall size must be between {MinimumScalePercent}% and {MaximumScalePercent}%.", out error);
        if (BaseCanvasSize < 160 || BaseCanvasSize > 800)
            return Invalid("Base canvas size must be between 160 and 800 pixels.", out error);
        if (HubRadius <= 0)
            return Invalid("Hub radius must be greater than zero.", out error);
        if (PetalInnerRadius <= HubRadius)
            return Invalid("Petal inner radius must be greater than hub radius.", out error);
        if (PetalOuterRadius <= PetalInnerRadius)
            return Invalid("Petal outer radius must be greater than petal inner radius.", out error);
        if (PetalOuterRadius >= BaseCanvasSize / 2f)
            return Invalid("Petal outer radius must fit inside half of the base canvas.", out error);
        if (TextRadius < PetalInnerRadius || TextRadius > PetalOuterRadius)
            return Invalid("Text radius must be between the petal inner and outer radii.", out error);
        if (PetalGapDegrees is < MinimumGapDegrees or > MaximumGapDegrees)
            return Invalid($"Petal gap must be between {MinimumGapDegrees:0} and {MaximumGapDegrees:0} degrees.", out error);
        if (!float.IsFinite(FontSize) || FontSize is < 6f or > 48f)
            return Invalid("Font size must be between 6 and 48 pixels.", out error);
        if (!ValidAlpha(FillAlpha) || !ValidAlpha(BorderAlpha) || !ValidAlpha(TextAlpha))
            return Invalid("Opacity values must be between 0 and 255.", out error);

        error = string.Empty;
        return true;
    }

    public RadialMenuRenderMetrics CreateRenderMetrics()
    {
        if (!TryValidate(out string error)) throw new ArgumentException(error, nameof(RadialMenuSettings));

        float scale = ScalePercent / 100f;
        int canvasSize = (int)MathF.Round(BaseCanvasSize * scale);
        if ((canvasSize & 1) != 0) canvasSize++;

        return new RadialMenuRenderMetrics(
            canvasSize,
            HubRadius * scale,
            PetalInnerRadius * scale,
            PetalOuterRadius * scale,
            TextRadius * scale,
            60f - PetalGapDegrees,
            FontSize * scale,
            scale);
    }

    private static bool ValidAlpha(int value) => value is >= byte.MinValue and <= byte.MaxValue;

    private static bool Invalid(string message, out string error)
    {
        error = message;
        return false;
    }
}

public readonly record struct RadialMenuRenderMetrics(
    int CanvasSize,
    float HubRadius,
    float PetalInnerRadius,
    float PetalOuterRadius,
    float TextRadius,
    float PetalSweepAngle,
    float FontSize,
    float ScaleFactor);
