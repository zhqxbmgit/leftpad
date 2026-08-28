using System.Drawing.Imaging;

namespace PcDs4Server;

internal delegate RuntimeRenderBundle RuntimeRenderBundleBuilder(
    NormalizedRenderPlan plan,
    RadialMenuSettings settings,
    int targetSize);

internal sealed class RuntimeRenderBundle : IDisposable
{
    private bool _disposed;

    private RuntimeRenderBundle(
        NormalizedRenderPlan plan,
        RadialVisualPackCache assetCache,
        RadialDynamicContentCache dynamicContent)
    {
        Plan = plan;
        AssetCache = assetCache;
        DynamicContent = dynamicContent;
    }

    public NormalizedRenderPlan Plan { get; }
    public int TargetSize => AssetCache.TargetSize;
    public RadialVisualPackCache AssetCache { get; }
    public RadialDynamicContentCache DynamicContent { get; }
    internal bool IsDisposed => _disposed;
    internal int DisposeCount { get; private set; }

    public static RuntimeRenderBundle Build(
        NormalizedRenderPlan plan,
        RadialMenuSettings settings,
        int targetSize)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

        var assetCache = new RadialVisualPackCache(plan, targetSize);
        RadialDynamicContentCache? dynamicContent = null;
        try
        {
            dynamicContent = new RadialDynamicContentCache(
                plan,
                WindowsUiFontResolver.ResolveUiFontFamily());
            dynamicContent.Ensure(settings, targetSize);
            ValidateComplete(plan, assetCache, dynamicContent, targetSize);
            return new RuntimeRenderBundle(plan, assetCache, dynamicContent);
        }
        catch
        {
            dynamicContent?.Dispose();
            assetCache.Dispose();
            throw;
        }
    }

    public void EnsureDynamicContent(RadialMenuSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DynamicContent.Ensure(settings, TargetSize);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeCount++;
        DynamicContent.Dispose();
        AssetCache.Dispose();
    }

    private static void ValidateComplete(
        NormalizedRenderPlan plan,
        RadialVisualPackCache assetCache,
        RadialDynamicContentCache dynamicContent,
        int targetSize)
    {
        if (!ReferenceEquals(plan, assetCache.Plan) ||
            !ReferenceEquals(plan, dynamicContent.Plan) ||
            assetCache.TargetSize != targetSize ||
            assetCache.ScaledBase.Size != new Size(targetSize, targetSize) ||
            assetCache.ScaledBase.PixelFormat != PixelFormat.Format32bppPArgb ||
            assetCache.SelectedSlotCount != plan.LayoutDefinition.SlotCount ||
            dynamicContent.Content.Size != new Size(targetSize, targetSize) ||
            dynamicContent.Content.PixelFormat != PixelFormat.Format32bppPArgb)
        {
            throw new InvalidDataException("Runtime render bundle failed completeness validation.");
        }
    }
}
