using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PcDs4Server;

internal delegate RuntimeRenderBundle RuntimeRenderBundleBuilder(
    NormalizedRenderPlan plan, RadialMenuSettings settings, int targetSize);
internal delegate RuntimeRenderBundle RuntimeRenderBundleTargetBuilder(
    NormalizedRenderPlan plan, RadialMenuSettings settings, int legacyTargetSize, int dpi);

internal interface IThemeAssetDecoder
{
    Bitmap Decode(VerifiedThemeAsset asset);
}

internal sealed class GdiThemeAssetDecoder : IThemeAssetDecoder
{
    public Bitmap Decode(VerifiedThemeAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        using Stream stream = asset.OpenRead();
        using var source = new Bitmap(stream);
        var decoded = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(decoded);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(source, 0, 0);
        return decoded;
    }
}

internal sealed record DynamicCacheBuildDiagnostics(
    int DynamicLayerRasterCount,
    int SharedDynamicLayerRasterCount,
    int SelectedDynamicLayerRasterCount,
    int DynamicCompositeBuildCount,
    int FullSurfaceDynamicBitmapCount,
    long FullSurfaceDynamicBytes,
    int LocalSpriteBitmapCount,
    long LocalSpriteBytes,
    int FinalStateBuildCount,
    int AuthoredDecodeCount)
{
    public static DynamicCacheBuildDiagnostics Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

internal sealed class FullStateFrameCache : IDisposable
{
    private Dictionary<string, Bitmap> _states;
    private Dictionary<string, ThemeDynamicSprite> _dynamicSprites = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, bool> _dynamicLayerSharing =
        new Dictionary<string, bool>(StringComparer.Ordinal);
    private readonly Dictionary<string, Bitmap> _authoredLayers = new(StringComparer.Ordinal);
    private readonly FullStateFrameRenderPlan _model;
    private readonly ThemeFontSession? _fontSession;
    private SolidBrush? _emptyDynamicAuthority;
    private readonly Action<ThemeMappingSnapshot>? _mappingBuildProbe;
    private UniversalSelectedEmphasisCache? _selectedEmphasis;
    private readonly UniversalSelectedEmphasisProvider? _selectedEmphasisProvider;
    private ThemeMappingSnapshot? _mappingSnapshot;
    private UniversalRadialParameters? _universalParameters;
    private int _fullSurfaceBitmapCreatedCount;
    private int _fullSurfaceBitmapDisposedCount;
    private int _fullSurfaceBitmapLiveCount;
    private int _peakFullSurfaceBitmapLiveCount;
    private int _localSpriteBitmapCreatedCount;
    private int _localSpriteBitmapDisposedCount;
    private int _localSpriteBitmapLiveCount;
    private int _peakLocalSpriteBitmapLiveCount;
    private long _localSpriteBitmapLiveBytes;
    private long _peakLocalSpriteBitmapLiveBytes;
    private bool _disposed;

    public FullStateFrameCache(NormalizedRenderPlan plan, int dpi,
        IThemeAssetDecoder? decoder = null)
        : this(plan, RadialMenuSettings.Default, dpi, decoder, null) { }

    public FullStateFrameCache(NormalizedRenderPlan plan, RadialMenuSettings settings, int dpi,
        IThemeAssetDecoder? decoder = null,
        Action<ThemeMappingSnapshot>? mappingBuildProbe = null,
        UniversalRadialParameters? universalParameters = null,
        UniversalSelectedEmphasisProvider? selectedEmphasisProvider = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.RenderModel is not FullStateFrameRenderPlan model)
            throw new ArgumentException("A full-state plan is required.", nameof(plan));
        Plan = plan;
        _universalParameters = universalParameters;
        _selectedEmphasisProvider = selectedEmphasisProvider;
        _mappingBuildProbe = mappingBuildProbe;
        Dpi = dpi > 0 ? dpi : RadialDpiScaling.DefaultDpi;
        _model = model;
        double presentationScale = universalParameters?.SurfaceScale ?? 1d;
        PhysicalSurfaceSize = RadialDpiScaling.LogicalSizeToPhysical(
            plan.ReferenceScale.LogicalWidth * presentationScale,
            plan.ReferenceScale.LogicalHeight * presentationScale,
            Dpi);
        if (universalParameters != null)
        {
            if (selectedEmphasisProvider == null)
                throw new ArgumentNullException(nameof(selectedEmphasisProvider));
            if (universalParameters.HighlightStrength != byte.MaxValue)
            {
                _selectedEmphasis = new(
                    selectedEmphasisProvider.Load(),
                    UniversalRadialRenderPlan.Create(plan, universalParameters),
                    Dpi);
            }
        }
        decoder ??= new GdiThemeAssetDecoder();

        Dictionary<string, VerifiedThemeAsset> assetSources = model.OrderedLayers
            .Where(x => x.StaticAsset != null).Select(x => x.StaticAsset!)
            .Concat(model.States.Values.SelectMany(x => x.Assets.Values))
            .DistinctBy(x => x.PackagePath, StringComparer.Ordinal)
            .ToDictionary(x => x.PackagePath, StringComparer.Ordinal);
        var masters = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        var states = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        try
        {
            foreach ((string path, VerifiedThemeAsset asset) in assetSources)
            {
                masters.Add(path, decoder.Decode(asset));
                DecodedAssetCount++;
            }
            if (plan.HasV2DynamicContent)
            {
                _fontSession = ThemeFontResolver.Resolve(plan.DynamicTheme!.FontRoles);
                _emptyDynamicAuthority = new SolidBrush(Color.Transparent);
                BuildAuthoredLayerCache(masters);
                _mappingSnapshot = ThemeMappingSnapshot.Capture(settings, plan.LayoutProfileId);
                DynamicCandidate candidate = BuildDynamicCandidate(
                    _mappingSnapshot,
                    universalParameters,
                    _selectedEmphasis);
                _dynamicSprites = candidate.Sprites;
                _dynamicLayerSharing = candidate.LayerSharing;
                states = candidate.States;
                DynamicDiagnostics = candidate.Diagnostics;
            }
            else
            {
                foreach (FullStateFrameState state in model.States.Values.OrderBy(x => x.SlotId ?? 0))
                    states.Add(state.Name, BuildLegacyCompatibleState(state, masters));
            }
            _states = states;
        }
        catch
        {
            foreach (Bitmap bitmap in states.Values) DisposeFullSurface(bitmap);
            foreach (ThemeDynamicSprite sprite in _dynamicSprites.Values) DisposeLocalSprite(sprite);
            foreach (Bitmap bitmap in _authoredLayers.Values) DisposeFullSurface(bitmap);
            _fontSession?.Dispose();
            _emptyDynamicAuthority?.Dispose();
            _emptyDynamicAuthority = null;
            _selectedEmphasis?.Dispose();
            throw;
        }
        finally
        {
            foreach (Bitmap bitmap in masters.Values) bitmap.Dispose();
        }
    }

    public NormalizedRenderPlan Plan { get; }
    public int Dpi { get; }
    public Size PhysicalSurfaceSize { get; }
    public int DecodedAssetCount { get; }
    public int StateCount => _states.Count;
    public int DynamicBuildCount { get; private set; }
    public int MappingRebuildCount { get; private set; }
    public int HotPathDynamicWorkCount { get; private set; }
    public int AuthoredLayerCount => _authoredLayers.Count;
    public DynamicCacheBuildDiagnostics DynamicDiagnostics { get; private set; } =
        DynamicCacheBuildDiagnostics.Empty;
    public int FullSurfaceBitmapCreatedCount => _fullSurfaceBitmapCreatedCount;
    public int FullSurfaceBitmapDisposedCount => _fullSurfaceBitmapDisposedCount;
    public int FullSurfaceBitmapLiveCount => _fullSurfaceBitmapLiveCount;
    public int PeakFullSurfaceBitmapLiveCount => _peakFullSurfaceBitmapLiveCount;
    public int DynamicBitmapLiveCount => 0;
    public int PeakDynamicBitmapLiveCount => 0;
    public int LocalSpriteBitmapCreatedCount => _localSpriteBitmapCreatedCount;
    public int LocalSpriteBitmapDisposedCount => _localSpriteBitmapDisposedCount;
    public int LocalSpriteBitmapLiveCount => _localSpriteBitmapLiveCount;
    public int PeakLocalSpriteBitmapLiveCount => _peakLocalSpriteBitmapLiveCount;
    public long LocalSpriteBitmapLiveBytes => _localSpriteBitmapLiveBytes;
    public long PeakLocalSpriteBitmapLiveBytes => _peakLocalSpriteBitmapLiveBytes;
    public long FullSurfaceBytesPerBitmap =>
        checked((long)PhysicalSurfaceSize.Width * PhysicalSurfaceSize.Height * 4L);
    public long FullSurfaceBitmapLiveBytes =>
        checked(FullSurfaceBytesPerBitmap * _fullSurfaceBitmapLiveCount);
    public long PeakFullSurfaceBitmapLiveBytes =>
        checked(FullSurfaceBytesPerBitmap * _peakFullSurfaceBitmapLiveCount);
    public long PeakDynamicBitmapLiveBytes => 0;
    public int SelectedEmphasisDecodedAssetCount => _selectedEmphasis?.DecodedAssetCount ?? 0;
    public int SelectedEmphasisArtworkBuildCount => _selectedEmphasis?.ArtworkBuildCount ?? 0;
    public int SelectedEmphasisManifestReadCount => _selectedEmphasisProvider?.ManifestReadCount ?? 0;
    public bool HasSelectedEmphasisCache => _selectedEmphasis != null;
    public int HighlightRebuildCount { get; private set; }
    public UniversalSelectedEmphasisCache SelectedEmphasisCache => _selectedEmphasis ??
        throw new InvalidOperationException("This cache was not built through the Universal opt-in path.");
    public ThemeMappingSnapshot? MappingSnapshot => _mappingSnapshot;
    public string FontEnvironment => _fontSession == null
        ? "none"
        : string.Join(";", _fontSession.ResolvedNames.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}"));

    public Bitmap GetState(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _states[_model.ResolveState(slot).Name];
    }

    public bool EnsureMappings(RadialMenuSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Plan.HasV2DynamicContent) return false;
        ThemeMappingSnapshot candidateSnapshot = ThemeMappingSnapshot.Capture(settings, Plan.LayoutProfileId);
        if (candidateSnapshot.Equals(_mappingSnapshot)) return false;
        DynamicCandidate candidate =
            BuildDynamicCandidate(candidateSnapshot, _universalParameters, _selectedEmphasis);
        Dictionary<string, ThemeDynamicSprite> previousDynamic = _dynamicSprites;
        Dictionary<string, Bitmap> previousStates = _states;
        _dynamicSprites = candidate.Sprites;
        _dynamicLayerSharing = candidate.LayerSharing;
        _states = candidate.States;
        DynamicDiagnostics = candidate.Diagnostics;
        _mappingSnapshot = candidateSnapshot;
        MappingRebuildCount++;
        foreach (ThemeDynamicSprite sprite in previousDynamic.Values) DisposeLocalSprite(sprite);
        foreach (Bitmap bitmap in previousStates.Values) DisposeFullSurface(bitmap);
        return true;
    }

    public bool EnsureUniversalDynamic(
        RadialMenuSettings settings,
        UniversalRadialParameters parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parameters);
        if (_universalParameters == null)
            throw new InvalidOperationException("This cache was not built through the Universal opt-in path.");
        if (parameters.SurfaceScale != _universalParameters.SurfaceScale)
            throw new InvalidOperationException("SurfaceScale changes require an authored-layer rebuild.");
        if (!Plan.HasV2DynamicContent) return false;

        ThemeMappingSnapshot candidateSnapshot = ThemeMappingSnapshot.Capture(settings, Plan.LayoutProfileId);
        bool mappingChanged = !candidateSnapshot.Equals(_mappingSnapshot);
        bool styleChanged = parameters.FontScale != _universalParameters.FontScale ||
            parameters.DynamicContentScale != _universalParameters.DynamicContentScale ||
            parameters.TextStrength != _universalParameters.TextStrength ||
            parameters.SlotContentRadiusCru != _universalParameters.SlotContentRadiusCru;
        bool highlightChanged = parameters.HighlightStrength != _universalParameters.HighlightStrength;
        if (!mappingChanged && !styleChanged && !highlightChanged) return false;
        byte previousHighlight = _universalParameters.HighlightStrength;
        UniversalSelectedEmphasisCache? previousSelectedEmphasis = _selectedEmphasis;
        UniversalSelectedEmphasisCache? candidateSelectedEmphasis = previousSelectedEmphasis;
        bool createdSelectedEmphasis = false;
        bool changedSelectedStrength = false;
        try
        {
            if (highlightChanged)
            {
                if (parameters.HighlightStrength == byte.MaxValue)
                {
                    candidateSelectedEmphasis = null;
                }
                else if (candidateSelectedEmphasis == null)
                {
                    UniversalSelectedEmphasisProvider provider = _selectedEmphasisProvider ??
                        throw new InvalidOperationException(
                            "This Universal cache has no selected-emphasis provider.");
                    candidateSelectedEmphasis = new(
                        provider.Load(),
                        UniversalRadialRenderPlan.Create(Plan, parameters),
                        Dpi);
                    createdSelectedEmphasis = true;
                }
                else
                {
                    changedSelectedStrength = candidateSelectedEmphasis.EnsureStrength(
                        parameters.HighlightStrength);
                }
            }

            if (!mappingChanged && !styleChanged)
            {
                Dictionary<string, Bitmap> selectedStates = BuildSelectedStateCandidate(
                    _dynamicSprites,
                    _dynamicLayerSharing,
                    candidateSelectedEmphasis);
                foreach ((string name, Bitmap replacement) in selectedStates)
                {
                    Bitmap previous = _states[name];
                    _states[name] = replacement;
                    DisposeFullSurface(previous);
                }
                _selectedEmphasis = candidateSelectedEmphasis;
                if (!ReferenceEquals(previousSelectedEmphasis, candidateSelectedEmphasis))
                    previousSelectedEmphasis?.Dispose();
                _universalParameters = parameters;
                HighlightRebuildCount++;
                return true;
            }

            DynamicCandidate candidate =
                BuildDynamicCandidate(candidateSnapshot, parameters, candidateSelectedEmphasis);
            Dictionary<string, ThemeDynamicSprite> previousDynamic = _dynamicSprites;
            Dictionary<string, Bitmap> previousStates = _states;
            _dynamicSprites = candidate.Sprites;
            _dynamicLayerSharing = candidate.LayerSharing;
            _states = candidate.States;
            DynamicDiagnostics = candidate.Diagnostics;
            _mappingSnapshot = candidateSnapshot;
            _selectedEmphasis = candidateSelectedEmphasis;
            if (!ReferenceEquals(previousSelectedEmphasis, candidateSelectedEmphasis))
                previousSelectedEmphasis?.Dispose();
            _universalParameters = parameters;
            if (mappingChanged) MappingRebuildCount++;
            if (highlightChanged) HighlightRebuildCount++;
            foreach (ThemeDynamicSprite sprite in previousDynamic.Values) DisposeLocalSprite(sprite);
            foreach (Bitmap bitmap in previousStates.Values) DisposeFullSurface(bitmap);
            return true;
        }
        catch
        {
            if (createdSelectedEmphasis)
                candidateSelectedEmphasis?.Dispose();
            else if (changedSelectedStrength)
                previousSelectedEmphasis!.EnsureStrength(previousHighlight);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Bitmap bitmap in _states.Values) DisposeFullSurface(bitmap);
        foreach (ThemeDynamicSprite sprite in _dynamicSprites.Values) DisposeLocalSprite(sprite);
        foreach (Bitmap bitmap in _authoredLayers.Values) DisposeFullSurface(bitmap);
        _fontSession?.Dispose();
        _emptyDynamicAuthority?.Dispose();
        _emptyDynamicAuthority = null;
        _selectedEmphasis?.Dispose();
    }

    private Bitmap BuildLegacyCompatibleState(FullStateFrameState state, IReadOnlyDictionary<string, Bitmap> masters)
    {
        Bitmap target = CreateFullSurface();
        try
        {
            using Graphics graphics = Graphics.FromImage(target);
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            foreach (FullStateFrameLayer layer in _model.OrderedLayers)
            {
                if (!IsVisible(layer.VisibleStates, state.Name, state.SlotId)) continue;
                VerifiedThemeAsset asset = layer.Kind == "staticAsset"
                    ? layer.StaticAsset!
                    : state.Assets[layer.Id];
                Rectangle targetBounds = ToPhysicalBounds(layer.Bounds);
                if (targetBounds.Width <= 0 || targetBounds.Height <= 0) continue;
                Bitmap master = masters[asset.PackagePath];
                graphics.DrawImage(master, targetBounds, 0, 0, master.Width, master.Height,
                    GraphicsUnit.Pixel, attributes);
            }
            return target;
        }
        catch
        {
            DisposeFullSurface(target);
            throw;
        }
    }

    private void BuildAuthoredLayerCache(IReadOnlyDictionary<string, Bitmap> masters)
    {
        foreach (FullStateFrameState state in _model.States.Values)
        foreach (FullStateFrameLayer layer in _model.OrderedLayers)
        {
            if (layer.Kind is not ("staticAsset" or "stateAsset") || !IsVisible(layer.VisibleStates, state.Name, state.SlotId))
                continue;
            VerifiedThemeAsset asset = layer.Kind == "staticAsset" ? layer.StaticAsset! : state.Assets[layer.Id];
            string key = AuthoredKey(layer.Id, asset.PackagePath);
            if (_authoredLayers.ContainsKey(key)) continue;
            Bitmap target = CreateFullSurface();
            try
            {
                using Graphics graphics = Graphics.FromImage(target);
                ConfigureGraphics(graphics);
                using var attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                Bitmap master = masters[asset.PackagePath];
                Rectangle bounds = ToPhysicalBounds(layer.Bounds);
                graphics.DrawImage(master, bounds, 0, 0, master.Width, master.Height, GraphicsUnit.Pixel, attributes);
                _authoredLayers.Add(key, target);
            }
            catch { DisposeFullSurface(target); throw; }
        }
    }

    private DynamicCandidate BuildDynamicCandidate(
        ThemeMappingSnapshot mappings,
        UniversalRadialParameters? universalParameters,
        UniversalSelectedEmphasisCache? selectedEmphasis = null)
    {
        _mappingBuildProbe?.Invoke(mappings);
        var sprites = new Dictionary<string, ThemeDynamicSprite>(StringComparer.Ordinal);
        var states = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        IReadOnlyList<FullStateFrameState> orderedStates = _model.States.Values
            .OrderBy(x => x.SlotId ?? 0)
            .ToArray();
        IReadOnlyList<FullStateFrameLayer> dynamicLayers = _model.OrderedLayers
            .Where(layer => layer.Kind is "dynamicText" or "dynamicGlyph")
            .ToArray();
        IReadOnlyDictionary<string, bool> layerSharing = dynamicLayers.ToDictionary(
            layer => layer.Id,
            layer => IsStateInvariant(layer, mappings, orderedStates),
            StringComparer.Ordinal);
        try
        {
            var rasterizer = new ThemeDynamicRasterizer(
                Plan,
                _fontSession!,
                universalParameters: universalParameters);
            int sharedRasters = 0;
            int selectedRasters = 0;
            foreach (FullStateFrameLayer layer in dynamicLayers)
            {
                if (layerSharing[layer.Id])
                {
                    int before = rasterizer.LayerRasterCount;
                    AddDynamicSprite(
                        sprites,
                        DynamicSpriteKey(layer.Id, null),
                        rasterizer,
                        layer,
                        orderedStates[0],
                        mappings);
                    sharedRasters += rasterizer.LayerRasterCount - before;
                    continue;
                }

                foreach (FullStateFrameState state in orderedStates)
                {
                    int before = rasterizer.LayerRasterCount;
                    AddDynamicSprite(
                        sprites,
                        DynamicSpriteKey(layer.Id, state.Name),
                        rasterizer,
                        layer,
                        state,
                        mappings);
                    selectedRasters += rasterizer.LayerRasterCount - before;
                }
            }

            foreach (FullStateFrameState state in orderedStates)
                states.Add(state.Name, ComposeDynamicState(
                    state,
                    sprites,
                    layerSharing,
                    universalParameters == null ? null : selectedEmphasis));

            DynamicBuildCount++;
            long spriteBytes = sprites.Values.Sum(sprite => sprite.Bytes);
            var diagnostics = new DynamicCacheBuildDiagnostics(
                rasterizer.LayerRasterCount,
                sharedRasters,
                selectedRasters,
                0,
                0,
                0,
                sprites.Count,
                spriteBytes,
                states.Count,
                DecodedAssetCount);
            return new(sprites, layerSharing, states, diagnostics);
        }
        catch
        {
            foreach (ThemeDynamicSprite sprite in sprites.Values) DisposeLocalSprite(sprite);
            foreach (Bitmap bitmap in states.Values) DisposeFullSurface(bitmap);
            throw;
        }
    }

    private void AddDynamicSprite(
        IDictionary<string, ThemeDynamicSprite> sprites,
        string key,
        ThemeDynamicRasterizer rasterizer,
        FullStateFrameLayer layer,
        FullStateFrameState state,
        ThemeMappingSnapshot mappings)
    {
        if (!IsVisible(layer.VisibleStates, state.Name, state.SlotId)) return;
        ThemeDynamicSprite? sprite = rasterizer.RenderSprite(
            layer,
            state,
            mappings,
            Dpi,
            out ThemeDynamicLayoutResult semantic);
        semantic.Dispose();
        if (sprite == null) return;
        try
        {
            sprites.Add(key, sprite);
            RegisterLocalSprite(sprite);
        }
        catch
        {
            sprite.Bitmap.Dispose();
            throw;
        }
    }

    private bool IsStateInvariant(
        FullStateFrameLayer layer,
        ThemeMappingSnapshot mappings,
        IReadOnlyList<FullStateFrameState> states)
    {
        NormalizedDynamicThemeModel model = Plan.DynamicTheme ??
            throw new InvalidOperationException("Dynamic layer grouping requires a dynamic theme model.");
        NormalizedDynamicAnchor anchor = model.AnchorForLayer(layer.Id);
        string contentKey = model.OwnershipByLayer[layer.Id].ContentKey;
        bool? baselineVisible = null;
        ResolvedDynamicContent? baselineContent = null;
        foreach (FullStateFrameState state in states)
        {
            bool visible = IsVisible(layer.VisibleStates, state.Name, state.SlotId) &&
                IsVisible(anchor.VisibleStates, state.Name, state.SlotId);
            if (baselineVisible.HasValue && baselineVisible.Value != visible) return false;
            baselineVisible ??= visible;
            if (!visible) continue;
            ResolvedDynamicContent content = ThemeDynamicContentResolver.Resolve(
                contentKey,
                state.SlotId ?? 0,
                mappings);
            if (baselineContent != null && baselineContent != content) return false;
            baselineContent ??= content;
        }
        return true;
    }

    private Bitmap ComposeDynamicState(
        FullStateFrameState state,
        IReadOnlyDictionary<string, ThemeDynamicSprite> dynamic,
        IReadOnlyDictionary<string, bool> layerSharing,
        UniversalSelectedEmphasisCache? selectedEmphasis)
    {
        Bitmap target = CreateFullSurface();
        try
        {
            using Graphics graphics = Graphics.FromImage(target);
            ConfigureGraphics(graphics);
            bool semanticSelected = state.SlotId.HasValue &&
                selectedEmphasis != null &&
                selectedEmphasis.HighlightStrength != byte.MaxValue;
            if (semanticSelected)
            {
                Bitmap staticArtwork = selectedEmphasis!.HighlightStrength == 0
                    ? selectedEmphasis.BaseStatic
                    : selectedEmphasis.GetIntermediateSelected(state.SlotId!.Value);
                graphics.DrawImageUnscaled(staticArtwork, 0, 0);
            }
            foreach (FullStateFrameLayer layer in _model.OrderedLayers)
            {
                if (layer.Kind is "dynamicText" or "dynamicGlyph")
                {
                    string key = DynamicSpriteKey(
                        layer.Id,
                        layerSharing[layer.Id] ? null : state.Name);
                    if (dynamic.TryGetValue(key, out ThemeDynamicSprite? sprite))
                    {
                        DrawEmptyOutsideSprite(graphics, sprite);
                        graphics.DrawImageUnscaled(
                            sprite.Bitmap,
                            sprite.Location.X,
                            sprite.Location.Y);
                    }
                    else if (IsVisible(layer.VisibleStates, state.Name, state.SlotId))
                        DrawEmptyAuthority(graphics, new Rectangle(Point.Empty, PhysicalSurfaceSize));
                    continue;
                }

                if (!IsVisible(layer.VisibleStates, state.Name, state.SlotId)) continue;
                if (semanticSelected && layer.Kind is ("staticAsset" or "stateAsset")) continue;
                VerifiedThemeAsset asset = layer.Kind == "staticAsset" ? layer.StaticAsset! : state.Assets[layer.Id];
                Bitmap source = _authoredLayers[AuthoredKey(layer.Id, asset.PackagePath)];
                graphics.DrawImageUnscaled(source, 0, 0);
            }
            return target;
        }
        catch
        {
            DisposeFullSurface(target);
            throw;
        }
    }

    private void DrawEmptyOutsideSprite(Graphics graphics, ThemeDynamicSprite sprite)
    {
        // The frozen PArgb contract includes the canonicalization performed by an
        // empty full-surface layer. Covering the area outside the tight sprite with
        // the same transparent SourceOver operation preserves those raw pixels
        // without allocating the former full-canvas dynamic bitmap.
        Rectangle bounds = new(sprite.Location, sprite.Bitmap.Size);
        int right = bounds.Right;
        int bottom = bounds.Bottom;
        DrawEmptyAuthority(graphics, new Rectangle(0, 0, PhysicalSurfaceSize.Width, bounds.Top));
        DrawEmptyAuthority(graphics, new Rectangle(
            0,
            bottom,
            PhysicalSurfaceSize.Width,
            PhysicalSurfaceSize.Height - bottom));
        DrawEmptyAuthority(graphics, new Rectangle(0, bounds.Top, bounds.Left, bounds.Height));
        DrawEmptyAuthority(graphics, new Rectangle(
            right,
            bounds.Top,
            PhysicalSurfaceSize.Width - right,
            bounds.Height));
    }

    private void DrawEmptyAuthority(Graphics graphics, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        SolidBrush brush = _emptyDynamicAuthority ??
            throw new InvalidOperationException(
                "Dynamic composition requires an empty-layer authority.");
        graphics.FillRectangle(brush, bounds);
    }

    private Dictionary<string, Bitmap> BuildSelectedStateCandidate(
        IReadOnlyDictionary<string, ThemeDynamicSprite> dynamic,
        IReadOnlyDictionary<string, bool> layerSharing,
        UniversalSelectedEmphasisCache? selectedEmphasis)
    {
        var states = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        try
        {
            foreach (FullStateFrameState state in _model.States.Values
                         .Where(x => x.SlotId.HasValue)
                         .OrderBy(x => x.SlotId))
                states.Add(state.Name, ComposeDynamicState(
                    state,
                    dynamic,
                    layerSharing,
                    selectedEmphasis));
            return states;
        }
        catch
        {
            foreach (Bitmap bitmap in states.Values) DisposeFullSurface(bitmap);
            throw;
        }
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }

    private static string AuthoredKey(string layerId, string path) => layerId + "\0" + path;
    private static string DynamicSpriteKey(string layerId, string? state) =>
        layerId + "\0" + (state ?? "*");

    private Bitmap CreateFullSurface()
    {
        Bitmap bitmap = new(
            PhysicalSurfaceSize.Width,
            PhysicalSurfaceSize.Height,
            PixelFormat.Format32bppPArgb);
        _fullSurfaceBitmapCreatedCount++;
        _fullSurfaceBitmapLiveCount++;
        _peakFullSurfaceBitmapLiveCount = Math.Max(
            _peakFullSurfaceBitmapLiveCount,
            _fullSurfaceBitmapLiveCount);
        return bitmap;
    }

    private void DisposeFullSurface(Bitmap bitmap)
    {
        bitmap.Dispose();
        _fullSurfaceBitmapDisposedCount++;
        _fullSurfaceBitmapLiveCount--;
    }

    private void RegisterLocalSprite(ThemeDynamicSprite sprite)
    {
        _localSpriteBitmapCreatedCount++;
        _localSpriteBitmapLiveCount++;
        _localSpriteBitmapLiveBytes += sprite.Bytes;
        _peakLocalSpriteBitmapLiveCount = Math.Max(
            _peakLocalSpriteBitmapLiveCount,
            _localSpriteBitmapLiveCount);
        _peakLocalSpriteBitmapLiveBytes = Math.Max(
            _peakLocalSpriteBitmapLiveBytes,
            _localSpriteBitmapLiveBytes);
    }

    private void DisposeLocalSprite(ThemeDynamicSprite sprite)
    {
        long bytes = sprite.Bytes;
        sprite.Bitmap.Dispose();
        _localSpriteBitmapDisposedCount++;
        _localSpriteBitmapLiveCount--;
        _localSpriteBitmapLiveBytes -= bytes;
    }

    private Rectangle ToPhysicalBounds(NormalizedReferenceBounds bounds)
    {
        NormalizedReferenceScale scale = Plan.ReferenceScale;
        double presentationScale = _universalParameters?.SurfaceScale ?? 1d;
        double factor = Math.Min(scale.LogicalWidth / Plan.ReferenceCanvas.Width,
            scale.LogicalHeight / Plan.ReferenceCanvas.Height) * presentationScale;
        double left = scale.ContentOrigin.X * presentationScale + bounds.X * factor;
        double top = scale.ContentOrigin.Y * presentationScale + bounds.Y * factor;
        double right = scale.ContentOrigin.X * presentationScale + (bounds.X + bounds.Width) * factor;
        double bottom = scale.ContentOrigin.Y * presentationScale + (bounds.Y + bounds.Height) * factor;
        return RadialDpiScaling.LogicalRectToPhysical(left, top, right, bottom, Dpi);
    }

    internal static bool IsVisible(IReadOnlyList<string> selectors, string state, int? slot) =>
        selectors.Contains("all", StringComparer.Ordinal) ||
        selectors.Contains(state, StringComparer.Ordinal) ||
        slot.HasValue && selectors.Contains("selected", StringComparer.Ordinal);

    private sealed record DynamicCandidate(
        Dictionary<string, ThemeDynamicSprite> Sprites,
        IReadOnlyDictionary<string, bool> LayerSharing,
        Dictionary<string, Bitmap> States,
        DynamicCacheBuildDiagnostics Diagnostics);
}

