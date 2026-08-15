namespace PcDs4Server;

public sealed record RadialMenuSettings
{
    public const int MinimumScalePercent = 60;
    public const int MaximumScalePercent = 140;
    public const float MinimumGapDegrees = 0f;
    public const float MaximumGapDegrees = 12f;
    public const int MinimumDoubleTapWindowMs = 80;
    public const int MaximumDoubleTapWindowMs = 500;
    public const int MinimumSelectionDeadZone = 8;
    public const int MaximumSelectionDeadZone = 80;
    public const int MinimumSelectionPollIntervalMs = 8;
    public const int MaximumSelectionPollIntervalMs = 50;

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
    public int DoubleTapWindowMs { get; init; } = 150;
    public int SelectionDeadZone { get; init; } = 28;
    public int HighlightAlpha { get; init; } = 80;
    public int SelectionPollIntervalMs { get; init; } = 16;
    public RadialSlotMappings SlotMappings { get; init; } = RadialSlotMappings.Default;

    public bool TryValidate(out string error)
    {
        if (ScalePercent is < MinimumScalePercent or > MaximumScalePercent)
            return Invalid($"整体大小必须在 {MinimumScalePercent}%～{MaximumScalePercent}% 之间。", out error);
        if (BaseCanvasSize < 160 || BaseCanvasSize > 800)
            return Invalid("画布大小必须在 160～800 px 之间。", out error);
        if (HubRadius <= 0)
            return Invalid("中心圆半径必须大于 0。", out error);
        if (PetalInnerRadius <= HubRadius)
            return Invalid("中心圆半径必须小于花瓣内半径。", out error);
        if (PetalOuterRadius <= PetalInnerRadius)
            return Invalid("花瓣内半径必须小于花瓣外半径。", out error);
        if (PetalOuterRadius >= BaseCanvasSize / 2f)
            return Invalid("花瓣外半径必须小于画布半径。", out error);
        if (TextRadius < PetalInnerRadius || TextRadius > PetalOuterRadius)
            return Invalid("文字位置半径必须位于花瓣内半径和外半径之间。", out error);
        if (PetalGapDegrees is < MinimumGapDegrees or > MaximumGapDegrees)
            return Invalid($"花瓣间隔角度必须在 {MinimumGapDegrees:0}°～{MaximumGapDegrees:0}° 之间。", out error);
        if (!float.IsFinite(FontSize) || FontSize is < 6f or > 48f)
            return Invalid("字体大小必须在 6～48 px 之间。", out error);
        if (!ValidAlpha(FillAlpha) || !ValidAlpha(BorderAlpha) || !ValidAlpha(TextAlpha))
            return Invalid("透明度数值必须在 0～255 之间。", out error);
        if (DoubleTapWindowMs is < MinimumDoubleTapWindowMs or > MaximumDoubleTapWindowMs)
            return Invalid($"环形菜单双击窗口必须在 {MinimumDoubleTapWindowMs}～{MaximumDoubleTapWindowMs} ms 之间。", out error);
        if (SelectionDeadZone is < MinimumSelectionDeadZone or > MaximumSelectionDeadZone)
            return Invalid($"选择死区必须在 {MinimumSelectionDeadZone}～{MaximumSelectionDeadZone} px 之间。", out error);
        if (!ValidAlpha(HighlightAlpha))
            return Invalid("选择高亮强度必须在 0～255 之间。", out error);
        if (SelectionPollIntervalMs is < MinimumSelectionPollIntervalMs or > MaximumSelectionPollIntervalMs)
            return Invalid($"选择检测间隔必须在 {MinimumSelectionPollIntervalMs}～{MaximumSelectionPollIntervalMs} ms 之间。", out error);
        if (SlotMappings == null)
            return Invalid("动作映射不能为空。", out error);
        if (!SlotMappings.TryValidate(out error)) return false;

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
