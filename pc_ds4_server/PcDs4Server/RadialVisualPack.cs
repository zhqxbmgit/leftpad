using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace PcDs4Server;

public static class RadialVisualPackContract
{
    public const string DefaultVisualPackId = "radial-v5";
    public const int SupportedManifestVersion = 1;
    public const string SupportedLayoutProfile = "radial-6";
    public const int SupportedSlotCount = 6;
    public const string SupportedSelectionAssetMode = "canonical-transform";

    public static string DiscoveryRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "UIVisualPacks");
}

public sealed record UiVisualPackManifest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public string LayoutProfile { get; init; } = string.Empty;
    public int SlotCount { get; init; }
    public string Base { get; init; } = string.Empty;
    public string Selected { get; init; } = string.Empty;
    public string Layout { get; init; } = string.Empty;
    public string SelectionAssetMode { get; init; } = string.Empty;
}

public sealed record RadialLayoutDocument
{
    public RadialLayoutCanvas Canvas { get; init; } = new();
    public int SlotCount { get; init; }
    public double[] SlotAnglesDegrees { get; init; } = Array.Empty<double>();
    public RadialLayoutSlot[] Slots { get; init; } = Array.Empty<RadialLayoutSlot>();
    public RadialLayoutPoint WheelCenter { get; init; } = new();
    public RadialLayoutPoint? CenterTextAnchor { get; init; }
}

public sealed record RadialLayoutCanvas
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string Mode { get; init; } = string.Empty;
}

public sealed record RadialLayoutPoint
{
    public double X { get; init; }
    public double Y { get; init; }
}

public sealed record RadialLayoutSlot
{
    public int Slot { get; init; }
    public string Name { get; init; } = string.Empty;
    public double AngleDegreesClockwiseFromTop { get; init; }
    public RadialLayoutPoint GlyphAnchor { get; init; } = new();
    public RadialLayoutPoint LabelAnchor { get; init; } = new();
}

public sealed class RadialVisualPackDefinition
{
    public const int ExpectedMasterSize = 1254;
    public const int ExpectedSlotCount = RadialVisualPackContract.SupportedSlotCount;
    public const double ExpectedCenter = 627d;

    private static readonly double[] ExpectedSlotAngles = { 0d, 60d, 120d, 180d, 240d, 300d };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private RadialVisualPackDefinition(
        string directoryPath,
        UiVisualPackManifest manifest,
        RadialLayoutDocument layout,
        string basePath,
        string selectedPath,
        string layoutPath)
    {
        DirectoryPath = directoryPath;
        Manifest = manifest;
        Layout = layout;
        BasePath = basePath;
        SelectedPath = selectedPath;
        LayoutPath = layoutPath;
    }

    public string DirectoryPath { get; }
    public UiVisualPackManifest Manifest { get; }
    public RadialLayoutDocument Layout { get; }
    public string BasePath { get; }
    public string SelectedPath { get; }
    public string LayoutPath { get; }

    public static string DefaultDirectory => Path.Combine(
        RadialVisualPackContract.DiscoveryRoot,
        RadialVisualPackContract.DefaultVisualPackId);

    public static RadialVisualPackDefinition Load(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string fullDirectory = Path.GetFullPath(directoryPath);
        UiVisualPackManifest manifest = ReadManifest(fullDirectory);
        ValidateManifest(manifest);

        string basePath = ResolveAssetPath(fullDirectory, manifest.Base);
        string selectedPath = ResolveAssetPath(fullDirectory, manifest.Selected);
        string layoutPath = ResolveAssetPath(fullDirectory, manifest.Layout);
        RequireFile(basePath);
        RequireFile(selectedPath);
        RequireFile(layoutPath);

        RadialLayoutDocument layout = Deserialize<RadialLayoutDocument>(layoutPath);
        ValidateLayout(layout);

        return new RadialVisualPackDefinition(
            fullDirectory,
            manifest,
            layout,
            basePath,
            selectedPath,
            layoutPath);
    }

