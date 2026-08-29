using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PcDs4Server;

internal static class WindowsUiFontResolver
{
    internal static readonly string[] CandidateFamilyNames =
    {
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "DengXian",
        "SimHei",
        "Segoe UI"
    };

    private static readonly Lazy<string?> PreferredFamilyName = new(
        FindInstalledPreferredFamily,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static string ResolvedFamilyName =>
        PreferredFamilyName.Value ?? FontFamily.GenericSansSerif.Name;

    public static FontFamily ResolveUiFontFamily()
        => CreateFontFamily(PreferredFamilyName.Value);

    internal static FontFamily CreateFontFamily(string? preferredName)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            try
            {
                return new FontFamily(preferredName);
            }
            catch (ArgumentException)
            {
                // Fall through to the Windows generic sans-serif family.
            }
        }

        return new FontFamily(FontFamily.GenericSansSerif.Name);
    }

    internal static string? SelectPreferredFamilyName(IEnumerable<string> installedNames)
    {
        ArgumentNullException.ThrowIfNull(installedNames);
        var installed = new HashSet<string>(installedNames, StringComparer.OrdinalIgnoreCase);
        return CandidateFamilyNames.FirstOrDefault(installed.Contains);
    }

    private static string? FindInstalledPreferredFamily()
    {
        try
        {
            using var installedFonts = new InstalledFontCollection();
            return SelectPreferredFamilyName(installedFonts.Families.Select(family => family.Name));
        }
        catch (Exception exception) when (
            exception is ArgumentException or ExternalException or InvalidOperationException)
        {
            return null;
        }
    }
}

internal sealed class RadialDynamicContentCache : IDisposable
{
    public const int SupersampleScale = 4;

    private readonly NormalizedRenderPlan _plan;
    private readonly FontFamily _fontFamily;
    private Bitmap? _content;
    private DynamicContentKey? _key;
    private bool _disposed;

    public RadialDynamicContentCache(
        RadialVisualPackDefinition definition,
        FontFamily fontFamily)
        : this(V1VisualPackCompatibilityAdapter.BuildPlan(definition), fontFamily)
    {
    }