internal sealed class RuntimeRenderBundle : IDisposable
{
    private readonly RadialVisualPackCache? _assetCache;
    private readonly RadialDynamicContentCache? _dynamicContent;
    private readonly FullStateFrameCache? _fullStateCache;
    private UniversalSelectedEmphasisCache? _selectedEmphasis;
    private readonly UniversalSelectedEmphasisProvider? _selectedEmphasisProvider;
    private Bitmap[]? _universalLegacyStates;
    private UniversalRadialRenderPlan? _universalPlan;
    private bool _disposed;

    private RuntimeRenderBundle(NormalizedRenderPlan plan, RadialVisualPackCache assetCache,
        RadialDynamicContentCache dynamicContent, int dpi,
        UniversalRadialRenderPlan? universalPlan = null,
        Bitmap[]? universalLegacyStates = null,
        UniversalSelectedEmphasisCache? selectedEmphasis = null,
        UniversalSelectedEmphasisProvider? selectedEmphasisProvider = null)
    {
        Plan = plan;
        _assetCache = assetCache;
        _dynamicContent = dynamicContent;
        _universalPlan = universalPlan;
        _universalLegacyStates = universalLegacyStates;
        _selectedEmphasis = selectedEmphasis;
        _selectedEmphasisProvider = selectedEmphasisProvider;
        Dpi = dpi;
        PhysicalSurfaceSize = new(assetCache.TargetSize, assetCache.TargetSize);
    }

