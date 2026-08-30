using System.Collections.ObjectModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PcDs4Server;

internal sealed record UniversalSelectedEmphasisAsset(
    string PackagePath,
    string Sha256,
    ReadOnlyMemory<byte> Content,
    string? PArgbPackagePath = null,
    string? PArgbSha256 = null,
    ReadOnlyMemory<byte>? PArgbContent = null)
{
    public Stream OpenRead() => new MemoryStream(Content.ToArray(), writable: false);
}

internal sealed record UniversalSelectedEmphasisSlotDescriptor(
    int SlotId,
    UniversalSelectedEmphasisAsset FullSelectedSource,
    UniversalSelectedEmphasisAsset ExplicitMask);

internal sealed class UniversalSelectedEmphasisDescriptor
{
    public UniversalSelectedEmphasisDescriptor(
        string themeId,
        int sourceProtocolVersion,
        int sourcePackageRevision,
        NormalizedReferenceCanvas referenceCanvas,
        UniversalSelectedEmphasisAsset baseStaticSource,
        IEnumerable<UniversalSelectedEmphasisSlotDescriptor> slots,
        string manifestSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        ArgumentNullException.ThrowIfNull(referenceCanvas);
        ArgumentNullException.ThrowIfNull(baseStaticSource);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestSha256);
        UniversalSelectedEmphasisSlotDescriptor[] values = slots.OrderBy(x => x.SlotId).ToArray();
        if (sourceProtocolVersion <= 0 || sourcePackageRevision <= 0 || values.Length == 0 ||
            values.Select(x => x.SlotId).SequenceEqual(Enumerable.Range(1, values.Length)) is false)
            throw new ArgumentException("Selected-emphasis descriptor identity is incomplete.", nameof(slots));

        ThemeId = themeId;
        SourceProtocolVersion = sourceProtocolVersion;
        SourcePackageRevision = sourcePackageRevision;
        ReferenceCanvas = referenceCanvas;
        BaseStaticSource = baseStaticSource;
        Slots = Array.AsReadOnly(values);
        ManifestSha256 = manifestSha256;
    }

    public string ThemeId { get; }
    public int SourceProtocolVersion { get; }
    public int SourcePackageRevision { get; }
    public NormalizedReferenceCanvas ReferenceCanvas { get; }
    public UniversalSelectedEmphasisAsset BaseStaticSource { get; }
    public ReadOnlyCollection<UniversalSelectedEmphasisSlotDescriptor> Slots { get; }
    public int SlotCount => Slots.Count;
    public string ManifestSha256 { get; }
    public UniversalSelectedEmphasisSlotDescriptor GetSlot(int slotId) =>
        slotId is > 0 && slotId <= Slots.Count
            ? Slots[slotId - 1]
            : throw new ArgumentOutOfRangeException(nameof(slotId));
}

internal static partial class UniversalSelectedEmphasisCatalog
{
    public const int SchemaVersion = 1;
    public const string AssetDirectoryName = "UniversalRadialV3";
    public const string PhaseDirectoryName = "phase3-selected-emphasis";

