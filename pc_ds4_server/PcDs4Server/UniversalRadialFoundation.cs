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

internal sealed record UniversalRadialRenderPlan
{
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
}

internal sealed record UniversalRadialCacheKey(
    string ThemeId,
    int SourceProtocolVersion,
    int SourcePackageRevision,
    double SurfaceScale,
    double FontScale,
    byte TextStrength,
    int Dpi,
    ThemeMappingSnapshot MappingSnapshot,
    string FontEnvironment);

internal sealed record UniversalRenderBuildDiagnostics(
    TimeSpan Elapsed,
    int AuthoredDecodeCount,
    int DynamicBuildCount,
    bool SelectionHotPathPrebuilt);
