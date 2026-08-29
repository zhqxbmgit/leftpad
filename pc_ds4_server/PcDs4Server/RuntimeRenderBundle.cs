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

internal sealed class FullStateFrameCache : IDisposable
{
    private Dictionary<string, Bitmap> _states;
    private Dictionary<string, Bitmap> _dynamicLayers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Bitmap> _authoredLayers = new(StringComparer.Ordinal);
    private readonly FullStateFrameRenderPlan _model;
    private readonly ThemeFontSession? _fontSession;
    private readonly Action<ThemeMappingSnapshot>? _mappingBuildProbe;
    private ThemeMappingSnapshot? _mappingSnapshot;
    private UniversalRadialParameters? _universalParameters;
    private bool _disposed;

    public FullStateFrameCache(NormalizedRenderPlan plan, int dpi,
        IThemeAssetDecoder? decoder = null)
        : this(plan, RadialMenuSettings.Default, dpi, decoder, null) { }

    public FullStateFrameCache(NormalizedRenderPlan plan, RadialMenuSettings settings, int dpi,
        IThemeAssetDecoder? decoder = null,
        Action<ThemeMappingSnapshot>? mappingBuildProbe = null,
        UniversalRadialParameters? universalParameters = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.RenderModel is not FullStateFrameRenderPlan model)
            throw new ArgumentException("A full-state plan is required.", nameof(plan));
        Plan = plan;
        _universalParameters = universalParameters;
        _mappingBuildProbe = mappingBuildProbe;
        Dpi = dpi > 0 ? dpi : RadialDpiScaling.DefaultDpi;
        _model = model;
        double presentationScale = universalParameters?.SurfaceScale ?? 1d;
        PhysicalSurfaceSize = RadialDpiScaling.LogicalSizeToPhysical(
            plan.ReferenceScale.LogicalWidth * presentationScale,
            plan.ReferenceScale.LogicalHeight * presentationScale,
            Dpi);
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
                BuildAuthoredLayerCache(masters);
                _mappingSnapshot = ThemeMappingSnapshot.Capture(settings, plan.LayoutProfileId);
                (_dynamicLayers, states) = BuildDynamicCandidate(_mappingSnapshot, universalParameters);
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
            foreach (Bitmap bitmap in states.Values) bitmap.Dispose();
            foreach (Bitmap bitmap in _dynamicLayers.Values) bitmap.Dispose();
            foreach (Bitmap bitmap in _authoredLayers.Values) bitmap.Dispose();
            _fontSession?.Dispose();
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
        (Dictionary<string, Bitmap> dynamic, Dictionary<string, Bitmap> states) =
            BuildDynamicCandidate(candidateSnapshot, _universalParameters);
        Dictionary<string, Bitmap> previousDynamic = _dynamicLayers;
        Dictionary<string, Bitmap> previousStates = _states;
        _dynamicLayers = dynamic;
        _states = states;
        _mappingSnapshot = candidateSnapshot;
        MappingRebuildCount++;
        foreach (Bitmap bitmap in previousDynamic.Values) bitmap.Dispose();
        foreach (Bitmap bitmap in previousStates.Values) bitmap.Dispose();
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
            parameters.TextStrength != _universalParameters.TextStrength;
        if (!mappingChanged && !styleChanged) return false;