    public static string DiscoveryRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        AssetDirectoryName,
        PhaseDirectoryName);

    public static UniversalSelectedEmphasisDescriptor LoadForPlan(
        NormalizedRenderPlan plan,
        string? discoveryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string root = Path.GetFullPath(discoveryRoot ?? DiscoveryRoot);
        string themeRoot = Path.Combine(root, plan.ThemeId);
        string manifestPath = Path.Combine(themeRoot, "manifest.json");
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        CompanionManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CompanionManifest>(manifestBytes, JsonOptions) ??
                throw new InvalidDataException("Selected-emphasis manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Selected-emphasis manifest is invalid JSON.", exception);
        }

        if (manifest.SchemaVersion != SchemaVersion ||
            !string.Equals(manifest.ThemeId, plan.ThemeId, StringComparison.Ordinal) ||
            manifest.SourceProtocolVersion != plan.SourceProtocolVersion ||
            manifest.SourcePackageRevision != plan.SourcePackageRevision ||
            manifest.SlotCount != plan.LayoutDefinition.SlotCount ||
            manifest.ReferenceCanvas == null ||
            manifest.ReferenceCanvas.Width != plan.ReferenceCanvas.Width ||
            manifest.ReferenceCanvas.Height != plan.ReferenceCanvas.Height ||
            !string.Equals(manifest.ReferenceCanvas.ColorMode, plan.ReferenceCanvas.ColorMode, StringComparison.Ordinal) ||
            manifest.BaseStatic == null || manifest.Selected == null ||
            manifest.Selected.Length != plan.LayoutDefinition.SlotCount)
        {
            throw new InvalidDataException(
                $"Selected-emphasis companion does not match render plan '{plan.ThemeId}'.");
        }

        UniversalSelectedEmphasisAsset baseStatic = LoadAsset(
            themeRoot, manifest.BaseStatic, requirePArgb: true);
        UniversalSelectedEmphasisSlotDescriptor[] slots = manifest.Selected
            .OrderBy(x => x.SlotId)
            .Select((slot, index) =>
            {
                if (slot.SlotId != index + 1 || slot.Source == null || slot.Mask == null)
                    throw new InvalidDataException("Selected-emphasis slot ownership is incomplete.");
                return new UniversalSelectedEmphasisSlotDescriptor(
                    slot.SlotId,
                    LoadAsset(themeRoot, slot.Source, requirePArgb: true),
                    LoadAsset(themeRoot, slot.Mask, requirePArgb: false));
            })
            .ToArray();
        return new(
            manifest.ThemeId,
            manifest.SourceProtocolVersion,
            manifest.SourcePackageRevision,
            new(manifest.ReferenceCanvas.Width, manifest.ReferenceCanvas.Height,
                manifest.ReferenceCanvas.ColorMode),
            baseStatic,
            slots,
            Convert.ToHexString(SHA256.HashData(manifestBytes)));
    }

    private static UniversalSelectedEmphasisAsset LoadAsset(
        string themeRoot,
        CompanionAsset asset,
        bool requirePArgb)
    {
        if (string.IsNullOrWhiteSpace(asset.Path) ||
            !PackagePathPattern().IsMatch(asset.Path) ||
            asset.Path.Contains('\\') || asset.Path.Contains(':') ||
            string.IsNullOrWhiteSpace(asset.Sha256) || !Sha256Pattern().IsMatch(asset.Sha256))
            throw new InvalidDataException("Selected-emphasis asset identity is invalid.");
        (byte[] bytes, string actual) = LoadVerifiedContent(
            themeRoot, asset.Path, asset.Sha256);
        if (!requirePArgb)
            return new(asset.Path, actual, bytes);
        if (string.IsNullOrWhiteSpace(asset.PArgbPath) ||
            string.IsNullOrWhiteSpace(asset.PArgbSha256) ||
            !PackagePathPattern().IsMatch(asset.PArgbPath) ||
            asset.PArgbPath.Contains('\\') || asset.PArgbPath.Contains(':') ||
            !Sha256Pattern().IsMatch(asset.PArgbSha256))
            throw new InvalidDataException("Selected-emphasis PArgb identity is invalid.");
        (byte[] pargb, string pargbActual) = LoadVerifiedContent(
            themeRoot, asset.PArgbPath, asset.PArgbSha256);
        return new(asset.Path, actual, bytes, asset.PArgbPath, pargbActual, pargb);
    }

    private static (byte[] Content, string Sha256) LoadVerifiedContent(
        string themeRoot,
        string packagePath,
        string expectedSha256)
    {
        string normalized = packagePath.Replace('/', Path.DirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(themeRoot, normalized));
        string containmentRoot = Path.GetFullPath(themeRoot) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(containmentRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected-emphasis asset escapes its companion directory.");
        byte[] bytes = File.ReadAllBytes(path);
        string actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Selected-emphasis asset hash mismatch: {packagePath}");
        return (bytes, actual);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex("^(?!/)(?!.*(?:^|/)\\.\\.(?:/|$))[A-Za-z0-9._/-]+$")]
    private static partial Regex PackagePathPattern();

    [GeneratedRegex("^[A-Fa-f0-9]{64}$")]
    private static partial Regex Sha256Pattern();

    private sealed record CompanionManifest
    {
        public int SchemaVersion { get; init; }
        public string ThemeId { get; init; } = string.Empty;
        public int SourceProtocolVersion { get; init; }
        public int SourcePackageRevision { get; init; }
        public int SlotCount { get; init; }
        public CompanionCanvas? ReferenceCanvas { get; init; }
        public CompanionAsset? BaseStatic { get; init; }
        public CompanionSlot[]? Selected { get; init; }
    }

    private sealed record CompanionCanvas
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public string ColorMode { get; init; } = string.Empty;
    }

    private sealed record CompanionAsset
    {
        public string Path { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
        public string? PArgbPath { get; init; }
        public string? PArgbSha256 { get; init; }
    }

    private sealed record CompanionSlot
    {
        public int SlotId { get; init; }
        public CompanionAsset? Source { get; init; }
        public CompanionAsset? Mask { get; init; }
    }
}