    public RadialDynamicContentCache(
        NormalizedRenderPlan plan,
        FontFamily fontFamily)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _fontFamily = fontFamily ?? throw new ArgumentNullException(nameof(fontFamily));
    }

    public Bitmap Content
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _content ?? throw new InvalidOperationException(
                "Dynamic content cache has not been built.");
        }
    }

    internal int BuildCount { get; private set; }
    internal int RenderedSlotCount { get; private set; }
    internal NormalizedRenderPlan Plan => _plan;
    internal string FontEnvironment => _fontFamily.Name;

    public bool Ensure(RadialMenuSettings settings, int targetSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

        RadialMenuRenderMetrics metrics = settings.CreateRenderMetrics();
        float outputScale = targetSize / (float)metrics.CanvasSize;
        var key = new DynamicContentKey(
            targetSize,
            metrics.FontSize * outputScale,
            outputScale,
            settings.TextAlpha,
            UseLegacyTextClamp: true,
            settings.GetProfileMappings(_plan.LayoutDefinition.ProfileId));
        if (_content != null && key == _key) return false;

        Bitmap replacement = BuildContent(key);
        Bitmap? previous = _content;
        _content = replacement;
        _key = key;
        BuildCount++;
        previous?.Dispose();
        return true;
    }

    public bool EnsureUniversal(
        RadialMenuSettings settings,
        int targetSize,
        UniversalRadialParameters parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parameters);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

        float physicalPresentationScale = checked((float)(
            targetSize / _plan.ReferenceScale.LogicalWidth));
        var key = new DynamicContentKey(
            targetSize,
            checked((float)(UniversalRadialParameters.NeutralFontSize *
                parameters.FontScale * physicalPresentationScale)),
            physicalPresentationScale,
            UniversalRadialSettingsNormalizer.ScaleAuthoredAlpha(235, parameters.TextStrength),
            UseLegacyTextClamp: false,
            settings.GetProfileMappings(_plan.LayoutDefinition.ProfileId));
        if (_content != null && key == _key) return false;

        Bitmap replacement = BuildContent(key);
        Bitmap? previous = _content;
        _content = replacement;
        _key = key;
        BuildCount++;
        previous?.Dispose();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _content?.Dispose();
        _content = null;
        _fontFamily.Dispose();
    }

    private Bitmap BuildContent(DynamicContentKey key)
    {
        int workingSize = checked(key.TargetSize * SupersampleScale);
        using var working = new Bitmap(
            workingSize,
            workingSize,
            PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(working))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            DrawSlotContent(graphics, key, workingSize);
        }

        return Downsample(working, key.TargetSize);
    }

    private void DrawSlotContent(Graphics graphics, DynamicContentKey key, int workingSize)
    {
        LayoutDefinition layout = _plan.LayoutDefinition;
        float masterScale = workingSize / (float)layout.Canvas.Width;
        float primaryWidth = 270f * masterScale;
        float primaryHeight = 92f * masterScale;
        float primaryFontSize = Math.Max(
            7.5f * key.OutputScale,
            key.FontPixelSize * 0.72f) * SupersampleScale;
        Color primaryColor = Color.FromArgb(
            key.UseLegacyTextClamp ? Math.Min(key.TextAlpha, 235) : key.TextAlpha,
            0xCC,
            0xD5,
            0xDE);

        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };

        int renderedSlotCount = 0;
        for (int index = 0; index < layout.Slots.Count; index++)
        {
            RadialSlotDefinition slot = layout.Slots[index];
            RadialSlotMapping mapping = index < key.SlotMappings.Count
                ? key.SlotMappings[index]
                : RadialSlotMapping.None;
            RadialActionDisplayText display = RadialActionDisplayText.FromMapping(
                mapping);
            float anchorScale = workingSize / (float)layout.Canvas.Width;
            PointF primaryAnchor = new(
                (float)slot.GlyphAnchor.X * anchorScale,
                (float)slot.GlyphAnchor.Y * anchorScale);

            DrawFittedText(
                graphics,
                display.Primary,
                CenteredBounds(primaryAnchor, primaryWidth, primaryHeight),
                primaryFontSize,
                6f * key.OutputScale * SupersampleScale,
                FontStyle.Bold,
                primaryColor,
                format);
            renderedSlotCount++;
        }
        RenderedSlotCount = renderedSlotCount;
    }

    private void DrawFittedText(
        Graphics graphics,
        string text,
        RectangleF bounds,
        float preferredFontSize,
        float minimumFontSize,
        FontStyle style,
        Color color,
        StringFormat format)
    {
        float fontSize = preferredFontSize;
        Font? font = null;
        try
        {
            while (fontSize > minimumFontSize)
            {
                font?.Dispose();
                font = new Font(_fontFamily, fontSize, style, GraphicsUnit.Pixel);
                SizeF measured = graphics.MeasureString(text, font, int.MaxValue, format);
                if (measured.Width <= bounds.Width && measured.Height <= bounds.Height) break;
                fontSize -= 2f;
            }

            font ??= new Font(_fontFamily, minimumFontSize, style, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            graphics.DrawString(text, font, brush, bounds, format);
        }
        finally
        {
            font?.Dispose();
        }
    }

    private static Bitmap Downsample(Bitmap working, int targetSize)
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
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(
                working,
                new Rectangle(0, 0, targetSize, targetSize),
                0,
                0,
                working.Width,
                working.Height,
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

    private static RectangleF CenteredBounds(PointF center, float width, float height) =>
        new(center.X - (width / 2f), center.Y - (height / 2f), width, height);

    private sealed record DynamicContentKey(
        int TargetSize,
        float FontPixelSize,
        float OutputScale,
        int TextAlpha,
        bool UseLegacyTextClamp,
        RadialSlotMappings SlotMappings);
}
