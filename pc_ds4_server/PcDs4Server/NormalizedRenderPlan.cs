using System.Collections.ObjectModel;

namespace PcDs4Server;

internal sealed record NormalizedReferenceCanvas(int Width, int Height, string ColorMode);

internal sealed record NormalizedReferenceScale(string Mode)
{
    public const string DirectToFinalPhysical = "direct-to-final-physical";
}

internal sealed record NormalizedStaticLayer(string Id, string AssetPath);

internal abstract record NormalizedRenderModel;

internal sealed record LegacyCanonicalSelectedPlan(
    NormalizedStaticLayer BaseLayer,
    string CanonicalSelectedAssetPath,
    string StateLookupMode) : NormalizedRenderModel
{
    public const string LegacyRotationStateLookup = "legacy-canonical-rotation";
}

internal sealed record NormalizedDynamicContentDescriptor(string Mode)
{
    public const string LegacyGlyphMappings = "legacy-glyph-mappings";
}

internal sealed record NormalizedGeometryTransform(string Mode)
{
    public const string LayoutSlotRotation = "layout-slot-rotation";
}

internal sealed record NormalizedFallbackMetadata(
    string StartupFallbackThemeId,
    bool RetainActiveOnCandidateFailure);

internal sealed class NormalizedRenderPlan
{
    private readonly ReadOnlyCollection<NormalizedStaticLayer> _staticLayers;
    private readonly ReadOnlyCollection<NormalizedGeometryTransform> _geometryTransforms;
    private readonly ReadOnlyCollection<string> _requiredRuntimeCapabilities;

