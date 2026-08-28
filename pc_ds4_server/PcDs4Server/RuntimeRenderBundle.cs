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
    private readonly ReadOnlyDictionary<string, Bitmap> _states;
    private readonly FullStateFrameRenderPlan _model;
    private bool _disposed;

    public FullStateFrameCache(NormalizedRenderPlan plan, int dpi,
        IThemeAssetDecoder? decoder = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.RenderModel is not FullStateFrameRenderPlan model)
            throw new ArgumentException("A full-state plan is required.", nameof(plan));
        Plan = plan;
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
            foreach (FullStateFrameState state in model.States.Values.OrderBy(x => x.SlotId ?? 0))
                states.Add(state.Name, BuildState(state, masters));
            _states = new ReadOnlyDictionary<string, Bitmap>(states);
        }
        catch
        {
            foreach (Bitmap bitmap in states.Values) bitmap.Dispose();
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

    public Bitmap GetState(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _states[_model.ResolveState(slot).Name];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Bitmap bitmap in _states.Values) bitmap.Dispose();
    }

    private Bitmap BuildState(FullStateFrameState state, IReadOnlyDictionary<string, Bitmap> masters)
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

    private static bool IsVisible(IReadOnlyList<string> selectors, string state, int? slot) =>
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
            return new RuntimeRenderBundle(plan, new FullStateFrameCache(plan, dpi));

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
        _dynamicContent?.Ensure(settings, TargetSize);
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