        (Dictionary<string, Bitmap> dynamic, Dictionary<string, Bitmap> states) =
            BuildDynamicCandidate(candidateSnapshot, parameters);
        Dictionary<string, Bitmap> previousDynamic = _dynamicLayers;
        Dictionary<string, Bitmap> previousStates = _states;
        _dynamicLayers = dynamic;
        _states = states;
        _mappingSnapshot = candidateSnapshot;
        _universalParameters = parameters;
        if (mappingChanged) MappingRebuildCount++;
        foreach (Bitmap bitmap in previousDynamic.Values) bitmap.Dispose();
        foreach (Bitmap bitmap in previousStates.Values) bitmap.Dispose();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Bitmap bitmap in _states.Values) bitmap.Dispose();
        foreach (Bitmap bitmap in _dynamicLayers.Values) bitmap.Dispose();
        foreach (Bitmap bitmap in _authoredLayers.Values) bitmap.Dispose();
        _fontSession?.Dispose();
    }

    private Bitmap BuildLegacyCompatibleState(FullStateFrameState state, IReadOnlyDictionary<string, Bitmap> masters)
    {
        var target = new Bitmap(PhysicalSurfaceSize.Width, PhysicalSurfaceSize.Height,
            PixelFormat.Format32bppPArgb);
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
            target.Dispose();
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
            var target = new Bitmap(PhysicalSurfaceSize.Width, PhysicalSurfaceSize.Height, PixelFormat.Format32bppPArgb);
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
            catch { target.Dispose(); throw; }
        }
    }

    private (Dictionary<string, Bitmap> Dynamic, Dictionary<string, Bitmap> States)
        BuildDynamicCandidate(
            ThemeMappingSnapshot mappings,
            UniversalRadialParameters? universalParameters)
    {
        _mappingBuildProbe?.Invoke(mappings);
        var dynamic = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        var states = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        try
        {
            var rasterizer = new ThemeDynamicRasterizer(
                Plan,
                _fontSession!,
                universalParameters: universalParameters);
            foreach (FullStateFrameState state in _model.States.Values.OrderBy(x => x.SlotId ?? 0))
            {
                foreach (FullStateFrameLayer layer in _model.OrderedLayers.Where(x => x.Kind is "dynamicText" or "dynamicGlyph"))
                {
                    string key = DynamicKey(state.Name, layer.Id);
                    Bitmap bitmap = rasterizer.RenderLayer(layer, state, mappings, Dpi, out ThemeDynamicLayoutResult semantic);
                    semantic.Dispose();
                    dynamic.Add(key, bitmap);
                }
                states.Add(state.Name, ComposeDynamicState(state, dynamic));
            }
            DynamicBuildCount++;
            return (dynamic, states);
        }
        catch
        {
            foreach (Bitmap bitmap in dynamic.Values) bitmap.Dispose();
            foreach (Bitmap bitmap in states.Values) bitmap.Dispose();
            throw;
        }
    }

    private Bitmap ComposeDynamicState(FullStateFrameState state, IReadOnlyDictionary<string, Bitmap> dynamic)
    {
        var target = new Bitmap(PhysicalSurfaceSize.Width, PhysicalSurfaceSize.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(target);
            ConfigureGraphics(graphics);
            foreach (FullStateFrameLayer layer in _model.OrderedLayers)
            {
                if (!IsVisible(layer.VisibleStates, state.Name, state.SlotId)) continue;
                Bitmap source;
                if (layer.Kind is "dynamicText" or "dynamicGlyph")
                    source = dynamic[DynamicKey(state.Name, layer.Id)];
                else
                {
                    VerifiedThemeAsset asset = layer.Kind == "staticAsset" ? layer.StaticAsset! : state.Assets[layer.Id];
                    source = _authoredLayers[AuthoredKey(layer.Id, asset.PackagePath)];
                }
                graphics.DrawImageUnscaled(source, 0, 0);
            }
            return target;
        }
        catch { target.Dispose(); throw; }
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
    private static string DynamicKey(string state, string layerId) => state + "\0" + layerId;

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
}

internal sealed class RuntimeRenderBundle : IDisposable
{
    private readonly RadialVisualPackCache? _assetCache;
    private readonly RadialDynamicContentCache? _dynamicContent;
    private readonly FullStateFrameCache? _fullStateCache;
    private Bitmap[]? _universalLegacyStates;
    private UniversalRadialRenderPlan? _universalPlan;
    private bool _disposed;

    private RuntimeRenderBundle(NormalizedRenderPlan plan, RadialVisualPackCache assetCache,
        RadialDynamicContentCache dynamicContent, int dpi,
        UniversalRadialRenderPlan? universalPlan = null,
        Bitmap[]? universalLegacyStates = null)
    {
        Plan = plan;
        _assetCache = assetCache;
        _dynamicContent = dynamicContent;
        _universalPlan = universalPlan;
        _universalLegacyStates = universalLegacyStates;
        Dpi = dpi;
        PhysicalSurfaceSize = new(assetCache.TargetSize, assetCache.TargetSize);
    }

