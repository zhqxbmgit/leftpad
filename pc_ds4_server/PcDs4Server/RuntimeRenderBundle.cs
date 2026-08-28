using System.Collections.ObjectModel;
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
    private bool _disposed;

    public FullStateFrameCache(NormalizedRenderPlan plan, int dpi,
        IThemeAssetDecoder? decoder = null)
        : this(plan, RadialMenuSettings.Default, dpi, decoder, null) { }

    public FullStateFrameCache(NormalizedRenderPlan plan, RadialMenuSettings settings, int dpi,
        IThemeAssetDecoder? decoder = null,
        Action<ThemeMappingSnapshot>? mappingBuildProbe = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.RenderModel is not FullStateFrameRenderPlan model)
            throw new ArgumentException("A full-state plan is required.", nameof(plan));
        Plan = plan;
        _mappingBuildProbe = mappingBuildProbe;
        Dpi = dpi > 0 ? dpi : RadialDpiScaling.DefaultDpi;
        _model = model;
        PhysicalSurfaceSize = RadialDpiScaling.LogicalSizeToPhysical(
            plan.ReferenceScale.LogicalWidth, plan.ReferenceScale.LogicalHeight, Dpi);
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
                (_dynamicLayers, states) = BuildDynamicCandidate(_mappingSnapshot);
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
            BuildDynamicCandidate(candidateSnapshot);
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
        BuildDynamicCandidate(ThemeMappingSnapshot mappings)
    {
        _mappingBuildProbe?.Invoke(mappings);
        var dynamic = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        var states = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        try
        {
            var rasterizer = new ThemeDynamicRasterizer(Plan, _fontSession!);
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
        double factor = Math.Min(scale.LogicalWidth / Plan.ReferenceCanvas.Width,
            scale.LogicalHeight / Plan.ReferenceCanvas.Height);
        double left = scale.ContentOrigin.X + bounds.X * factor;
        double top = scale.ContentOrigin.Y + bounds.Y * factor;
        double right = scale.ContentOrigin.X + (bounds.X + bounds.Width) * factor;
        double bottom = scale.ContentOrigin.Y + (bounds.Y + bounds.Height) * factor;
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
    private bool _disposed;

    private RuntimeRenderBundle(NormalizedRenderPlan plan, RadialVisualPackCache assetCache,
        RadialDynamicContentCache dynamicContent, int dpi)
    {
        Plan = plan;
        _assetCache = assetCache;
        _dynamicContent = dynamicContent;
        Dpi = dpi;
        PhysicalSurfaceSize = new(assetCache.TargetSize, assetCache.TargetSize);
    }

    private RuntimeRenderBundle(NormalizedRenderPlan plan, FullStateFrameCache cache)
    {
        Plan = plan;
        _fullStateCache = cache;
        Dpi = cache.Dpi;
        PhysicalSurfaceSize = cache.PhysicalSurfaceSize;
    }

    public NormalizedRenderPlan Plan { get; }
    public int TargetSize => PhysicalSurfaceSize.Width;
    public int Dpi { get; }
    public Size PhysicalSurfaceSize { get; }
    public bool IsFullStateFrame => _fullStateCache != null;
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

    public void EnsureDynamicContent(RadialMenuSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fullStateCache != null) _fullStateCache.EnsureMappings(settings);
        else _dynamicContent?.Ensure(settings, TargetSize);
    }

    public Bitmap GetFinalState(int selectedSlot) => FullStateCache.GetState(selectedSlot);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeCount++;
        _fullStateCache?.Dispose();
        _dynamicContent?.Dispose();
        _assetCache?.Dispose();
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