    private RuntimeRenderBundle(
        NormalizedRenderPlan plan,
        FullStateFrameCache cache,
        UniversalRadialRenderPlan? universalPlan = null,
        UniversalSelectedEmphasisProvider? selectedEmphasisProvider = null)
    {
        Plan = plan;
        _fullStateCache = cache;
        _universalPlan = universalPlan;
        _selectedEmphasisProvider = selectedEmphasisProvider;
        Dpi = cache.Dpi;
        PhysicalSurfaceSize = cache.PhysicalSurfaceSize;
    }

    public NormalizedRenderPlan Plan { get; }
    public int TargetSize => PhysicalSurfaceSize.Width;
    public int Dpi { get; }
    public Size PhysicalSurfaceSize { get; }
    public bool IsFullStateFrame => _fullStateCache != null;
    public bool IsUniversal => _universalPlan != null;
    public bool HasPrebuiltFinalStates => _fullStateCache != null || _universalLegacyStates != null;
    public bool HasSelectedEmphasisCache => _fullStateCache?.HasSelectedEmphasisCache ??
        _selectedEmphasis != null;
    public int SelectedEmphasisManifestReadCount =>
        _selectedEmphasisProvider?.ManifestReadCount ??
        _fullStateCache?.SelectedEmphasisManifestReadCount ?? 0;
    public int SelectedEmphasisDecodedAssetCount =>
        _fullStateCache?.SelectedEmphasisDecodedAssetCount ??
        _selectedEmphasis?.DecodedAssetCount ?? 0;
    public UniversalRadialRenderPlan UniversalPlan => _universalPlan ??
        throw new InvalidOperationException("This bundle uses the production legacy path.");
    public UniversalRadialCacheKey? UniversalCacheKey { get; private set; }
    public UniversalRenderBuildDiagnostics? UniversalDiagnostics { get; private set; }
    public TimeSpan LastUniversalDynamicRebuild { get; private set; }
    public int SelectionHotPathWorkCount { get; private set; }
    public RadialVisualPackCache AssetCache => _assetCache ?? throw new InvalidOperationException("This is a V2 full-state bundle.");
    public RadialDynamicContentCache DynamicContent => _dynamicContent ?? throw new InvalidOperationException("This is a V2 full-state bundle.");
    public FullStateFrameCache FullStateCache => _fullStateCache ?? throw new InvalidOperationException("This is a V1 legacy bundle.");
    public UniversalSelectedEmphasisCache SelectedEmphasisCache =>
        _fullStateCache == null
            ? _selectedEmphasis ?? throw new InvalidOperationException("This bundle has no selected-emphasis cache.")
            : throw new InvalidOperationException("The V2 selected-emphasis cache is owned by the full-state cache.");
    internal bool IsDisposed => _disposed;
    internal int DisposeCount { get; private set; }

