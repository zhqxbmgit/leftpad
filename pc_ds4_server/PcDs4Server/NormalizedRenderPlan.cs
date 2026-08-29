using System.Collections.ObjectModel;

namespace PcDs4Server;

internal sealed record NormalizedReferenceCanvas(int Width, int Height, string ColorMode);
internal sealed record NormalizedPoint(double X, double Y);

internal sealed record NormalizedReferenceScale
{
    public const string DirectToFinalPhysical = "direct-to-final-physical";
    public const string Contain = "contain";

    public NormalizedReferenceScale(string mode)
        : this(mode, 280d, 280d, new NormalizedPoint(0d, 0d), mode) { }

    public NormalizedReferenceScale(string mode, double logicalWidth, double logicalHeight,
        NormalizedPoint contentOrigin, string fit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(contentOrigin);
        ArgumentException.ThrowIfNullOrWhiteSpace(fit);
        if (!double.IsFinite(logicalWidth) || logicalWidth <= 0d ||
            !double.IsFinite(logicalHeight) || logicalHeight <= 0d ||
            !double.IsFinite(contentOrigin.X) || contentOrigin.X < 0d ||
            !double.IsFinite(contentOrigin.Y) || contentOrigin.Y < 0d)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        Mode = mode;
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        ContentOrigin = contentOrigin;
        Fit = fit;
    }

    public string Mode { get; }
    public double LogicalWidth { get; }
    public double LogicalHeight { get; }
    public NormalizedPoint ContentOrigin { get; }
    public string Fit { get; }
}

internal sealed record NormalizedPlacement(NormalizedPoint ActivationAnchor);
internal sealed record NormalizedStaticLayer(string Id, string AssetPath);
internal sealed record NormalizedReferenceBounds(double X, double Y, double Width, double Height);
internal sealed class VerifiedThemeAsset
{
    private readonly byte[] _bytes;

    public VerifiedThemeAsset(string packagePath, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(bytes);
        PackagePath = packagePath;
        _bytes = bytes.ToArray();
    }

    public string PackagePath { get; }
    public ReadOnlyMemory<byte> Content => _bytes;
    public Stream OpenRead() => new MemoryStream(_bytes, writable: false);
}

internal sealed record FullStateFrameLayer(string Id, string Kind, int ZIndex, int DeclarationOrder,
    NormalizedReferenceBounds Bounds, IReadOnlyList<string> VisibleStates,
    VerifiedThemeAsset? StaticAsset);
internal sealed record FullStateFrameState(string Name, int? SlotId,
    IReadOnlyDictionary<string, VerifiedThemeAsset> Assets);

internal sealed record NormalizedDynamicAnchor(
    string Id,
    string LayerId,
    string Role,
    NormalizedReferenceBounds Bounds,
    string HorizontalAlignment,
    string VerticalAlignment,
    string OverflowPolicy,
    double MinimumScale,
    double Rotation,
    int? MaxLines,
    IReadOnlyList<string> VisibleStates,
    string StyleRole,
    string? GlyphRole);

internal readonly record struct NormalizedThemeColor(byte Red, byte Green, byte Blue, byte Alpha)
{
    public bool IsVisible => Alpha != 0;
    public Color ToColor() => Color.FromArgb(Alpha, Red, Green, Blue);
}

internal sealed record NormalizedOutlineStyle(NormalizedThemeColor Color, double Width);
internal sealed record NormalizedShadowStyle(
    NormalizedThemeColor Color, double OffsetX, double OffsetY, double Blur);
internal sealed record NormalizedDynamicStyle(
    string FontRole,
    NormalizedThemeColor Color,
    double Size,
    NormalizedOutlineStyle? Outline,
    NormalizedShadowStyle? Shadow);
internal sealed record NormalizedGlyphSource(string Type, string? StyleRole, string? SymbolSet);

