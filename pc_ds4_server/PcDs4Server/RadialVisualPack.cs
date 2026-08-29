using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace PcDs4Server;

public static class RadialVisualPackContract
{
    public const string DefaultVisualPackId = "dark-fantasy-radial8-v1";
    public const string FallbackVisualPackId = "radial-v5";
    public const string LegacyV1DefaultPackId = "radial-v5";
    public const string DefaultMappingProfileId = LayoutProfileRegistry.Radial8ProfileId;
    public const string FallbackMappingProfileId = LayoutProfileRegistry.Radial6ProfileId;
    public const int SupportedManifestVersion = 1;
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
    public const double ExpectedCenter = 627d;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private RadialVisualPackDefinition(
        string directoryPath,
        UiVisualPackManifest manifest,
        RadialLayoutDocument layout,
        LayoutDefinition layoutDefinition,
        string basePath,
        string selectedPath,
        string layoutPath)
    {
        DirectoryPath = directoryPath;
        Manifest = manifest;
        Layout = layout;
        LayoutDefinition = layoutDefinition;
        BasePath = basePath;
        SelectedPath = selectedPath;
        LayoutPath = layoutPath;
    }

    public string DirectoryPath { get; }
    public UiVisualPackManifest Manifest { get; }
    public RadialLayoutDocument Layout { get; }
    public LayoutDefinition LayoutDefinition { get; }
    public string BasePath { get; }
    public string SelectedPath { get; }
    public string LayoutPath { get; }

    public static string DefaultDirectory => Path.Combine(
        RadialVisualPackContract.DiscoveryRoot,
        RadialVisualPackContract.LegacyV1DefaultPackId);

    public static RadialVisualPackDefinition Load(string directoryPath)
    {
        RadialVisualPackDefinition definition = Parse(directoryPath);
        definition.EnsureRuntimeSessionSupported();
        return definition;
    }