    private RuntimeRenderBundle(
        NormalizedRenderPlan plan,
        FullStateFrameCache cache,
        UniversalRadialRenderPlan? universalPlan = null)
    {
        Plan = plan;
        _fullStateCache = cache;
        _universalPlan = universalPlan;
        Dpi = cache.Dpi;
        PhysicalSurfaceSize = cache.PhysicalSurfaceSize;
    }

    public NormalizedRenderPlan Plan { get; }
    public int TargetSize => PhysicalSurfaceSize.Width;
    public int Dpi { get; }
    public Size PhysicalSurfaceSize { get; }
    public bool IsFullStateFrame => _fullStateCache != null;
    public bool IsUniversal => _universalPlan != null;
    public UniversalRadialRenderPlan UniversalPlan => _universalPlan ??
        throw new InvalidOperationException("This bundle uses the production legacy path.");
    public UniversalRadialCacheKey? UniversalCacheKey { get; private set; }
    public UniversalRenderBuildDiagnostics? UniversalDiagnostics { get; private set; }
    public TimeSpan LastUniversalDynamicRebuild { get; private set; }
    public int SelectionHotPathWorkCount { get; private set; }
    public RadialVisualPackCache AssetCache => _assetCache ?? throw new InvalidOperationException("This is a V2 full-state bundle.");
    public RadialDynamicContentCache DynamicContent => _dynamicContent ?? throw new InvalidOperationException("This is a V2 full-state bundle.");
    public FullStateFrameCache FullStateCache => _fullStateCache ?? throw new InvalidOperationException("This is a V1 legacy bundle.");
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
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(universalPlan);
        ArgumentNullException.ThrowIfNull(settings);
        var stopwatch = Stopwatch.StartNew();
        NormalizedRenderPlan plan = universalPlan.SourcePlan;
        RuntimeRenderBundle bundle;
        if (plan.RenderModel is FullStateFrameRenderPlan)
        {
            var cache = new FullStateFrameCache(
                plan,
                settings,
                dpi,
                universalParameters: universalPlan.Parameters);
            bundle = new RuntimeRenderBundle(plan, cache, universalPlan);
        }
        else
        {
            Size physical = universalPlan.PhysicalSurfaceSize(dpi);
            if (physical.Width != physical.Height)
                throw new InvalidDataException("The V1 Universal foundation requires a square logical surface.");
            var assets = new RadialVisualPackCache(plan, physical.Width);
            RadialDynamicContentCache? dynamic = null;
            Bitmap[]? states = null;
            try
            {
                dynamic = new RadialDynamicContentCache(plan, WindowsUiFontResolver.ResolveUiFontFamily());
                dynamic.EnsureUniversal(settings, physical.Width, universalPlan.Parameters);
                states = BuildLegacyFinalStates(plan, assets, dynamic);
                bundle = new RuntimeRenderBundle(plan, assets, dynamic, dpi, universalPlan, states);
            }
            catch
            {
                if (states != null) foreach (Bitmap state in states) state.Dispose();
                dynamic?.Dispose();
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
            rebuilt = _dynamicContent!.EnsureUniversal(settings, TargetSize, parameters);
            if (rebuilt)
            {
                Bitmap[] replacement = BuildLegacyFinalStates(Plan, _assetCache!, _dynamicContent);
                Bitmap[]? previous = _universalLegacyStates;
                _universalLegacyStates = replacement;
                if (previous != null) foreach (Bitmap state in previous) state.Dispose();
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
            plan.Parameters.TextStrength,
            Dpi,
            mappings,
            fontEnvironment);
    }

    private static Bitmap[] BuildLegacyFinalStates(
        NormalizedRenderPlan plan,
        RadialVisualPackCache assets,
        RadialDynamicContentCache dynamic)
    {
        var states = new Bitmap[plan.LayoutDefinition.SlotCount + 1];
        try
        {
            for (int slot = 0; slot < states.Length; slot++)
            {
                var target = new Bitmap(assets.TargetSize, assets.TargetSize, PixelFormat.Format32bppPArgb);
                using Graphics graphics = Graphics.FromImage(target);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(assets.ScaledBase, 0, 0);
                graphics.CompositingMode = CompositingMode.SourceOver;
                if (slot > 0) graphics.DrawImageUnscaled(assets.GetSelectedSlot(slot), 0, 0);
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