internal sealed class NormalizedGlyphFamily
{
    private readonly ReadOnlyCollection<NormalizedGlyphSource> _sources;
    public NormalizedGlyphFamily(IEnumerable<NormalizedGlyphSource> sources) =>
        _sources = Array.AsReadOnly(sources.ToArray());
    public IReadOnlyList<NormalizedGlyphSource> Sources => _sources;
}

internal sealed record NormalizedGlyphRole(
    NormalizedGlyphFamily Keyboard,
    NormalizedGlyphFamily KeyboardShortcut,
    NormalizedGlyphFamily Ds4,
    NormalizedGlyphFamily GenericAction);

internal sealed record NormalizedDynamicOwnership(string Element, string LayerId, string ContentKey);

internal sealed class NormalizedDynamicThemeModel
{
    private readonly ReadOnlyCollection<NormalizedDynamicAnchor> _anchors;
    private readonly ReadOnlyDictionary<string, string> _fontRoles;
    private readonly ReadOnlyDictionary<string, NormalizedDynamicStyle> _styles;
    private readonly ReadOnlyDictionary<string, NormalizedGlyphRole> _glyphRoles;
    private readonly ReadOnlyDictionary<string, NormalizedDynamicOwnership> _ownershipByLayer;

    public NormalizedDynamicThemeModel(
        IEnumerable<NormalizedDynamicAnchor> anchors,
        IReadOnlyDictionary<string, string> fontRoles,
        IReadOnlyDictionary<string, NormalizedDynamicStyle> styles,
        IReadOnlyDictionary<string, NormalizedGlyphRole> glyphRoles,
        IEnumerable<NormalizedDynamicOwnership> ownership)
    {
        _anchors = Array.AsReadOnly(anchors.ToArray());
        _fontRoles = new(new Dictionary<string, string>(fontRoles, StringComparer.Ordinal));
        _styles = new(new Dictionary<string, NormalizedDynamicStyle>(styles, StringComparer.Ordinal));
        _glyphRoles = new(new Dictionary<string, NormalizedGlyphRole>(glyphRoles, StringComparer.Ordinal));
        _ownershipByLayer = new(ownership.ToDictionary(x => x.LayerId, StringComparer.Ordinal));
    }

    public IReadOnlyList<NormalizedDynamicAnchor> Anchors => _anchors;
    public IReadOnlyDictionary<string, string> FontRoles => _fontRoles;
    public IReadOnlyDictionary<string, NormalizedDynamicStyle> Styles => _styles;
    public IReadOnlyDictionary<string, NormalizedGlyphRole> GlyphRoles => _glyphRoles;
    public IReadOnlyDictionary<string, NormalizedDynamicOwnership> OwnershipByLayer => _ownershipByLayer;
    public NormalizedDynamicAnchor AnchorForLayer(string layerId) =>
        _anchors.Single(x => string.Equals(x.LayerId, layerId, StringComparison.Ordinal));
}

internal abstract record NormalizedRenderModel;
internal sealed record LegacyCanonicalSelectedPlan(NormalizedStaticLayer BaseLayer,
    string CanonicalSelectedAssetPath, string StateLookupMode) : NormalizedRenderModel
{
    public const string LegacyRotationStateLookup = "legacy-canonical-rotation";
}

internal sealed record FullStateFrameRenderPlan : NormalizedRenderModel
{
    private readonly ReadOnlyCollection<FullStateFrameLayer> _layers;
    private readonly ReadOnlyDictionary<string, FullStateFrameState> _states;
    private readonly ReadOnlyDictionary<int, string> _slotStates;

    public FullStateFrameRenderPlan(IEnumerable<FullStateFrameLayer> layers,
        IEnumerable<FullStateFrameState> states)
    {
        FullStateFrameLayer[] ordered = layers.OrderBy(x => x.ZIndex)
            .ThenBy(x => x.DeclarationOrder).ToArray();
        FullStateFrameState[] values = states.ToArray();
        if (ordered.Length == 0 || values.Length == 0)
            throw new ArgumentException("Full-state plans require layers and states.");
        _layers = Array.AsReadOnly(ordered);
        _states = new(values.ToDictionary(x => x.Name, StringComparer.Ordinal));
        _slotStates = new(values.Where(x => x.SlotId.HasValue)
            .ToDictionary(x => x.SlotId!.Value, x => x.Name));
        if (!_states.ContainsKey("idle")) throw new ArgumentException("Idle state is required.");
    }

