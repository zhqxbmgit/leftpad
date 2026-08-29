using System.Collections.ObjectModel;

namespace PcDs4Server;

public enum RadialSettingsSemanticRevision
{
    Legacy = 1,
    Universal = 2
}

internal sealed record UniversalRadialParameters
{
    public const double MinimumSurfaceScale = 12d / 35d;
    public const double MaximumSurfaceScale = 4d;
    public const double NeutralFontSize = 15d;

    public UniversalRadialParameters(
        RadialSettingsSemanticRevision semanticRevision,
        double surfaceScale,
        double hubRadiusCru,
        double innerRadiusCru,
        double outerRadiusCru,
        double gapDegrees,
        double slotContentRadiusCru,
        double fontScale,
        byte fillStrength,
        byte borderStrength,
        byte textStrength,
        byte highlightStrength)
    {
        if (!Enum.IsDefined(semanticRevision))
            throw new ArgumentOutOfRangeException(nameof(semanticRevision));
        RequireFiniteRange(surfaceScale, MinimumSurfaceScale, MaximumSurfaceScale, nameof(surfaceScale));
        if (!double.IsFinite(hubRadiusCru) || !double.IsFinite(innerRadiusCru) ||
            !double.IsFinite(outerRadiusCru) ||
            !(0d < hubRadiusCru && hubRadiusCru < innerRadiusCru && innerRadiusCru < outerRadiusCru))
            throw new ArgumentOutOfRangeException(nameof(hubRadiusCru), "Universal radii must satisfy 0 < H < I < O.");
        RequireFiniteRange(gapDegrees, 0d, 12d, nameof(gapDegrees));
        if (!double.IsFinite(slotContentRadiusCru) ||
            slotContentRadiusCru < innerRadiusCru || slotContentRadiusCru > outerRadiusCru)
            throw new ArgumentOutOfRangeException(nameof(slotContentRadiusCru));
        RequireFiniteRange(fontScale, 6d / NeutralFontSize, 48d / NeutralFontSize, nameof(fontScale));

        SemanticRevision = semanticRevision;
        SurfaceScale = surfaceScale;
        HubRadiusCru = hubRadiusCru;
        InnerRadiusCru = innerRadiusCru;
        OuterRadiusCru = outerRadiusCru;
        GapDegrees = gapDegrees;
        SlotContentRadiusCru = slotContentRadiusCru;
        FontScale = fontScale;
        FillStrength = fillStrength;
        BorderStrength = borderStrength;
        TextStrength = textStrength;
        HighlightStrength = highlightStrength;
    }

    public static UniversalRadialParameters Neutral { get; } = new(
        RadialSettingsSemanticRevision.Universal,
        surfaceScale: 1d,
        hubRadiusCru: 35d,
        innerRadiusCru: 42d,
        outerRadiusCru: 103d,
        gapDegrees: 4d,
        slotContentRadiusCru: 73d,
        fontScale: 1d,
        fillStrength: byte.MaxValue,
        borderStrength: byte.MaxValue,
        textStrength: byte.MaxValue,
        highlightStrength: byte.MaxValue);

    public RadialSettingsSemanticRevision SemanticRevision { get; }
    public double SurfaceScale { get; }
    public double HubRadiusCru { get; }
    public double InnerRadiusCru { get; }
    public double OuterRadiusCru { get; }
    public double GapDegrees { get; }
    public double SlotContentRadiusCru { get; }
    public double FontScale { get; }
    public byte FillStrength { get; }
    public byte BorderStrength { get; }
    public byte TextStrength { get; }
    public byte HighlightStrength { get; }

    public void ValidateForLayout(LayoutDefinition layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ValidateForSlotCount(layout.SlotCount);
    }

    public void ValidateForSlotCount(int slotCount)
    {
        if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
        if (GapDegrees >= 360d / slotCount)
            throw new ArgumentOutOfRangeException(nameof(GapDegrees), "The visual gap must be smaller than the slot pitch.");
    }

    private static void RequireFiniteRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }
}

internal static class UniversalRadialSettingsNormalizer
{
    public static UniversalRadialParameters Normalize(RadialMenuSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Normalize(settings, settings.SettingsSemanticRevision ?? RadialSettingsSemanticRevision.Legacy);
    }

