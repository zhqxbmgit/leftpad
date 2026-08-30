namespace PcDs4Server;

internal static class ProductionUniversalRadialParametersAdapter
{
    private const double NeutralHubRadiusCru = 35d;
    private const double NeutralInnerRadiusCru = 42d;
    private const double NeutralOuterRadiusCru = 103d;
    private const double NeutralGapDegrees = 4d;
    private const double NeutralSlotContentRadiusCru = 73d;

    public static UniversalRadialParameters Adapt(
        RadialMenuSettings settings,
        NormalizedRenderPlan sourcePlan)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sourcePlan);
        if (!settings.TryValidate(out string error))
            throw new ArgumentException(error, nameof(settings));

        RadialSettingsSemanticRevision revision =
            settings.SettingsSemanticRevision ?? RadialSettingsSemanticRevision.Legacy;
        bool isV1 = sourcePlan.SourceProtocolVersion == 1 &&
            sourcePlan.RenderModel is LegacyCanonicalSelectedPlan;
        bool isV2 = sourcePlan.SourceProtocolVersion == 2 &&
            sourcePlan.RenderModel is FullStateFrameRenderPlan;
        if (!isV1 && !isV2)
        {
            throw new NotSupportedException(
                $"UniversalInitial does not support source protocol {sourcePlan.SourceProtocolVersion} " +
                $"with render model '{sourcePlan.RenderModel.GetType().Name}'.");
        }

        double productScale = settings.ScalePercent / 100d;
        double surfaceScale;
        double fontScale;
        byte textStrength;
        if (revision == RadialSettingsSemanticRevision.Legacy && isV1)
        {
            surfaceScale = settings.BaseCanvasSize / 280d * productScale;
            fontScale = settings.FontSize / UniversalRadialParameters.NeutralFontSize *
                (280d / settings.BaseCanvasSize);
            textStrength = UniversalRadialSettingsNormalizer.NormalizeLegacyTextStrength(
                settings.TextAlpha);
        }
        else
        {
            surfaceScale = productScale;
            fontScale = settings.FontSize / UniversalRadialParameters.NeutralFontSize;
            textStrength = byte.MaxValue;
        }

        var parameters = new UniversalRadialParameters(
            revision,
            surfaceScale,
            NeutralHubRadiusCru,
            NeutralInnerRadiusCru,
            NeutralOuterRadiusCru,
            NeutralGapDegrees,
            NeutralSlotContentRadiusCru,
            fontScale,
            byte.MaxValue,
            byte.MaxValue,
            textStrength,
            byte.MaxValue,
            dynamicContentScale: productScale);
        parameters.ValidateForLayout(sourcePlan.LayoutDefinition);
        return parameters;
    }
}
