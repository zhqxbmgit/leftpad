using System.Text.Json.Serialization;

namespace PcDs4Server;

public sealed record RadialMenuSettings
{
    private RadialMappingsByProfile _mappingsByProfile = RadialMappingsByProfile.Empty;

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

    public string VisualPackId { get; init; } = RadialVisualPackContract.DefaultVisualPackId;
    public int ReceiverUiScalePercent { get; init; } = ReceiverUiScaling.DefaultScalePercent;
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
    public string MappingProfileId { get; init; } = LayoutProfileRegistry.Radial6ProfileId;

    public RadialMappingsByProfile MappingsByProfile
    {
        get => _mappingsByProfile;
        init => _mappingsByProfile = value ?? RadialMappingsByProfile.Empty;
    }

    // Source compatibility for radial-v5 callers. New code should use the profile APIs.
    [JsonIgnore]
    public RadialSlotMappings SlotMappings
    {
        get => GetProfileMappings(LayoutProfileRegistry.Radial6ProfileId);
        init => _mappingsByProfile = _mappingsByProfile.Set(
            LayoutProfileRegistry.Radial6ProfileId,
            NormalizeForProfile(LayoutProfileRegistry.Radial6ProfileId, value));
    }

    // Read-only migration input for the pre-profile JSON schema. Normalized settings never write it.
    [JsonPropertyName("slotMappings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RadialSlotMappings? LegacySlotMappings
    {
        get => null;
        init
        {
            if (value != null)
            {
                _mappingsByProfile = _mappingsByProfile.Set(
                    LayoutProfileRegistry.Radial6ProfileId,
                    NormalizeForProfile(LayoutProfileRegistry.Radial6ProfileId, value));
            }
        }
    }

    public RadialSlotMappings GetProfileMappings(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (MappingsByProfile.TryGetValue(profileId, out RadialSlotMappings mappings))
            return mappings;

        return TryGetProfileSlotCount(profileId, out int slotCount)
            ? RadialSlotMappings.Create(slotCount)
            : RadialSlotMappings.Empty;
    }

    public RadialMenuSettings SetProfileMappings(
        string profileId,
        RadialSlotMappings mappings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(mappings);
        return this with
        {
            MappingsByProfile = MappingsByProfile.Set(
                profileId,
                NormalizeForProfile(profileId, mappings)),
            LegacySlotMappings = null
        };
    }

    public RadialMenuSettings NormalizeMappings()
    {
        RadialMappingsByProfile normalized = RadialMappingsByProfile.Empty;
        foreach ((string profileId, RadialSlotMappings mappings) in MappingsByProfile)
            normalized = normalized.Set(profileId, NormalizeForProfile(profileId, mappings));

        return this with
        {
            MappingsByProfile = normalized,
            LegacySlotMappings = null
        };
    }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(VisualPackId))
            return Invalid("视觉主题 ID 不能为空。", out error);
        if (!ReceiverUiScaling.IsPreset(ReceiverUiScalePercent))
            return Invalid("接收器界面缩放必须为 100%、125%、150%、175% 或 200%。", out error);
        if (ScalePercent is < MinimumScalePercent or > MaximumScalePercent)
            return Invalid($"环形菜单整体大小必须在 {MinimumScalePercent}%～{MaximumScalePercent}% 之间。", out error);
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
        if (string.IsNullOrWhiteSpace(MappingProfileId))
            return Invalid("动作映射布局 Profile ID 不能为空。", out error);
        if (MappingsByProfile == null)
            return Invalid("按布局隔离的动作映射不能为空。", out error);
        foreach ((string profileId, RadialSlotMappings mappings) in MappingsByProfile)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return Invalid("动作映射布局 Profile ID 不能为空。", out error);
            if (TryGetProfileSlotCount(profileId, out int slotCount) && mappings.Count != slotCount)
                return Invalid($"布局 {profileId} 必须包含 {slotCount} 个动作映射。", out error);
            if (!mappings.TryValidate(out string mappingError))
                return Invalid($"布局 {profileId}：{mappingError}", out error);
        }

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

    private static RadialSlotMappings NormalizeForProfile(
        string profileId,
        RadialSlotMappings? mappings)
    {
        RadialSlotMappings safeMappings = mappings ?? RadialSlotMappings.Empty;
        return TryGetProfileSlotCount(profileId, out int slotCount)
            ? safeMappings.Normalize(slotCount)
            : RadialSlotMappings.Sanitize(safeMappings);
    }

    private static bool TryGetProfileSlotCount(string profileId, out int slotCount)
    {
        try
        {
            slotCount = LayoutProfileRegistry.GetRequired(profileId).SlotCount;
            return true;
        }
        catch (InvalidDataException)
        {
            slotCount = 0;
            return false;
        }
    }

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