    public static UniversalRadialParameters Normalize(
        RadialMenuSettings settings,
        RadialSettingsSemanticRevision revision)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(revision)) throw new ArgumentOutOfRangeException(nameof(revision));
        ValidateVisualInputs(settings, revision);

        double surfaceScale = revision == RadialSettingsSemanticRevision.Legacy
            ? (settings.BaseCanvasSize / 280d) * (settings.ScalePercent / 100d)
            : settings.ScalePercent / 100d;
        var result = new UniversalRadialParameters(
            revision,
            surfaceScale,
            settings.HubRadius,
            settings.PetalInnerRadius,
            settings.PetalOuterRadius,
            settings.PetalGapDegrees,
            settings.TextRadius,
            settings.FontSize / UniversalRadialParameters.NeutralFontSize,
            revision == RadialSettingsSemanticRevision.Legacy
                ? NormalizeLegacyStrength(settings.FillAlpha, 218)
                : checked((byte)settings.FillAlpha),
            revision == RadialSettingsSemanticRevision.Legacy
                ? NormalizeLegacyStrength(settings.BorderAlpha, 100)
                : checked((byte)settings.BorderAlpha),
            revision == RadialSettingsSemanticRevision.Legacy
                ? NormalizeLegacyTextStrength(settings.TextAlpha)
                : checked((byte)settings.TextAlpha),
            revision == RadialSettingsSemanticRevision.Legacy
                ? NormalizeLegacyStrength(settings.HighlightAlpha, 80)
                : checked((byte)settings.HighlightAlpha));
        result.ValidateForSlotCount(LayoutProfileRegistry.GetRequired(settings.MappingProfileId).SlotCount);
        return result;
    }

    private static void ValidateVisualInputs(
        RadialMenuSettings settings,
        RadialSettingsSemanticRevision revision)
    {
        if (revision == RadialSettingsSemanticRevision.Legacy)
        {
            if (settings.BaseCanvasSize is < 160 or > 800)
                throw new ArgumentOutOfRangeException(nameof(settings.BaseCanvasSize));
            if (settings.ScalePercent is < RadialMenuSettings.MinimumScalePercent or
                > RadialMenuSettings.MaximumScalePercent)
                throw new ArgumentOutOfRangeException(nameof(settings.ScalePercent));
        }
        else
        {
            double scale = settings.ScalePercent / 100d;
            if (scale < UniversalRadialParameters.MinimumSurfaceScale ||
                scale > UniversalRadialParameters.MaximumSurfaceScale)
                throw new ArgumentOutOfRangeException(nameof(settings.ScalePercent));
        }

        if (settings.HubRadius <= 0 ||
            settings.PetalInnerRadius <= settings.HubRadius ||
            settings.PetalOuterRadius <= settings.PetalInnerRadius)
            throw new ArgumentOutOfRangeException(nameof(settings.HubRadius));
        if (settings.TextRadius < settings.PetalInnerRadius ||
            settings.TextRadius > settings.PetalOuterRadius)
            throw new ArgumentOutOfRangeException(nameof(settings.TextRadius));
        if (!float.IsFinite(settings.PetalGapDegrees) ||
            settings.PetalGapDegrees is < 0f or > 12f)
            throw new ArgumentOutOfRangeException(nameof(settings.PetalGapDegrees));
        if (!float.IsFinite(settings.FontSize) || settings.FontSize is < 6f or > 48f)
            throw new ArgumentOutOfRangeException(nameof(settings.FontSize));
        if (settings.FillAlpha is < byte.MinValue or > byte.MaxValue ||
            settings.BorderAlpha is < byte.MinValue or > byte.MaxValue ||
            settings.TextAlpha is < byte.MinValue or > byte.MaxValue ||
            settings.HighlightAlpha is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(settings.FillAlpha));
    }

    internal static byte NormalizeLegacyStrength(int oldValue, int oldDefault)
    {
        if (oldValue is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(oldValue));
        if (oldDefault is <= 0 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(oldDefault));
        return oldValue <= oldDefault
            ? RoundStrength(oldValue, oldDefault)
            : byte.MaxValue;
    }

    internal static byte NormalizeLegacyTextStrength(int oldTextAlpha)
    {
        if (oldTextAlpha is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(oldTextAlpha));
        return RoundStrength(Math.Min(oldTextAlpha, 235), 235);
    }

    internal static byte ScaleAuthoredAlpha(byte authoredAlpha, byte strength) =>
        checked((byte)Math.Round(
            authoredAlpha * strength / 255d,
            MidpointRounding.AwayFromZero));

    private static byte RoundStrength(int value, int denominator) =>
        checked((byte)Math.Round(
            byte.MaxValue * value / (double)denominator,
            MidpointRounding.AwayFromZero));
}