    public static RuntimeRenderBundle Build(NormalizedRenderPlan plan,
        RadialMenuSettings settings, int targetSize) =>
        Build(plan, settings, targetSize, RadialDpiScaling.DefaultDpi);

    public static RuntimeRenderBundle Build(NormalizedRenderPlan plan,
        RadialMenuSettings settings, int legacyTargetSize, int dpi)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        if (legacyTargetSize <= 0) throw new ArgumentOutOfRangeException(nameof(legacyTargetSize));
        if (plan.RenderModel is FullStateFrameRenderPlan)
            return new RuntimeRenderBundle(plan, new FullStateFrameCache(plan, settings, dpi));

        var assets = new RadialVisualPackCache(plan, legacyTargetSize);
        RadialDynamicContentCache? dynamic = null;
        try
        {
            dynamic = new RadialDynamicContentCache(plan, WindowsUiFontResolver.ResolveUiFontFamily());
            dynamic.Ensure(settings, legacyTargetSize);
            ValidateLegacy(plan, assets, dynamic, legacyTargetSize);
            return new RuntimeRenderBundle(plan, assets, dynamic, dpi);
        }
        catch
        {
            dynamic?.Dispose();
            assets.Dispose();
            throw;
        }
    }

    public static RuntimeRenderBundle BuildUniversal(
        UniversalRadialRenderPlan universalPlan,
        RadialMenuSettings settings,
        int dpi,
        string? selectedEmphasisDiscoveryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(universalPlan);
        ArgumentNullException.ThrowIfNull(settings);
        var stopwatch = Stopwatch.StartNew();
        NormalizedRenderPlan plan = universalPlan.SourcePlan;
        var selectedEmphasisProvider = new UniversalSelectedEmphasisProvider(
            plan,
            selectedEmphasisDiscoveryRoot);
        RuntimeRenderBundle bundle;
        if (plan.RenderModel is FullStateFrameRenderPlan)
        {
            var cache = new FullStateFrameCache(
                plan,
                settings,
                dpi,
                universalParameters: universalPlan.Parameters,
                selectedEmphasisProvider: selectedEmphasisProvider);
            bundle = new RuntimeRenderBundle(
                plan,
                cache,
                universalPlan,
                selectedEmphasisProvider);
        }
        else
        {
            Size physical = universalPlan.PhysicalSurfaceSize(dpi);
            if (physical.Width != physical.Height)
                throw new InvalidDataException("The V1 Universal foundation requires a square logical surface.");
            var assets = new RadialVisualPackCache(plan, physical.Width);
            RadialDynamicContentCache? dynamic = null;
            UniversalSelectedEmphasisCache? selectedCache = null;
            Bitmap[]? states = null;
            try
            {
                if (universalPlan.Parameters.HighlightStrength != byte.MaxValue)
                {
                    selectedCache = new(
                        selectedEmphasisProvider.Load(),
                        universalPlan,
                        dpi);
                }
                dynamic = new RadialDynamicContentCache(plan, WindowsUiFontResolver.ResolveUiFontFamily());
                dynamic.EnsureUniversal(settings, physical.Width, universalPlan);
                states = BuildLegacyFinalStates(
                    plan,
                    assets,
                    dynamic,
                    selectedCache,
                    universalPlan.Parameters.HighlightStrength);
                bundle = new RuntimeRenderBundle(
                    plan,
                    assets,
                    dynamic,
                    dpi,
                    universalPlan,
                    states,
                    selectedCache,
                    selectedEmphasisProvider);
            }
            catch
            {
                if (states != null) foreach (Bitmap state in states) state.Dispose();
                dynamic?.Dispose();
                selectedCache?.Dispose();
                assets.Dispose();
                throw;
            }
        }

        stopwatch.Stop();
        bundle.RefreshUniversalMetadata(settings);
        bundle.UniversalDiagnostics = new(
            stopwatch.Elapsed,
            bundle.IsFullStateFrame
                ? bundle.FullStateCache.DecodedAssetCount
                : bundle.AssetCache.DecodedAssetCount,
            bundle.IsFullStateFrame
                ? bundle.FullStateCache.DynamicBuildCount
                : bundle.DynamicContent.BuildCount,
            SelectionHotPathPrebuilt: true);
        return bundle;
    }

    public void EnsureDynamicContent(RadialMenuSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_universalPlan != null)
        {
            _ = EnsureUniversalContent(settings, _universalPlan.Parameters);
            return;
        }
        if (_fullStateCache != null) _fullStateCache.EnsureMappings(settings);
        else _dynamicContent?.Ensure(settings, TargetSize);
    }

    public bool EnsureUniversalContent(
        RadialMenuSettings settings,
        UniversalRadialParameters parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parameters);
        if (_universalPlan == null)
            throw new InvalidOperationException("This bundle was not built through the Universal opt-in path.");
        if (parameters.SurfaceScale != _universalPlan.Parameters.SurfaceScale)
            throw new InvalidOperationException("SurfaceScale changes require a complete Universal bundle rebuild.");

        var stopwatch = Stopwatch.StartNew();
        bool rebuilt;
        if (_fullStateCache != null)
        {
            rebuilt = _fullStateCache.EnsureUniversalDynamic(settings, parameters);
        }
        else
        {
            UniversalRadialRenderPlan candidatePlan = _universalPlan.WithParameters(parameters);
            byte previousHighlight = _universalPlan.Parameters.HighlightStrength;
            UniversalSelectedEmphasisCache? previousSelectedEmphasis = _selectedEmphasis;
            UniversalSelectedEmphasisCache? candidateSelectedEmphasis = previousSelectedEmphasis;
            bool createdSelectedEmphasis = false;
            bool changedSelectedStrength = false;
            try
            {
                if (parameters.HighlightStrength != previousHighlight)
                {
                    if (parameters.HighlightStrength == byte.MaxValue)
                    {
                        candidateSelectedEmphasis = null;
                    }
                    else if (candidateSelectedEmphasis == null)
                    {
                        UniversalSelectedEmphasisProvider provider = _selectedEmphasisProvider ??
                            throw new InvalidOperationException(
                                "This Universal bundle has no selected-emphasis provider.");
                        candidateSelectedEmphasis = new(
                            provider.Load(),
                            candidatePlan,
                            Dpi);
                        createdSelectedEmphasis = true;
                    }
                    else
                    {
                        changedSelectedStrength = candidateSelectedEmphasis.EnsureStrength(
                            parameters.HighlightStrength);
                    }
                }

                bool dynamicRebuilt = _dynamicContent!.EnsureUniversal(
                    settings,
                    TargetSize,
                    candidatePlan);
                bool highlightRebuilt = parameters.HighlightStrength != previousHighlight;
                rebuilt = dynamicRebuilt || highlightRebuilt;
                if (rebuilt)
                {
                    Bitmap[] replacement = BuildLegacyFinalStates(
                        Plan,
                        _assetCache!,
                        _dynamicContent,
                        candidateSelectedEmphasis,
                        parameters.HighlightStrength);
                    Bitmap[]? previous = _universalLegacyStates;
                    _universalLegacyStates = replacement;
                    _selectedEmphasis = candidateSelectedEmphasis;
                    if (!ReferenceEquals(previousSelectedEmphasis, candidateSelectedEmphasis))
                        previousSelectedEmphasis?.Dispose();
                    if (previous != null) foreach (Bitmap state in previous) state.Dispose();
                }
            }
            catch
            {
                if (createdSelectedEmphasis)
                    candidateSelectedEmphasis?.Dispose();
                else if (changedSelectedStrength)
                    previousSelectedEmphasis!.EnsureStrength(previousHighlight);
                throw;
            }
        }
        stopwatch.Stop();
        LastUniversalDynamicRebuild = stopwatch.Elapsed;
        if (rebuilt)
        {
            _universalPlan = _universalPlan.WithParameters(parameters);
            RefreshUniversalMetadata(settings);
        }
        return rebuilt;
    }

    public Bitmap GetFinalState(int selectedSlot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fullStateCache != null) return _fullStateCache.GetState(selectedSlot);
        if (_universalLegacyStates == null)
            throw new InvalidOperationException("The production V1 bundle is composed by the overlay.");
        if (selectedSlot < 0 || selectedSlot >= _universalLegacyStates.Length)
            throw new ArgumentOutOfRangeException(nameof(selectedSlot));
        return _universalLegacyStates[selectedSlot];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeCount++;
        _fullStateCache?.Dispose();
        _selectedEmphasis?.Dispose();
        if (_universalLegacyStates != null)
            foreach (Bitmap state in _universalLegacyStates) state.Dispose();
        _dynamicContent?.Dispose();
        _assetCache?.Dispose();
    }

    private void RefreshUniversalMetadata(RadialMenuSettings settings)
    {
        UniversalRadialRenderPlan plan = UniversalPlan;
        ThemeMappingSnapshot mappings = ThemeMappingSnapshot.Capture(settings, plan.SourcePlan.LayoutProfileId);
        string fontEnvironment = _fullStateCache?.FontEnvironment ?? _dynamicContent!.FontEnvironment;
        UniversalCacheKey = new(
            plan.SourcePlan.ThemeId,
            plan.SourceProtocolVersion,
            plan.SourcePackageRevision,
            plan.Parameters.SurfaceScale,
            plan.Parameters.FontScale,
            plan.Parameters.DynamicContentScale,
            plan.Parameters.TextStrength,
            plan.Parameters.HighlightStrength,
            plan.Parameters.SlotContentRadiusCru,
            Dpi,
            mappings,
            fontEnvironment);
    }

    private static Bitmap[] BuildLegacyFinalStates(
        NormalizedRenderPlan plan,
        RadialVisualPackCache assets,
        RadialDynamicContentCache dynamic,
        UniversalSelectedEmphasisCache? selectedEmphasis,
        byte highlightStrength)
    {
        var states = new Bitmap[plan.LayoutDefinition.SlotCount + 1];
        try
        {
            for (int slot = 0; slot < states.Length; slot++)
            {
                var target = new Bitmap(assets.TargetSize, assets.TargetSize, PixelFormat.Format32bppPArgb);
                using Graphics graphics = Graphics.FromImage(target);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                if (slot > 0 && highlightStrength is > 0 and < byte.MaxValue)
                {
                    UniversalSelectedEmphasisCache semantic = selectedEmphasis ??
                        throw new InvalidOperationException(
                            "Non-neutral selected emphasis requires a semantic companion cache.");
                    graphics.DrawImageUnscaled(semantic.GetIntermediateSelected(slot), 0, 0);
                }
                else
                {
                    graphics.DrawImageUnscaled(assets.ScaledBase, 0, 0);
                    if (slot > 0 && highlightStrength == byte.MaxValue)
                    {
                        graphics.CompositingMode = CompositingMode.SourceOver;
                        graphics.DrawImageUnscaled(assets.GetSelectedSlot(slot), 0, 0);
                    }
                }
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.DrawImageUnscaled(dynamic.Content, 0, 0);
                states[slot] = target;
            }
            return states;
        }
        catch
        {
            foreach (Bitmap? state in states) state?.Dispose();
            throw;
        }
    }

    private static void ValidateLegacy(NormalizedRenderPlan plan, RadialVisualPackCache assets,
        RadialDynamicContentCache dynamic, int targetSize)
    {
        if (!ReferenceEquals(plan, assets.Plan) || !ReferenceEquals(plan, dynamic.Plan) ||
            assets.TargetSize != targetSize || assets.ScaledBase.Size != new Size(targetSize, targetSize) ||
            assets.ScaledBase.PixelFormat != PixelFormat.Format32bppPArgb ||
            assets.SelectedSlotCount != plan.LayoutDefinition.SlotCount ||
            dynamic.Content.Size != new Size(targetSize, targetSize) ||
            dynamic.Content.PixelFormat != PixelFormat.Format32bppPArgb)
            throw new InvalidDataException("Runtime render bundle failed completeness validation.");
    }
}