internal sealed class UniversalSelectedEmphasisProvider
{
    private readonly NormalizedRenderPlan _plan;
    private readonly string? _discoveryRoot;

    public UniversalSelectedEmphasisProvider(
        NormalizedRenderPlan plan,
        string? discoveryRoot = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _discoveryRoot = discoveryRoot;
    }

    public int ManifestReadCount { get; private set; }

    public UniversalSelectedEmphasisDescriptor Load()
    {
        ManifestReadCount++;
        try
        {
            return UniversalSelectedEmphasisCatalog.LoadForPlan(_plan, _discoveryRoot);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException(
                $"Selected-emphasis semantic companion unavailable for theme '{_plan.ThemeId}'.",
                exception);
        }
    }
}

internal sealed class UniversalSelectedEmphasisCache : IDisposable
{
    private readonly UniversalSelectedEmphasisDescriptor _descriptor;
    private readonly UniversalRadialRenderPlan _plan;
    private readonly PArgbRaster _baseSource;
    private readonly PArgbRaster[] _selectedSources;
    private readonly byte[][] _masks;
    private Bitmap[] _intermediateSelected = Array.Empty<Bitmap>();
    private bool _disposed;

    public UniversalSelectedEmphasisCache(
        UniversalSelectedEmphasisDescriptor descriptor,
        UniversalRadialRenderPlan plan,
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(descriptor.ThemeId, plan.SourcePlan.ThemeId, StringComparison.Ordinal) ||
            descriptor.SourceProtocolVersion != plan.SourceProtocolVersion ||
            descriptor.SourcePackageRevision != plan.SourcePackageRevision ||
            descriptor.SlotCount != plan.LayoutDefinition.SlotCount ||
            descriptor.ReferenceCanvas != plan.SourcePlan.ReferenceCanvas)
            throw new InvalidDataException("Selected-emphasis descriptor and Universal plan do not match.");