internal readonly record struct UniversalLogicalSize(double Width, double Height)
{
    public UniversalLogicalSize Scale(double factor) => new(Width * factor, Height * factor);
}

internal enum UniversalRadialRenderModel
{
    LegacyCanonicalSelected,
    FullStateFrame
}

internal enum UniversalDynamicContentGroupRole
{
    OuterSlotContent,
    SelectedCenterContent
}

internal sealed record UniversalDynamicContentMember(
    string Id,
    string LayerId,
    string Role,
    NormalizedReferenceBounds AuthoredBounds,
    double LocalOffsetX,
    double LocalOffsetY,
    double Rotation,
    string HorizontalAlignment,
    string VerticalAlignment,
    string OverflowPolicy);

internal sealed class UniversalDynamicContentGroup
{
    public UniversalDynamicContentGroup(
        string identity,
        UniversalDynamicContentGroupRole role,
        int? slotId,
        NormalizedPoint authoredGroupOrigin,
        double authoredRadialPosition,
        double? slotCenterAngleDegrees,
        NormalizedPoint radialUnitVector,
        IEnumerable<UniversalDynamicContentMember> members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(authoredGroupOrigin);
        ArgumentNullException.ThrowIfNull(radialUnitVector);
        ArgumentNullException.ThrowIfNull(members);
        UniversalDynamicContentMember[] values = members.ToArray();
        if (values.Length == 0) throw new ArgumentException("A dynamic content group requires members.", nameof(members));
        if (role == UniversalDynamicContentGroupRole.OuterSlotContent &&
            (!slotId.HasValue || !slotCenterAngleDegrees.HasValue))
            throw new ArgumentException("Outer slot groups require slot identity and angle.");
        if (role == UniversalDynamicContentGroupRole.SelectedCenterContent && slotId.HasValue)
            throw new ArgumentException("Selected-center groups cannot own a slot.", nameof(slotId));

        Identity = identity;
        Role = role;
        SlotId = slotId;
        AuthoredGroupOrigin = authoredGroupOrigin;
        AuthoredRadialPosition = authoredRadialPosition;
        SlotCenterAngleDegrees = slotCenterAngleDegrees;
        RadialUnitVector = radialUnitVector;
        Members = Array.AsReadOnly(values);
    }

    public string Identity { get; }
    public UniversalDynamicContentGroupRole Role { get; }
    public int? SlotId { get; }
    public NormalizedPoint AuthoredGroupOrigin { get; }
    public double AuthoredRadialPosition { get; }
    public double? SlotCenterAngleDegrees { get; }
    public NormalizedPoint RadialUnitVector { get; }
    public ReadOnlyCollection<UniversalDynamicContentMember> Members { get; }
}

internal readonly record struct UniversalDynamicTranslation(double X, double Y)
{
    public static UniversalDynamicTranslation Zero { get; } = new(0d, 0d);
    public double Magnitude => Math.Sqrt(X * X + Y * Y);
}

internal static class UniversalDynamicContentTransform
{
    public const double NeutralSlotContentRadiusCru = 73d;
    public const double CanonicalRadialDesignWidthCru = 280d;

    public static NormalizedPoint RadialUnitVector(double angleDegrees)
    {
        if (!double.IsFinite(angleDegrees)) throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        double radians = angleDegrees * Math.PI / 180d;
        return new(Math.Sin(radians), -Math.Cos(radians));
    }

    public static double NominalLogicalRadialDelta(
        NormalizedRenderPlan sourcePlan,
        double slotContentRadiusCru)
    {
        ArgumentNullException.ThrowIfNull(sourcePlan);
        if (!double.IsFinite(slotContentRadiusCru))
            throw new ArgumentOutOfRangeException(nameof(slotContentRadiusCru));
        return (slotContentRadiusCru - NeutralSlotContentRadiusCru) *
            sourcePlan.ReferenceScale.LogicalWidth / CanonicalRadialDesignWidthCru;
    }