    public IReadOnlyList<FullStateFrameLayer> OrderedLayers => _layers;
    public IReadOnlyDictionary<string, FullStateFrameState> States => _states;
    public IReadOnlyDictionary<int, string> SlotStates => _slotStates;
    public FullStateFrameState ResolveState(int slot) => slot == 0 ? _states["idle"] :
        _states[_slotStates.TryGetValue(slot, out string? state) ? state :
            throw new ArgumentOutOfRangeException(nameof(slot))];
}

internal sealed record NormalizedDynamicContentDescriptor(string Mode)
{
    public const string LegacyGlyphMappings = "legacy-glyph-mappings";
    public const string None = "none";
}
internal sealed record NormalizedGeometryTransform(string Mode)
{
    public const string LayoutSlotRotation = "layout-slot-rotation";
}
internal sealed record NormalizedFallbackMetadata(string StartupFallbackThemeId,
    bool RetainActiveOnCandidateFailure);

internal sealed class NormalizedRenderPlan
{
    private readonly ReadOnlyCollection<NormalizedStaticLayer> _staticLayers;
    private readonly ReadOnlyCollection<NormalizedGeometryTransform> _geometryTransforms;
    private readonly ReadOnlyCollection<string> _requiredRuntimeCapabilities;

    internal NormalizedRenderPlan(int sourceProtocolVersion, string themeId, string displayName,
        LayoutDefinition layoutDefinition, NormalizedReferenceCanvas referenceCanvas,
        NormalizedReferenceScale referenceScale, NormalizedRenderModel renderModel,
        IEnumerable<NormalizedStaticLayer> staticLayers,
        NormalizedDynamicContentDescriptor dynamicContent,
        IEnumerable<NormalizedGeometryTransform> geometryTransforms,
        NormalizedFallbackMetadata fallback,
        IEnumerable<string> requiredRuntimeCapabilities,
        NormalizedPlacement? placement = null,
        NormalizedDynamicThemeModel? dynamicTheme = null)
    {
        if (sourceProtocolVersion <= 0) throw new ArgumentOutOfRangeException(nameof(sourceProtocolVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(layoutDefinition);
        ArgumentNullException.ThrowIfNull(referenceCanvas);
        ArgumentNullException.ThrowIfNull(referenceScale);
        ArgumentNullException.ThrowIfNull(renderModel);
        NormalizedStaticLayer[] staticValues = staticLayers.ToArray();
        NormalizedGeometryTransform[] transforms = geometryTransforms.ToArray();
        string[] capabilities = requiredRuntimeCapabilities.ToArray();
        if (referenceCanvas.Width <= 0 || referenceCanvas.Height <= 0 ||
            string.IsNullOrWhiteSpace(referenceCanvas.ColorMode))
            throw new ArgumentException("Reference canvas is incomplete.");
        if (renderModel is LegacyCanonicalSelectedPlan &&
            (referenceCanvas.Width != layoutDefinition.Canvas.Width ||
             referenceCanvas.Height != layoutDefinition.Canvas.Height ||
             !string.Equals(referenceCanvas.ColorMode, layoutDefinition.Canvas.Mode, StringComparison.Ordinal)))
            throw new ArgumentException("Legacy reference canvas must match its layout canvas.");
        if (staticValues.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.AssetPath)))
            throw new ArgumentException("Static layers are incomplete.");
        if (renderModel is LegacyCanonicalSelectedPlan legacy &&
            (staticValues.Length == 0 || string.IsNullOrWhiteSpace(legacy.CanonicalSelectedAssetPath) ||
             !staticValues.Contains(legacy.BaseLayer)))
            throw new ArgumentException("Legacy render model is incomplete.");
        if (capabilities.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Capability IDs cannot be empty.");

        SourceProtocolVersion = sourceProtocolVersion;
        ThemeId = themeId;
        DisplayName = displayName;
        LayoutDefinition = layoutDefinition;
        ReferenceCanvas = referenceCanvas;
        ReferenceScale = referenceScale;
        RenderModel = renderModel;
        _staticLayers = Array.AsReadOnly(staticValues);
        DynamicContent = dynamicContent;
        _geometryTransforms = Array.AsReadOnly(transforms);
        Fallback = fallback;
        _requiredRuntimeCapabilities = Array.AsReadOnly(capabilities);
        Placement = placement ?? LegacyPlacement(referenceCanvas, referenceScale, layoutDefinition);
        DynamicTheme = dynamicTheme;
    }

    public int SourceProtocolVersion { get; }
    public string ThemeId { get; }
    public string DisplayName { get; }
    public LayoutDefinition LayoutDefinition { get; }
    public string LayoutProfileId => LayoutDefinition.ProfileId;
    public NormalizedReferenceCanvas ReferenceCanvas { get; }
    public NormalizedReferenceScale ReferenceScale { get; }
    public NormalizedPlacement Placement { get; }
    public NormalizedRenderModel RenderModel { get; }
    public IReadOnlyList<NormalizedStaticLayer> StaticLayers => _staticLayers;
    public NormalizedDynamicContentDescriptor DynamicContent { get; }
    public IReadOnlyList<NormalizedGeometryTransform> GeometryTransforms => _geometryTransforms;
    public NormalizedFallbackMetadata Fallback { get; }
    public IReadOnlyList<string> RequiredRuntimeCapabilities => _requiredRuntimeCapabilities;
    public bool IsFullStateFrame => RenderModel is FullStateFrameRenderPlan;
    public NormalizedDynamicThemeModel? DynamicTheme { get; }
    public bool HasV2DynamicContent => DynamicTheme is { Anchors.Count: > 0 };

    private static NormalizedPlacement LegacyPlacement(NormalizedReferenceCanvas canvas,
        NormalizedReferenceScale scale, LayoutDefinition layout)
    {
        double factor = Math.Min(scale.LogicalWidth / canvas.Width, scale.LogicalHeight / canvas.Height);
        return new(new(scale.ContentOrigin.X + layout.WheelCenter.X * factor,
            scale.ContentOrigin.Y + layout.WheelCenter.Y * factor));
    }
}

internal static class V1VisualPackCompatibilityAdapter
{
    public static NormalizedRenderPlan BuildPlan(RadialVisualPackDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.EnsureRuntimeSessionSupported();
        UiVisualPackManifest manifest = definition.Manifest;
        var baseLayer = new NormalizedStaticLayer("base", definition.BasePath);
        var scale = new NormalizedReferenceScale(NormalizedReferenceScale.DirectToFinalPhysical,
            RadialMenuSettings.Default.BaseCanvasSize, RadialMenuSettings.Default.BaseCanvasSize,
            new NormalizedPoint(0d, 0d), NormalizedReferenceScale.DirectToFinalPhysical);
        return new NormalizedRenderPlan(manifest.Version, manifest.Id, manifest.Name,
            definition.LayoutDefinition,
            new(definition.Layout.Canvas.Width, definition.Layout.Canvas.Height, definition.Layout.Canvas.Mode),
            scale,
            new LegacyCanonicalSelectedPlan(baseLayer, definition.SelectedPath,
                LegacyCanonicalSelectedPlan.LegacyRotationStateLookup),
            new[] { baseLayer }, new(NormalizedDynamicContentDescriptor.LegacyGlyphMappings),
            new[] { new NormalizedGeometryTransform(NormalizedGeometryTransform.LayoutSlotRotation) },
            new(RadialVisualPackContract.FallbackVisualPackId, true),
            new[] { "legacy-canonical-selected", "legacy-glyph-content", "pargb-layered-window" });
    }
}