    internal NormalizedRenderPlan(
        int sourceProtocolVersion,
        string themeId,
        string displayName,
        LayoutDefinition layoutDefinition,
        NormalizedReferenceCanvas referenceCanvas,
        NormalizedReferenceScale referenceScale,
        NormalizedRenderModel renderModel,
        IEnumerable<NormalizedStaticLayer> staticLayers,
        NormalizedDynamicContentDescriptor dynamicContent,
        IEnumerable<NormalizedGeometryTransform> geometryTransforms,
        NormalizedFallbackMetadata fallback,
        IEnumerable<string> requiredRuntimeCapabilities)
    {
        if (sourceProtocolVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceProtocolVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(layoutDefinition);
        ArgumentNullException.ThrowIfNull(referenceCanvas);
        ArgumentNullException.ThrowIfNull(referenceScale);
        ArgumentNullException.ThrowIfNull(renderModel);
        ArgumentNullException.ThrowIfNull(staticLayers);
        ArgumentNullException.ThrowIfNull(dynamicContent);
        ArgumentNullException.ThrowIfNull(geometryTransforms);
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(requiredRuntimeCapabilities);

        NormalizedStaticLayer[] staticLayerValues = staticLayers.ToArray();
        NormalizedGeometryTransform[] geometryTransformValues = geometryTransforms.ToArray();
        string[] capabilityValues = requiredRuntimeCapabilities.ToArray();
        if (referenceCanvas.Width <= 0 || referenceCanvas.Height <= 0 ||
            string.IsNullOrWhiteSpace(referenceCanvas.ColorMode))
        {
            throw new ArgumentException("Reference canvas must be complete.", nameof(referenceCanvas));
        }
        if (referenceCanvas.Width != layoutDefinition.Canvas.Width ||
            referenceCanvas.Height != layoutDefinition.Canvas.Height ||
            !string.Equals(
                referenceCanvas.ColorMode,
                layoutDefinition.Canvas.Mode,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Reference canvas must match the authoritative layout canvas.",
                nameof(referenceCanvas));
        }
        if (staticLayerValues.Length == 0)
            throw new ArgumentException("A render plan requires at least one static layer.", nameof(staticLayers));
        if (staticLayerValues.Any(layer =>
                string.IsNullOrWhiteSpace(layer.Id) || string.IsNullOrWhiteSpace(layer.AssetPath)))
        {
            throw new ArgumentException("Static layers must be complete.", nameof(staticLayers));
        }
        if (geometryTransformValues.Any(transform => string.IsNullOrWhiteSpace(transform.Mode)))
            throw new ArgumentException("Geometry transforms must be complete.", nameof(geometryTransforms));
        if (capabilityValues.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Runtime capability IDs cannot be empty.", nameof(requiredRuntimeCapabilities));
        if (string.IsNullOrWhiteSpace(fallback.StartupFallbackThemeId))
            throw new ArgumentException("Fallback metadata must be complete.", nameof(fallback));
        if (renderModel is LegacyCanonicalSelectedPlan legacy &&
            (string.IsNullOrWhiteSpace(legacy.CanonicalSelectedAssetPath) ||
             string.IsNullOrWhiteSpace(legacy.StateLookupMode) ||
             !staticLayerValues.Contains(legacy.BaseLayer)))
        {
            throw new ArgumentException("Legacy canonical render model is incomplete.", nameof(renderModel));
        }

        SourceProtocolVersion = sourceProtocolVersion;
        ThemeId = themeId;
        DisplayName = displayName;
        LayoutDefinition = layoutDefinition;
        ReferenceCanvas = referenceCanvas;
        ReferenceScale = referenceScale;
        RenderModel = renderModel;
        _staticLayers = Array.AsReadOnly(staticLayerValues);
        DynamicContent = dynamicContent;
        _geometryTransforms = Array.AsReadOnly(geometryTransformValues);
        Fallback = fallback;
        _requiredRuntimeCapabilities = Array.AsReadOnly(capabilityValues);
    }

    public int SourceProtocolVersion { get; }
    public string ThemeId { get; }
    public string DisplayName { get; }
    public LayoutDefinition LayoutDefinition { get; }
    public string LayoutProfileId => LayoutDefinition.ProfileId;
    public NormalizedReferenceCanvas ReferenceCanvas { get; }
    public NormalizedReferenceScale ReferenceScale { get; }
    public NormalizedRenderModel RenderModel { get; }
    public IReadOnlyList<NormalizedStaticLayer> StaticLayers => _staticLayers;
    public NormalizedDynamicContentDescriptor DynamicContent { get; }
    public IReadOnlyList<NormalizedGeometryTransform> GeometryTransforms => _geometryTransforms;
    public NormalizedFallbackMetadata Fallback { get; }
    public IReadOnlyList<string> RequiredRuntimeCapabilities => _requiredRuntimeCapabilities;
}

internal static class V1VisualPackCompatibilityAdapter
{
    public static NormalizedRenderPlan BuildPlan(RadialVisualPackDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.EnsureRuntimeSessionSupported();

        UiVisualPackManifest manifest = definition.Manifest;
        if (manifest.Version != RadialVisualPackContract.SupportedManifestVersion)
            throw new InvalidDataException($"Unsupported V1 visual pack version: {manifest.Version}");
        if (!string.Equals(
                manifest.SelectionAssetMode,
                RadialVisualPackContract.SupportedSelectionAssetMode,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported V1 selection asset mode: {manifest.SelectionAssetMode}");
        }

        var baseLayer = new NormalizedStaticLayer("base", definition.BasePath);
        var renderModel = new LegacyCanonicalSelectedPlan(
            baseLayer,
            definition.SelectedPath,
            LegacyCanonicalSelectedPlan.LegacyRotationStateLookup);

        return new NormalizedRenderPlan(
            manifest.Version,
            manifest.Id,
            manifest.Name,
            definition.LayoutDefinition,
            new NormalizedReferenceCanvas(
                definition.Layout.Canvas.Width,
                definition.Layout.Canvas.Height,
                definition.Layout.Canvas.Mode),
            new NormalizedReferenceScale(NormalizedReferenceScale.DirectToFinalPhysical),
            renderModel,
            new[] { baseLayer },
            new NormalizedDynamicContentDescriptor(
                NormalizedDynamicContentDescriptor.LegacyGlyphMappings),
            new[]
            {
                new NormalizedGeometryTransform(
                    NormalizedGeometryTransform.LayoutSlotRotation)
            },
            new NormalizedFallbackMetadata(
                RadialVisualPackContract.DefaultVisualPackId,
                RetainActiveOnCandidateFailure: true),
            new[]
            {
                "legacy-canonical-selected",
                "legacy-glyph-content",
                "pargb-layered-window"
            });
    }
}