    public static UniversalDynamicTranslation AuthoredGeometryTranslation(
        UniversalRadialRenderPlan plan,
        UniversalDynamicContentGroup group)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(group);
        if (group.Role != UniversalDynamicContentGroupRole.OuterSlotContent)
            return UniversalDynamicTranslation.Zero;
        double logicalDelta = NominalLogicalRadialDelta(
            plan.SourcePlan,
            plan.Parameters.SlotContentRadiusCru);
        if (logicalDelta == 0d) return UniversalDynamicTranslation.Zero;
        double referenceFactor = Math.Min(
            plan.SourcePlan.ReferenceScale.LogicalWidth / plan.SourcePlan.ReferenceCanvas.Width,
            plan.SourcePlan.ReferenceScale.LogicalHeight / plan.SourcePlan.ReferenceCanvas.Height);
        double authoredDelta = logicalDelta / referenceFactor;
        return new(
            group.RadialUnitVector.X * authoredDelta,
            group.RadialUnitVector.Y * authoredDelta);
    }

    public static UniversalDynamicTranslation EffectivePhysicalTranslation(
        UniversalRadialRenderPlan plan,
        UniversalDynamicContentGroup group,
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(group);
        if (group.Role != UniversalDynamicContentGroupRole.OuterSlotContent)
            return UniversalDynamicTranslation.Zero;
        double magnitude = NominalLogicalRadialDelta(
            plan.SourcePlan,
            plan.Parameters.SlotContentRadiusCru) *
            plan.Parameters.SurfaceScale *
            RadialDpiScaling.GetScale(dpi);
        return new(group.RadialUnitVector.X * magnitude, group.RadialUnitVector.Y * magnitude);
    }

    public static NormalizedDynamicAnchor TranslateAnchor(
        UniversalRadialRenderPlan plan,
        string layerId,
        NormalizedDynamicAnchor authoredAnchor)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentNullException.ThrowIfNull(authoredAnchor);
        if (!plan.TryGetDynamicContentGroupForLayer(layerId, out UniversalDynamicContentGroup group))
            return authoredAnchor;
        UniversalDynamicTranslation translation = AuthoredGeometryTranslation(plan, group);
        if (translation == UniversalDynamicTranslation.Zero) return authoredAnchor;
        NormalizedReferenceBounds bounds = authoredAnchor.Bounds;
        return authoredAnchor with
        {
            Bounds = bounds with
            {
                X = bounds.X + translation.X,
                Y = bounds.Y + translation.Y
            }
        };
    }
}

internal static class UniversalDynamicContentGroupFactory
{
    public static ReadOnlyCollection<UniversalDynamicContentGroup> Build(NormalizedRenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.HasV2DynamicContent ? BuildV2(plan) : BuildV1(plan);
    }

    private static ReadOnlyCollection<UniversalDynamicContentGroup> BuildV1(NormalizedRenderPlan plan)
    {
        LayoutDefinition layout = plan.LayoutDefinition;
        UniversalDynamicContentGroup[] groups = layout.Slots.Select(slot =>
        {
            var origin = new NormalizedPoint(slot.GlyphAnchor.X, slot.GlyphAnchor.Y);
            double radial = Distance(origin.X - layout.WheelCenter.X, origin.Y - layout.WheelCenter.Y);
            var bounds = new NormalizedReferenceBounds(origin.X, origin.Y, 0d, 0d);
            return new UniversalDynamicContentGroup(
                $"slot-{slot.Id}",
                UniversalDynamicContentGroupRole.OuterSlotContent,
                slot.Id,
                origin,
                radial,
                slot.AngleDegrees,
                UniversalDynamicContentTransform.RadialUnitVector(slot.AngleDegrees),
                new[]
                {
                    new UniversalDynamicContentMember(
                        $"slot{slot.Id}ActionLabel",
                        $"slot{slot.Id}ActionLabel",
                        "text",
                        bounds,
                        0d,
                        0d,
                        0d,
                        "center",
                        "center",
                        "ellipsis")
                });
        }).ToArray();
        return Array.AsReadOnly(groups);
    }