        _descriptor = descriptor;
        _plan = plan;
        Dpi = dpi > 0 ? dpi : RadialDpiScaling.DefaultDpi;
        _baseSource = DecodePArgb(descriptor.BaseStaticSource, descriptor.ReferenceCanvas);
        _selectedSources = new PArgbRaster[descriptor.SlotCount];
        _masks = new byte[descriptor.SlotCount][];
        for (int index = 0; index < descriptor.SlotCount; index++)
        {
            UniversalSelectedEmphasisSlotDescriptor slot = descriptor.Slots[index];
            _selectedSources[index] = DecodePArgb(slot.FullSelectedSource, descriptor.ReferenceCanvas);
            _masks[index] = DecodeBinaryMask(slot.ExplicitMask, descriptor.ReferenceCanvas);
        }
        Bitmap baseStatic;
        using (Bitmap source = CreateBitmap(_baseSource))
            baseStatic = ScaleToPhysicalSurface(source);
        try
        {
            DecodedAssetCount = 1 + descriptor.SlotCount * 2;
            HighlightStrength = plan.Parameters.HighlightStrength;
            RebuildIntermediate(HighlightStrength);
            BaseStatic = baseStatic;
        }
        catch
        {
            baseStatic.Dispose();
            throw;
        }
    }

    public UniversalSelectedEmphasisDescriptor Descriptor => _descriptor;
    public int Dpi { get; }
    public int DecodedAssetCount { get; }
    public int ArtworkBuildCount { get; private set; }
    public byte HighlightStrength { get; private set; }
    public bool HasIntermediateArtwork => HighlightStrength is > 0 and < byte.MaxValue;
    public Bitmap BaseStatic { get; }

    public Bitmap GetIntermediateSelected(int slotId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasIntermediateArtwork)
            throw new InvalidOperationException("Endpoint strengths use the frozen identity fast paths.");
        if (slotId <= 0 || slotId > _intermediateSelected.Length)
            throw new ArgumentOutOfRangeException(nameof(slotId));
        return _intermediateSelected[slotId - 1];
    }

    public bool EnsureStrength(byte strength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (HighlightStrength == strength) return false;
        RebuildIntermediate(strength);
        HighlightStrength = strength;
        return true;
    }

    internal Bitmap BuildSourceArtwork(int slotId, byte strength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (slotId <= 0 || slotId > _selectedSources.Length)
            throw new ArgumentOutOfRangeException(nameof(slotId));
        return CreateBitmap(Interpolate(
            _baseSource,
            _selectedSources[slotId - 1],
            _masks[slotId - 1],
            strength));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BaseStatic.Dispose();
        foreach (Bitmap bitmap in _intermediateSelected) bitmap.Dispose();
        _intermediateSelected = Array.Empty<Bitmap>();
    }

    private void RebuildIntermediate(byte strength)
    {
        Bitmap[] replacement = Array.Empty<Bitmap>();
        if (strength is > 0 and < byte.MaxValue)
        {
            replacement = new Bitmap[_selectedSources.Length];
            try
            {
                for (int index = 0; index < replacement.Length; index++)
                {
                    PArgbRaster interpolated = Interpolate(
                        _baseSource,
                        _selectedSources[index],
                        _masks[index],
                        strength);
                    using Bitmap source = CreateBitmap(interpolated);
                    replacement[index] = ScaleToPhysicalSurface(source);
                }
            }
            catch
            {
                foreach (Bitmap? bitmap in replacement) bitmap?.Dispose();
                throw;
            }
            ArtworkBuildCount++;
        }
        Bitmap[] previous = _intermediateSelected;
        _intermediateSelected = replacement;
        foreach (Bitmap bitmap in previous) bitmap.Dispose();
    }

    private Bitmap ScaleToPhysicalSurface(Bitmap source)
    {
        Size size = _plan.PhysicalSurfaceSize(Dpi);
        var target = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(target);
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(source, PhysicalReferenceBounds(), 0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel, attributes);
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    private Rectangle PhysicalReferenceBounds()
    {
        NormalizedReferenceScale scale = _plan.SourcePlan.ReferenceScale;
        NormalizedReferenceCanvas canvas = _plan.SourcePlan.ReferenceCanvas;
        double presentationScale = _plan.Parameters.SurfaceScale;
        double factor = Math.Min(
            scale.LogicalWidth / canvas.Width,
            scale.LogicalHeight / canvas.Height) * presentationScale;
        double left = scale.ContentOrigin.X * presentationScale;
        double top = scale.ContentOrigin.Y * presentationScale;
        double right = left + canvas.Width * factor;
        double bottom = top + canvas.Height * factor;
        return RadialDpiScaling.LogicalRectToPhysical(left, top, right, bottom, Dpi);
    }

    private static PArgbRaster DecodePArgb(
        UniversalSelectedEmphasisAsset asset,
        NormalizedReferenceCanvas canvas)
    {
        if (asset.PArgbContent is not { } content)
            throw new InvalidDataException($"Selected-emphasis source has no PArgb authority: {asset.PackagePath}");
        ReadOnlySpan<byte> bytes = content.Span;
        if (bytes.Length <= 13 ||
            !bytes[..5].SequenceEqual("PARGZ"u8) ||
            BitConverter.ToUInt32(bytes.Slice(5, 4)) != canvas.Width ||
            BitConverter.ToUInt32(bytes.Slice(9, 4)) != canvas.Height)
            throw new InvalidDataException($"Selected-emphasis PArgb geometry is invalid: {asset.PArgbPackagePath}");
        byte[] pixels;
        using (var compressed = new MemoryStream(bytes[13..].ToArray(), writable: false))
        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress))
        using (var expanded = new MemoryStream())
        {
            inflater.CopyTo(expanded);
            pixels = expanded.ToArray();
        }
        if (pixels.Length != canvas.Width * canvas.Height * 4)
            throw new InvalidDataException($"Selected-emphasis PArgb payload is invalid: {asset.PArgbPackagePath}");
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            byte alpha = pixels[offset + 3];
            if (pixels[offset] > alpha || pixels[offset + 1] > alpha || pixels[offset + 2] > alpha ||
                alpha == 0 && (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0))
                throw new InvalidDataException("Selected-emphasis source violates the PArgb invariant.");
        }
        return new(canvas.Width, canvas.Height, pixels);
    }

    private static byte[] DecodeBinaryMask(
        UniversalSelectedEmphasisAsset asset,
        NormalizedReferenceCanvas canvas)
    {
        using Stream stream = asset.OpenRead();
        using var source = new Bitmap(stream);
        if (source.Width != canvas.Width || source.Height != canvas.Height)
            throw new InvalidDataException($"Selected-emphasis mask has invalid dimensions: {asset.PackagePath}");
        using var decoded = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(decoded))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }
        PArgbRaster raster = ReadPArgb(decoded, requireInvariant: true);
        var mask = new byte[canvas.Width * canvas.Height];
        for (int pixel = 0, offset = 0; pixel < mask.Length; pixel++, offset += 4)
        {
            byte blue = raster.Bytes[offset];
            byte green = raster.Bytes[offset + 1];
            byte red = raster.Bytes[offset + 2];
            byte alpha = raster.Bytes[offset + 3];
            if (alpha != byte.MaxValue || blue != green || green != red || blue is not (0 or byte.MaxValue))
                throw new InvalidDataException(
                    $"Selected-emphasis masks must contain only opaque 0/255 coverage: {asset.PackagePath}");
            mask[pixel] = blue;
        }
        return mask;
    }

    private static PArgbRaster Interpolate(
        PArgbRaster baseSource,
        PArgbRaster selectedSource,
        IReadOnlyList<byte> mask,
        byte strength)
    {
        if (baseSource.Width != selectedSource.Width || baseSource.Height != selectedSource.Height ||
            baseSource.Bytes.Length != selectedSource.Bytes.Length ||
            mask.Count != baseSource.Width * baseSource.Height)
            throw new InvalidDataException("Selected-emphasis rasters do not share one reference geometry.");
        byte[] result = new byte[baseSource.Bytes.Length];
        if (strength == 0)
        {
            Buffer.BlockCopy(baseSource.Bytes, 0, result, 0, result.Length);
            return new(baseSource.Width, baseSource.Height, result);
        }

        int inverse = byte.MaxValue - strength;
        for (int pixel = 0, offset = 0; pixel < mask.Count; pixel++, offset += 4)
        {
            if (mask[pixel] == 0)
            {
                result[offset] = baseSource.Bytes[offset];
                result[offset + 1] = baseSource.Bytes[offset + 1];
                result[offset + 2] = baseSource.Bytes[offset + 2];
                result[offset + 3] = baseSource.Bytes[offset + 3];
                continue;
            }
            for (int channel = 0; channel < 4; channel++)
            {
                int value = baseSource.Bytes[offset + channel] * inverse +
                    selectedSource.Bytes[offset + channel] * strength;
                result[offset + channel] = checked((byte)((value + 127) / byte.MaxValue));
            }
        }
        return new(baseSource.Width, baseSource.Height, result);
    }

    private static PArgbRaster ReadPArgb(Bitmap bitmap, bool requireInvariant)
    {
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = new byte[bitmap.Width * bitmap.Height * 4];
            int rowBytes = bitmap.Width * 4;
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * rowBytes, rowBytes);
            if (requireInvariant)
            {
                for (int offset = 0; offset < bytes.Length; offset += 4)
                {
                    byte alpha = bytes[offset + 3];
                    if (bytes[offset] > alpha || bytes[offset + 1] > alpha || bytes[offset + 2] > alpha ||
                        alpha == 0 && (bytes[offset] != 0 || bytes[offset + 1] != 0 || bytes[offset + 2] != 0))
                        throw new InvalidDataException("Selected-emphasis source violates the PArgb invariant.");
                }
            }
            return new(bitmap.Width, bitmap.Height, bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap CreateBitmap(PArgbRaster raster)
    {
        var bitmap = new Bitmap(raster.Width, raster.Height, PixelFormat.Format32bppPArgb);
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int rowBytes = raster.Width * 4;
            for (int y = 0; y < raster.Height; y++)
                Marshal.Copy(raster.Bytes, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    private sealed record PArgbRaster(int Width, int Height, byte[] Bytes);
}