    internal static UiVisualPackManifest ReadManifest(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string manifestPath = Path.Combine(Path.GetFullPath(directoryPath), "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("UI visual pack manifest was not found.", manifestPath);
        return Deserialize<UiVisualPackManifest>(manifestPath);
    }

    public double GetRotationDegrees(int slot)
    {
        if (slot is < 1 or > ExpectedSlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return Layout.SlotAnglesDegrees[slot - 1];
    }

    public PointF ScalePoint(RadialLayoutPoint point, int targetSize)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        float scale = targetSize / (float)Layout.Canvas.Width;
        return new PointF((float)point.X * scale, (float)point.Y * scale);
    }

    private static T Deserialize<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"JSON file is empty: {path}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid JSON file: {path}", exception);
        }
    }

    private static void ValidateManifest(UiVisualPackManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidDataException("Visual pack id is required.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("Visual pack name is required.");
        if (manifest.Version != RadialVisualPackContract.SupportedManifestVersion)
            throw new InvalidDataException($"Unsupported visual pack version: {manifest.Version}");
        if (!string.Equals(
                manifest.LayoutProfile,
                RadialVisualPackContract.SupportedLayoutProfile,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported layout profile: {manifest.LayoutProfile}");
        }
        if (manifest.SlotCount != RadialVisualPackContract.SupportedSlotCount)
            throw new InvalidDataException($"Visual pack must contain {ExpectedSlotCount} slots.");
        if (!string.Equals(
                manifest.SelectionAssetMode,
                RadialVisualPackContract.SupportedSelectionAssetMode,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported selection asset mode: {manifest.SelectionAssetMode}");
        }
    }

    private static void ValidateLayout(RadialLayoutDocument layout)
    {
        if (layout.Canvas.Width != ExpectedMasterSize ||
            layout.Canvas.Height != ExpectedMasterSize ||
            !string.Equals(layout.Canvas.Mode, "RGBA", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Radial layout canvas must be {ExpectedMasterSize}x{ExpectedMasterSize} RGBA.");
        }
        if (layout.SlotCount != ExpectedSlotCount ||
            layout.SlotAnglesDegrees.Length != ExpectedSlotCount ||
            layout.Slots.Length != ExpectedSlotCount)
        {
            throw new InvalidDataException($"Radial layout must contain {ExpectedSlotCount} slots.");
        }
        if (!NearlyEqual(layout.WheelCenter.X, ExpectedCenter) ||
            !NearlyEqual(layout.WheelCenter.Y, ExpectedCenter))
        {
            throw new InvalidDataException("Radial layout center must be 627,627.");
        }

        for (int index = 0; index < ExpectedSlotCount; index++)
        {
            double expectedAngle = ExpectedSlotAngles[index];
            RadialLayoutSlot slot = layout.Slots[index];
            if (!NearlyEqual(layout.SlotAnglesDegrees[index], expectedAngle) ||
                slot.Slot != index + 1 ||
                !NearlyEqual(slot.AngleDegreesClockwiseFromTop, expectedAngle))
            {
                throw new InvalidDataException($"Invalid transform mapping for slot {index + 1}.");
            }

            ValidateAnchor(slot.GlyphAnchor, layout.Canvas, $"Slot {index + 1} glyph");
            ValidateAnchor(slot.LabelAnchor, layout.Canvas, $"Slot {index + 1} label");
        }

        if (layout.CenterTextAnchor != null)
            ValidateAnchor(layout.CenterTextAnchor, layout.Canvas, "Center text");
    }

    private static void ValidateAnchor(
        RadialLayoutPoint point,
        RadialLayoutCanvas canvas,
        string name)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X < 0 || point.X > canvas.Width ||
            point.Y < 0 || point.Y > canvas.Height)
        {
            throw new InvalidDataException($"{name} anchor is outside the master canvas.");
        }
    }

    private static string ResolveAssetPath(string directoryPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Visual pack asset names must be local file names.");
        }
        return Path.Combine(directoryPath, fileName);
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Visual pack asset was not found.", path);
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.0001d;
}

internal sealed class RadialVisualPackCache : IDisposable
{
    private readonly Bitmap _baseMaster;
    private readonly Bitmap _selectedMaster;
    private Bitmap[] _selectedSlots = Array.Empty<Bitmap>();
    private bool _disposed;

    public RadialVisualPackCache(RadialVisualPackDefinition definition, int targetSize)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

        Definition = definition;
        _baseMaster = DecodeMaster(definition.BasePath);
        Bitmap? selectedMaster = null;
        try
        {
            selectedMaster = DecodeMaster(definition.SelectedPath);
            _selectedMaster = selectedMaster;
            Rebuild(targetSize);
        }
        catch
        {
            selectedMaster?.Dispose();
            _baseMaster.Dispose();
            throw;
        }
    }

    public RadialVisualPackDefinition Definition { get; }
    public int TargetSize { get; private set; }
    public Bitmap ScaledBase { get; private set; } = null!;

    public void Rebuild(int targetSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        if (TargetSize == targetSize) return;

        Bitmap? scaledBase = null;
        var selectedSlots = new List<Bitmap>(RadialVisualPackDefinition.ExpectedSlotCount);
        try
        {
            scaledBase = ScaleBitmap(_baseMaster, targetSize, rotationDegrees: 0d);
            foreach (int slot in Enumerable.Range(1, RadialVisualPackDefinition.ExpectedSlotCount))
            {
                selectedSlots.Add(ScaleBitmap(
                    _selectedMaster,
                    targetSize,
                    Definition.GetRotationDegrees(slot)));
            }
        }
        catch
        {
            foreach (Bitmap bitmap in selectedSlots) bitmap.Dispose();
            scaledBase?.Dispose();
            throw;
        }

        if (TargetSize > 0)
        {
            ScaledBase.Dispose();
            foreach (Bitmap bitmap in _selectedSlots) bitmap.Dispose();
        }
        ScaledBase = scaledBase;
        _selectedSlots = selectedSlots.ToArray();
        TargetSize = targetSize;
    }

    public Bitmap GetSelectedSlot(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (slot is < 1 or > RadialVisualPackDefinition.ExpectedSlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return _selectedSlots[slot - 1];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ScaledBase.Dispose();
        foreach (Bitmap bitmap in _selectedSlots) bitmap.Dispose();
        _selectedMaster.Dispose();
        _baseMaster.Dispose();
    }

    private static Bitmap DecodeMaster(string path)
    {
        using var source = new Bitmap(path);
        if (source.Width != RadialVisualPackDefinition.ExpectedMasterSize ||
            source.Height != RadialVisualPackDefinition.ExpectedMasterSize)
        {
            throw new InvalidDataException(
                $"Visual pack bitmap must be {RadialVisualPackDefinition.ExpectedMasterSize}x" +
                $"{RadialVisualPackDefinition.ExpectedMasterSize}: {path}");
        }

        var decoded = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(decoded);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(source, 0, 0);
        return decoded;
    }

    private static Bitmap ScaleBitmap(Bitmap source, int targetSize, double rotationDegrees)
    {
        var target = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppPArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(target);
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (rotationDegrees != 0d)
            {
                float center = targetSize / 2f;
                graphics.TranslateTransform(center, center);
                graphics.RotateTransform((float)rotationDegrees);
                graphics.TranslateTransform(-center, -center);
            }

            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, targetSize, targetSize),
                0,
                0,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel,
                attributes);
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }
}

internal readonly record struct RadialActionDisplayText(string Primary)
{
    public static RadialActionDisplayText FromMapping(RadialSlotMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        return mapping.Kind switch
        {
            RadialActionKind.KeyboardKey => new(mapping.Key?.ToString() ?? "?"),
            RadialActionKind.KeyboardShortcut => new(
                RadialActionResolver.FormatShortcut(mapping)),
            RadialActionKind.Ds4Button when RadialDs4ActionCatalog.TryGet(
                mapping.Ds4Button,
                out RadialDs4ActionMapping action) => new(
                    string.Equals(action.Id, "dpad_down", StringComparison.Ordinal)
                        ? "D-Pad Down"
                        : action.DisplayName),
            _ => new("未设置")
        };
    }
}