    private static ReadOnlyCollection<UniversalDynamicContentGroup> BuildV2(NormalizedRenderPlan plan)
    {
        NormalizedDynamicThemeModel dynamic = plan.DynamicTheme!;
        var drafts = new Dictionary<string, List<(NormalizedDynamicAnchor Anchor, NormalizedDynamicOwnership Ownership)>>(StringComparer.Ordinal);
        foreach (NormalizedDynamicOwnership ownership in dynamic.OwnershipByLayer.Values)
        {
            string identity = GroupIdentity(ownership.ContentKey);
            if (!drafts.TryGetValue(identity, out var members))
                drafts.Add(identity, members = new());
            members.Add((dynamic.AnchorForLayer(ownership.LayerId), ownership));
        }

        var groups = new List<UniversalDynamicContentGroup>();
        foreach ((string identity, List<(NormalizedDynamicAnchor Anchor, NormalizedDynamicOwnership Ownership)> draft) in
            drafts.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            int? slotId = ParseSlotId(draft[0].Ownership.ContentKey);
            UniversalDynamicContentGroupRole role = slotId.HasValue
                ? UniversalDynamicContentGroupRole.OuterSlotContent
                : UniversalDynamicContentGroupRole.SelectedCenterContent;
            double originX = draft.Average(x => x.Anchor.Bounds.X + x.Anchor.Bounds.Width / 2d);
            double originY = draft.Average(x => x.Anchor.Bounds.Y + x.Anchor.Bounds.Height / 2d);
            var origin = new NormalizedPoint(originX, originY);
            double centerX = plan.ReferenceCanvas.Width / 2d;
            double centerY = plan.ReferenceCanvas.Height / 2d;
            double radial = Distance(originX - centerX, originY - centerY);
            RadialSlotDefinition? slot = slotId.HasValue
                ? plan.LayoutDefinition.Slots.Single(x => x.Id == slotId.Value)
                : null;
            NormalizedPoint unit = slot == null
                ? new NormalizedPoint(0d, 0d)
                : UniversalDynamicContentTransform.RadialUnitVector(slot.AngleDegrees);
            UniversalDynamicContentMember[] members = draft
                .OrderBy(x => x.Anchor.LayerId, StringComparer.Ordinal)
                .Select(x => new UniversalDynamicContentMember(
                    x.Anchor.Id,
                    x.Anchor.LayerId,
                    x.Anchor.Role,
                    x.Anchor.Bounds,
                    x.Anchor.Bounds.X + x.Anchor.Bounds.Width / 2d - originX,
                    x.Anchor.Bounds.Y + x.Anchor.Bounds.Height / 2d - originY,
                    x.Anchor.Rotation,
                    x.Anchor.HorizontalAlignment,
                    x.Anchor.VerticalAlignment,
                    x.Anchor.OverflowPolicy))
                .ToArray();
            groups.Add(new(
                identity,
                role,
                slotId,
                origin,
                radial,
                slot?.AngleDegrees,
                unit,
                members));
        }
        return Array.AsReadOnly(groups.ToArray());
    }

    private static string GroupIdentity(string contentKey) =>
        contentKey.StartsWith("selectedAction", StringComparison.Ordinal)
            ? "selected-center"
            : $"slot-{ParseSlotId(contentKey)!.Value}";

    private static int? ParseSlotId(string contentKey)
    {
        if (!contentKey.StartsWith("slot", StringComparison.Ordinal)) return null;
        int action = contentKey.IndexOf("Action", StringComparison.Ordinal);
        if (action <= 4 || !int.TryParse(contentKey.AsSpan(4, action - 4), out int slot))
            throw new InvalidDataException($"Unknown dynamic content key '{contentKey}'.");
        return slot;
    }

    private static double Distance(double x, double y) => Math.Sqrt(x * x + y * y);
}

internal sealed record UniversalRadialRenderPlan
{
    private readonly IReadOnlyDictionary<string, UniversalDynamicContentGroup> _dynamicGroupByLayer;
    private readonly IReadOnlyDictionary<int, UniversalDynamicContentGroup> _outerGroupBySlot;