    public static RadialVisualPackDefinition Parse(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string fullDirectory = Path.GetFullPath(directoryPath);
        UiVisualPackManifest manifest = ReadManifest(fullDirectory);
        LayoutProfileRegistration profile = ValidateManifest(manifest);

        string basePath = ResolveAssetPath(fullDirectory, manifest.Base);
        string selectedPath = ResolveAssetPath(fullDirectory, manifest.Selected);
        string layoutPath = ResolveAssetPath(fullDirectory, manifest.Layout);
        RequireFile(basePath);
        RequireFile(selectedPath);
        RequireFile(layoutPath);

        RadialLayoutDocument layout = Deserialize<RadialLayoutDocument>(layoutPath);
        LayoutDefinition layoutDefinition = CreateLayoutDefinition(
            profile,
            manifest.SelectionAssetMode,
            layout);

        return new RadialVisualPackDefinition(
            fullDirectory,
            manifest,
            layout,
            layoutDefinition,
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
        if (slot < 1 || slot > LayoutDefinition.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return LayoutDefinition.Slots[slot - 1].AngleDegrees;
    }

    public PointF ScalePoint(RadialLayoutPoint point, int targetSize)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        float scale = targetSize / (float)Layout.Canvas.Width;
        return new PointF((float)point.X * scale, (float)point.Y * scale);
    }

    public PointF ScalePoint(LayoutPointDefinition point, int targetSize)
    {
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        float scale = targetSize / (float)LayoutDefinition.Canvas.Width;
        return new PointF((float)point.X * scale, (float)point.Y * scale);
    }

    internal void EnsureRuntimeSessionSupported()
    {
        LayoutProfileRegistration profile = LayoutProfileRegistry.GetRequired(
            LayoutDefinition.ProfileId);
        if (!profile.RuntimeSessionSupported)
        {
            throw new InvalidDataException(
                $"Layout profile is validated but not Runtime integrated: {profile.ProfileId}");
        }
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

    private static LayoutProfileRegistration ValidateManifest(UiVisualPackManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidDataException("Visual pack id is required.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("Visual pack name is required.");
        if (manifest.Version != RadialVisualPackContract.SupportedManifestVersion)
            throw new InvalidDataException($"Unsupported visual pack version: {manifest.Version}");
        LayoutProfileRegistration profile = LayoutProfileRegistry.GetRequired(manifest.LayoutProfile);
        if (manifest.SlotCount != profile.SlotCount)
            throw new InvalidDataException($"Visual pack must contain {profile.SlotCount} slots.");
        if (!string.Equals(
                manifest.SelectionAssetMode,
                RadialVisualPackContract.SupportedSelectionAssetMode,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported selection asset mode: {manifest.SelectionAssetMode}");
        }

        return profile;
    }

    private static LayoutDefinition CreateLayoutDefinition(
        LayoutProfileRegistration profile,
        string selectionAssetMode,
        RadialLayoutDocument layout)
    {
        if (layout.Canvas.Width != ExpectedMasterSize ||
            layout.Canvas.Height != ExpectedMasterSize ||
            !string.Equals(layout.Canvas.Mode, "RGBA", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Radial layout canvas must be {ExpectedMasterSize}x{ExpectedMasterSize} RGBA.");
        }
        if (layout.SlotCount != profile.SlotCount ||
            layout.SlotAnglesDegrees.Length != profile.SlotCount ||
            layout.Slots.Length != profile.SlotCount)
        {
            throw new InvalidDataException($"Radial layout must contain {profile.SlotCount} slots.");
        }
        if (!NearlyEqual(layout.WheelCenter.X, ExpectedCenter) ||
            !NearlyEqual(layout.WheelCenter.Y, ExpectedCenter))
        {
            throw new InvalidDataException("Radial layout center must be 627,627.");
        }

        var slots = new List<RadialSlotDefinition>(profile.SlotCount);
        for (int index = 0; index < profile.SlotCount; index++)
        {
            double expectedAngle = profile.ExpectedAngles[index];
            RadialLayoutSlot slot = layout.Slots[index];
            if (!NearlyEqual(layout.SlotAnglesDegrees[index], expectedAngle) ||
                slot.Slot != index + 1 ||
                !NearlyEqual(slot.AngleDegreesClockwiseFromTop, expectedAngle))
            {
                throw new InvalidDataException($"Invalid transform mapping for slot {index + 1}.");
            }

            ValidateAnchor(slot.GlyphAnchor, layout.Canvas, $"Slot {index + 1} glyph");
            ValidateAnchor(slot.LabelAnchor, layout.Canvas, $"Slot {index + 1} label");
            slots.Add(new RadialSlotDefinition(
                slot.Slot,
                slot.AngleDegreesClockwiseFromTop,
                new LayoutPointDefinition(slot.GlyphAnchor.X, slot.GlyphAnchor.Y),
                new LayoutPointDefinition(slot.LabelAnchor.X, slot.LabelAnchor.Y)));
        }

        if (layout.CenterTextAnchor != null)
            ValidateAnchor(layout.CenterTextAnchor, layout.Canvas, "Center text");

        return new LayoutDefinition(
            profile.ProfileId,
            profile.Family,
            profile.SlotCount,
            new LayoutCanvasDefinition(
                layout.Canvas.Width,
                layout.Canvas.Height,
                layout.Canvas.Mode),
            new LayoutPointDefinition(layout.WheelCenter.X, layout.WheelCenter.Y),
            profile.SelectionModel,
            selectionAssetMode,
            slots);
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
        : this(V1VisualPackCompatibilityAdapter.BuildPlan(definition), targetSize)
    {
    }

    public RadialVisualPackCache(NormalizedRenderPlan plan, int targetSize)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

        if (plan.RenderModel is not LegacyCanonicalSelectedPlan legacy)
            throw new NotSupportedException($"Unsupported render model: {plan.RenderModel.GetType().Name}");

        Plan = plan;
        _baseMaster = DecodeMaster(legacy.BaseLayer.AssetPath);
        Bitmap? selectedMaster = null;
        try
        {
            selectedMaster = DecodeMaster(legacy.CanonicalSelectedAssetPath);
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

    public NormalizedRenderPlan Plan { get; }
    public int TargetSize { get; private set; }
    public Bitmap ScaledBase { get; private set; } = null!;
    internal int SelectedSlotCount => _selectedSlots.Length;

    public void Rebuild(int targetSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        if (TargetSize == targetSize) return;

        LayoutDefinition layout = Plan.LayoutDefinition;
        Bitmap? scaledBase = null;
        var selectedSlots = new List<Bitmap>(layout.SlotCount);
        Bitmap[] replacementSelectedSlots;
        try
        {
            scaledBase = ScaleBitmap(
                _baseMaster,
                targetSize,
                rotationDegrees: 0d,
                layout);
            foreach (RadialSlotDefinition slot in layout.Slots)
            {
                selectedSlots.Add(ScaleBitmap(
                    _selectedMaster,
                    targetSize,
                    slot.AngleDegrees,
                    layout));
            }
            ValidateReplacement(scaledBase, selectedSlots, targetSize, layout.SlotCount);
            replacementSelectedSlots = selectedSlots.ToArray();
        }
        catch
        {
            foreach (Bitmap bitmap in selectedSlots) bitmap.Dispose();
            scaledBase?.Dispose();
            throw;
        }

        Bitmap? previousBase = TargetSize > 0 ? ScaledBase : null;
        Bitmap[] previousSelectedSlots = _selectedSlots;
        ScaledBase = scaledBase!;
        _selectedSlots = replacementSelectedSlots;
        TargetSize = targetSize;

        previousBase?.Dispose();
        foreach (Bitmap bitmap in previousSelectedSlots) bitmap.Dispose();
    }

    public Bitmap GetSelectedSlot(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (slot < 1 || slot > Plan.LayoutDefinition.SlotCount)
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

    private static void ValidateReplacement(
        Bitmap scaledBase,
        IReadOnlyCollection<Bitmap> selectedSlots,
        int targetSize,
        int expectedSlotCount)
    {
        if (scaledBase.Size != new Size(targetSize, targetSize) ||
            scaledBase.PixelFormat != PixelFormat.Format32bppPArgb)
        {
            throw new InvalidDataException("Scaled Base cache failed validation.");
        }
        if (selectedSlots.Count != expectedSlotCount || selectedSlots.Any(bitmap =>
                bitmap.Size != new Size(targetSize, targetSize) ||
                bitmap.PixelFormat != PixelFormat.Format32bppPArgb))
        {
            throw new InvalidDataException("Scaled Selected cache failed validation.");
        }
    }

    private static Bitmap ScaleBitmap(
        Bitmap source,
        int targetSize,
        double rotationDegrees,
        LayoutDefinition layout)
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
                float centerX = (float)(
                    layout.WheelCenter.X * targetSize / layout.Canvas.Width);
                float centerY = (float)(
                    layout.WheelCenter.Y * targetSize / layout.Canvas.Height);
                graphics.TranslateTransform(centerX, centerY);
                graphics.RotateTransform((float)rotationDegrees);
                graphics.TranslateTransform(-centerX, -centerY);
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