    private UniversalRadialRenderPlan(
        NormalizedRenderPlan sourcePlan,
        UniversalRadialParameters parameters)
    {
        SourcePlan = sourcePlan;
        Parameters = parameters;
        parameters.ValidateForLayout(sourcePlan.LayoutDefinition);
        LayoutDefinition = sourcePlan.LayoutDefinition;
        NominalLogicalSurface = new(
            sourcePlan.ReferenceScale.LogicalWidth,
            sourcePlan.ReferenceScale.LogicalHeight);
        EffectiveLogicalSurface = NominalLogicalSurface.Scale(parameters.SurfaceScale);
        AuthoredActivationAnchor = sourcePlan.Placement.ActivationAnchor;
        EffectiveActivationAnchor = new(
            AuthoredActivationAnchor.X * parameters.SurfaceScale,
            AuthoredActivationAnchor.Y * parameters.SurfaceScale);
        RenderModel = sourcePlan.RenderModel switch
        {
            LegacyCanonicalSelectedPlan => UniversalRadialRenderModel.LegacyCanonicalSelected,
            FullStateFrameRenderPlan => UniversalRadialRenderModel.FullStateFrame,
            _ => throw new NotSupportedException($"Unsupported render model: {sourcePlan.RenderModel.GetType().Name}")
        };
        DynamicContentGroups = UniversalDynamicContentGroupFactory.Build(sourcePlan);
        _dynamicGroupByLayer = DynamicContentGroups
            .SelectMany(group => group.Members.Select(member => (member.LayerId, Group: group)))
            .ToDictionary(x => x.LayerId, x => x.Group, StringComparer.Ordinal);
        _outerGroupBySlot = DynamicContentGroups
            .Where(group => group.Role == UniversalDynamicContentGroupRole.OuterSlotContent)
            .ToDictionary(group => group.SlotId!.Value);
    }

    public static UniversalRadialRenderPlan Create(
        NormalizedRenderPlan sourcePlan,
        UniversalRadialParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(sourcePlan);
        ArgumentNullException.ThrowIfNull(parameters);
        return new(sourcePlan, parameters);
    }

    public NormalizedRenderPlan SourcePlan { get; }
    public LayoutDefinition LayoutDefinition { get; }
    public UniversalRadialParameters Parameters { get; }
    public UniversalLogicalSize NominalLogicalSurface { get; }
    public UniversalLogicalSize EffectiveLogicalSurface { get; }
    public NormalizedPoint AuthoredActivationAnchor { get; }
    public NormalizedPoint EffectiveActivationAnchor { get; }
    public UniversalRadialRenderModel RenderModel { get; }
    public ReadOnlyCollection<UniversalDynamicContentGroup> DynamicContentGroups { get; }
    public int SourceProtocolVersion => SourcePlan.SourceProtocolVersion;
    public int SourcePackageRevision => SourcePlan.SourcePackageRevision;

    public Size PhysicalSurfaceSize(int dpi) => RadialDpiScaling.LogicalSizeToPhysical(
        EffectiveLogicalSurface.Width,
        EffectiveLogicalSurface.Height,
        dpi);

    public Point PhysicalActivationAnchor(int dpi) => new(
        RadialDpiScaling.LogicalEdgeToPhysical(EffectiveActivationAnchor.X, dpi),
        RadialDpiScaling.LogicalEdgeToPhysical(EffectiveActivationAnchor.Y, dpi));

    public Point ComputeOverlayTopLeft(Point screenPoint, int dpi)
    {
        Point anchor = PhysicalActivationAnchor(dpi);
        return new(screenPoint.X - anchor.X, screenPoint.Y - anchor.Y);
    }

    public UniversalRadialRenderPlan WithParameters(UniversalRadialParameters parameters) =>
        Create(SourcePlan, parameters);

    public UniversalDynamicContentGroup GetOuterDynamicContentGroup(int slotId) =>
        _outerGroupBySlot.TryGetValue(slotId, out UniversalDynamicContentGroup? group)
            ? group
            : throw new ArgumentOutOfRangeException(nameof(slotId));

    public bool TryGetDynamicContentGroupForLayer(
        string layerId,
        out UniversalDynamicContentGroup group) =>
        _dynamicGroupByLayer.TryGetValue(layerId, out group!);
}

internal sealed record UniversalRadialCacheKey(
    string ThemeId,
    int SourceProtocolVersion,
    int SourcePackageRevision,
    double SurfaceScale,
    double FontScale,
    byte TextStrength,
    double SlotContentRadiusCru,
    int Dpi,
    ThemeMappingSnapshot MappingSnapshot,
    string FontEnvironment);

internal sealed record UniversalRenderBuildDiagnostics(
    TimeSpan Elapsed,
    int AuthoredDecodeCount,
    int DynamicBuildCount,
    bool SelectionHotPathPrebuilt);
