using System.Collections.ObjectModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PcDs4Server;

internal sealed class ThemeMappingSnapshot : IEquatable<ThemeMappingSnapshot>
{
    private readonly ReadOnlyCollection<RadialSlotMapping> _slots;

    private ThemeMappingSnapshot(IEnumerable<RadialSlotMapping> slots) =>
        _slots = Array.AsReadOnly(slots.Select(x => x.Sanitize() with { }).ToArray());

    public IReadOnlyList<RadialSlotMapping> Slots => _slots;
    public RadialSlotMapping GetSlot(int slot) => slot >= 1 && slot <= _slots.Count
        ? _slots[slot - 1] : RadialSlotMapping.None;

    public static ThemeMappingSnapshot Capture(RadialMenuSettings settings, string profileId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(settings.GetProfileMappings(profileId));
    }

    public bool Equals(ThemeMappingSnapshot? other) => other != null && _slots.SequenceEqual(other._slots);
    public override bool Equals(object? obj) => Equals(obj as ThemeMappingSnapshot);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (RadialSlotMapping slot in _slots) hash.Add(slot);
        return hash.ToHashCode();
    }
}

internal sealed record ResolvedDynamicContent(
    bool IsEmpty,
    string LabelText,
    RadialActionKind ActionKind,
    string GlyphFamily,
    string GlyphId,
    string TextFallback,
    int? SourceSlot)
{
    public static ResolvedDynamicContent Empty { get; } =
        new(true, string.Empty, RadialActionKind.None, "genericAction", string.Empty, string.Empty, null);
}

internal static class ThemeDynamicContentResolver
{
    public static ResolvedDynamicContent Resolve(
        string contentKey, int selectedSlot, ThemeMappingSnapshot mappings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentKey);
        ArgumentNullException.ThrowIfNull(mappings);
        int slot = contentKey.StartsWith("selectedAction", StringComparison.Ordinal)
            ? selectedSlot : ParseSlot(contentKey);
        if (slot == 0) return ResolvedDynamicContent.Empty;
        RadialSlotMapping mapping = mappings.GetSlot(slot);
        if (mapping.Kind == RadialActionKind.None)
            return new(true, string.Empty, RadialActionKind.None, "genericAction",
                string.Empty, string.Empty, slot);
        string text = RadialActionDisplayText.FromMapping(mapping).Primary;
        string family = mapping.Kind switch
        {
            RadialActionKind.KeyboardKey => "keyboard",
            RadialActionKind.KeyboardShortcut => "keyboardShortcut",
            RadialActionKind.Ds4Button => "ds4",
            _ => "genericAction"
        };
        string glyphId = mapping.Kind switch
        {
            RadialActionKind.KeyboardKey => mapping.Key?.ToString().ToUpperInvariant() ?? string.Empty,
            RadialActionKind.KeyboardShortcut => mapping.Key?.ToString().ToUpperInvariant() ?? string.Empty,
            RadialActionKind.Ds4Button when RadialDs4ActionCatalog.TryGet(mapping.Ds4Button, out var action) =>
                action.Id.ToUpperInvariant(),
            _ => string.Empty
        };
        return new(false, text, mapping.Kind, family, glyphId, text, slot);
    }

    private static int ParseSlot(string contentKey)
    {
        if (!contentKey.StartsWith("slot", StringComparison.Ordinal))
            throw new InvalidDataException($"Unknown dynamic content key '{contentKey}'.");
        int action = contentKey.IndexOf("Action", StringComparison.Ordinal);
        if (action <= 4 || !int.TryParse(contentKey.AsSpan(4, action - 4), out int slot))
            throw new InvalidDataException($"Unknown dynamic content key '{contentKey}'.");
        return slot;
    }
}

internal sealed class ThemeFontSession : IDisposable
{
    private readonly ReadOnlyDictionary<string, FontFamily> _families;
    private bool _disposed;

    internal ThemeFontSession(IDictionary<string, FontFamily> families) =>
        _families = new(new Dictionary<string, FontFamily>(families, StringComparer.Ordinal));

    public FontFamily Get(string role)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _families[role];
    }

    public IReadOnlyDictionary<string, string> ResolvedNames =>
        new ReadOnlyDictionary<string, string>(_families.ToDictionary(x => x.Key, x => x.Value.Name,
            StringComparer.Ordinal));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (FontFamily family in _families.Values.Distinct()) family.Dispose();
    }
}

internal static class ThemeFontResolver
{
    private static readonly Lazy<ISet<string>> SessionInstalledNames = new(
        ReadInstalledNames, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly IReadOnlyDictionary<string, string[]> Chains =
        new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ui"] = new[] { "Microsoft YaHei UI", "Segoe UI", "Arial" },
            ["display"] = new[] { "Segoe UI Semibold", "Microsoft YaHei UI", "Segoe UI", "Arial" },
            ["monospace"] = new[] { "Cascadia Mono", "Consolas", "Courier New" },
            ["symbol"] = new[] { "Segoe UI Symbol", "Segoe UI", "Arial" }
        });

    public static ThemeFontSession Resolve(IReadOnlyDictionary<string, string> manifestRoles,
        ISet<string>? installedNames = null)
    {
        installedNames ??= SessionInstalledNames.Value;
        var families = new Dictionary<string, FontFamily>(StringComparer.Ordinal);
        try
        {
            foreach ((string role, string abstractRole) in manifestRoles)
            {
                string name = SelectFamilyName(abstractRole, installedNames);
                families.Add(role, new FontFamily(name));
            }
            return new(families);
        }
        catch
        {
            foreach (FontFamily family in families.Values) family.Dispose();
            throw;
        }
    }

    internal static string SelectFamilyName(string abstractRole, ISet<string> installedNames)
    {
        if (!Chains.TryGetValue(abstractRole, out string[]? chain))
            throw new InvalidDataException($"Unknown abstract font role '{abstractRole}'.");
        foreach (string candidate in chain)
            if (installedNames.Contains(candidate)) return candidate;
        return abstractRole == "monospace" ? FontFamily.GenericMonospace.Name : FontFamily.GenericSansSerif.Name;
    }

    private static ISet<string> ReadInstalledNames()
    {
        try
        {
            using var installed = new InstalledFontCollection();
            return installed.Families.Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or InvalidOperationException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

internal interface IThemeRuntimeSymbolProvider
{
    bool TryCreatePath(string symbolSet, string glyphId, float emSize, out GraphicsPath path);
}

internal sealed class LeftPadRuntimeSymbolProvider : IThemeRuntimeSymbolProvider
{
    public bool TryCreatePath(string symbolSet, string glyphId, float emSize, out GraphicsPath path)
    {
        path = new GraphicsPath();
        bool supported = symbolSet switch
        {
            "leftpad-ds4" => AddDs4(path, glyphId, emSize),
            "leftpad-basic" => AddBasic(path, glyphId, emSize),
            _ => false
        };
        if (supported) return true;
        path.Dispose();
        path = null!;
        return false;
    }

    private static bool AddDs4(GraphicsPath path, string id, float size)
    {
        float p = size * 0.12f;
        RectangleF box = new(p, p, size - 2 * p, size - 2 * p);
        switch (id.ToUpperInvariant())
        {
            case "CROSS":
                path.AddPolygon(new[] { new PointF(size*.25f,size*.15f), new PointF(size*.5f,size*.4f),
                    new PointF(size*.75f,size*.15f), new PointF(size*.85f,size*.25f),
                    new PointF(size*.6f,size*.5f), new PointF(size*.85f,size*.75f),
                    new PointF(size*.75f,size*.85f), new PointF(size*.5f,size*.6f),
                    new PointF(size*.25f,size*.85f), new PointF(size*.15f,size*.75f),
                    new PointF(size*.4f,size*.5f), new PointF(size*.15f,size*.25f) });
                return true;
            case "CIRCLE": path.AddEllipse(box); return true;
            case "SQUARE": path.AddRectangle(box); return true;
            case "TRIANGLE": path.AddPolygon(new[] { new PointF(size*.5f,p), new PointF(size-p,size-p), new PointF(p,size-p) }); return true;
            case "L1": case "L3": case "R3":
                using (var family = new FontFamily("Arial"))
                    path.AddString(id.ToUpperInvariant(), family, (int)FontStyle.Bold, size*.55f,
                        new PointF(size*.08f,size*.2f), StringFormat.GenericTypographic);
                return true;
            case "DPAD_DOWN":
                path.AddPolygon(new[] { new PointF(size*.35f,p), new PointF(size*.65f,p),
                    new PointF(size*.65f,size*.55f), new PointF(size-p,size*.55f),
                    new PointF(size*.5f,size-p), new PointF(p,size*.55f), new PointF(size*.35f,size*.55f) });
                return true;
            default: return false;
        }
    }

    private static bool AddBasic(GraphicsPath path, string id, float size)
    {
        switch (id.ToUpperInvariant())
        {
            case "ENTER":
                path.AddPolygon(new[] { new PointF(size*.1f,size*.55f), new PointF(size*.42f,size*.25f),
                    new PointF(size*.42f,size*.45f), new PointF(size*.82f,size*.45f),
                    new PointF(size*.82f,size*.12f), new PointF(size*.95f,size*.12f),
                    new PointF(size*.95f,size*.58f), new PointF(size*.42f,size*.58f),
                    new PointF(size*.42f,size*.78f) }); return true;
            case "SPACE": path.AddRectangle(new RectangleF(size*.12f,size*.65f,size*.76f,size*.12f)); return true;
            case "TAB":
                path.AddPolygon(new[] { new PointF(size*.1f,size*.5f), new PointF(size*.38f,size*.25f),
                    new PointF(size*.38f,size*.42f), new PointF(size*.82f,size*.42f),
                    new PointF(size*.82f,size*.25f), new PointF(size*.92f,size*.25f),
                    new PointF(size*.92f,size*.75f), new PointF(size*.82f,size*.75f),
                    new PointF(size*.82f,size*.58f), new PointF(size*.38f,size*.58f),
                    new PointF(size*.38f,size*.75f) }); return true;
            default: return false;
        }
    }
}

internal sealed record ResolvedGlyphSource(
    string Type, string? SymbolSet, string? StyleRole, string Text, string GlyphId);

internal static class ThemeGlyphResolver
{
    public static ResolvedGlyphSource? Resolve(NormalizedGlyphRole role,
        ResolvedDynamicContent content, IThemeRuntimeSymbolProvider symbols, float probeSize = 100f)
    {
        NormalizedGlyphFamily family = content.GlyphFamily switch
        {
            "keyboard" => role.Keyboard,
            "keyboardShortcut" => role.KeyboardShortcut,
            "ds4" => role.Ds4,
            _ => role.GenericAction
        };
        foreach (NormalizedGlyphSource source in family.Sources)
        {
            if (source.Type == "text")
                return new("text", null, source.StyleRole, content.TextFallback, content.GlyphId);
            if (source.Type == "runtimeSymbol" && source.SymbolSet != null &&
                symbols.TryCreatePath(source.SymbolSet, content.GlyphId, probeSize, out GraphicsPath probe))
            {
                probe.Dispose();
                return new("runtimeSymbol", source.SymbolSet, null, content.TextFallback, content.GlyphId);
            }
        }
        return null;
    }
}

internal readonly record struct ThemeSemanticRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public static ThemeSemanticRect From(RectangleF value) => new(value.Left, value.Top, value.Right, value.Bottom);
    public ThemeSemanticRect Inflate(double x, double y) => new(Left-x, Top-y, Right+x, Bottom+y);
    public ThemeSemanticRect Translate(double x, double y) => new(Left+x, Top+y, Right+x, Bottom+y);
    public static ThemeSemanticRect Union(ThemeSemanticRect a, ThemeSemanticRect b) =>
        new(Math.Min(a.Left,b.Left), Math.Min(a.Top,b.Top), Math.Max(a.Right,b.Right), Math.Max(a.Bottom,b.Bottom));
}

internal sealed class ThemeDynamicLayoutResult : IDisposable
{
    public ThemeDynamicLayoutResult(bool draw, string displayedText, double scale,
        ThemeSemanticRect f, ThemeSemanticRect o, ThemeSemanticRect r,
        ThemeSemanticRect? s, ThemeSemanticRect? styledBounds, GraphicsPath? path)
    { Draw = draw; DisplayedText = displayedText; Scale = scale; F=f; O=o; R=r; S=s; StyledBounds=styledBounds; Path=path; }
    public bool Draw { get; }
    public string DisplayedText { get; }
    public double Scale { get; }
    public ThemeSemanticRect F { get; }
    public ThemeSemanticRect O { get; }
    public ThemeSemanticRect R { get; }
    public ThemeSemanticRect? S { get; }
    public ThemeSemanticRect? StyledBounds { get; }
    public GraphicsPath? Path { get; }
    public void Dispose() => Path?.Dispose();
    public static ThemeDynamicLayoutResult NoDraw(string text = "") =>
        new(false, text, 1d, default, default, default, null, null, null);
}

internal static class ThemeDynamicLayoutEngine
{
    public const int ScalePrecisionDenominator = 64;
    private const double FitTolerance = 0.000001d;

    public static ThemeDynamicLayoutResult Layout(
        NormalizedDynamicAnchor anchor,
        NormalizedDynamicStyle style,
        FontFamily font,
        string text,
        Func<float, GraphicsPath>? symbolFactory = null)
    {
        if (string.IsNullOrEmpty(text) && symbolFactory == null) return ThemeDynamicLayoutResult.NoDraw();
        return anchor.OverflowPolicy switch
        {
            "ellipsis" => Ellipsis(anchor, style, font, text, symbolFactory),
            "shrink" => Shrink(anchor, style, font, text, symbolFactory),
            "clip" => Candidate(anchor, style, font, text, 1d, true, symbolFactory) ?? ThemeDynamicLayoutResult.NoDraw(text),
            "hide" => Candidate(anchor, style, font, text, 1d, false, symbolFactory) ?? ThemeDynamicLayoutResult.NoDraw(text),
            _ => throw new InvalidDataException($"Unknown overflow policy '{anchor.OverflowPolicy}'.")
        };
    }

    private static ThemeDynamicLayoutResult Ellipsis(NormalizedDynamicAnchor anchor,
        NormalizedDynamicStyle style, FontFamily font, string text, Func<float, GraphicsPath>? symbolFactory)
    {
        ThemeDynamicLayoutResult? full = Candidate(anchor, style, font, text, 1d, false, symbolFactory);
        if (full != null) return full;
        if (symbolFactory != null) return ThemeDynamicLayoutResult.NoDraw(text);
        string[] elements = TextElements(text);
        for (int count = elements.Length - 1; count >= 0; count--)
        {
            string candidate = string.Concat(elements.Take(count)) + "\u2026";
            ThemeDynamicLayoutResult? result = Candidate(anchor, style, font, candidate, 1d, false, null);
            if (result != null) return result;
        }
        return ThemeDynamicLayoutResult.NoDraw("\u2026");
    }

    private static ThemeDynamicLayoutResult Shrink(NormalizedDynamicAnchor anchor,
        NormalizedDynamicStyle style, FontFamily font, string text, Func<float, GraphicsPath>? symbolFactory)
    {
        int minimumTick = (int)Math.Ceiling(anchor.MinimumScale * ScalePrecisionDenominator - FitTolerance);
        for (int tick = ScalePrecisionDenominator; tick >= minimumTick; tick--)
        {
            double scale = tick / (double)ScalePrecisionDenominator;
            ThemeDynamicLayoutResult? result = Candidate(anchor, style, font, text, scale, false, symbolFactory);
            if (result != null) return result;
        }
        return ThemeDynamicLayoutResult.NoDraw(text);
    }

    private static ThemeDynamicLayoutResult? Candidate(NormalizedDynamicAnchor anchor,
        NormalizedDynamicStyle style, FontFamily font, string text, double scale,
        bool allowOverflow, Func<float, GraphicsPath>? symbolFactory)
    {
        float size = checked((float)(style.Size * scale));
        GraphicsPath? path = symbolFactory?.Invoke(size) ?? BuildTextPath(text, font, size,
            checked((float)anchor.Bounds.Width), anchor.MaxLines);
        if (path == null || path.PointCount == 0) { path?.Dispose(); return null; }
        RectangleF raw = path.GetBounds();
        float x = anchor.HorizontalAlignment switch
        {
            "left" => (float)anchor.Bounds.X - raw.Left,
            "center" => (float)(anchor.Bounds.X + anchor.Bounds.Width / 2d) - (raw.Left + raw.Width/2f),
            _ => (float)(anchor.Bounds.X + anchor.Bounds.Width) - raw.Right
        };
        float y = anchor.VerticalAlignment switch
        {
            "top" => (float)anchor.Bounds.Y - raw.Top,
            "center" => (float)(anchor.Bounds.Y + anchor.Bounds.Height / 2d) - (raw.Top + raw.Height/2f),
            _ => (float)(anchor.Bounds.Y + anchor.Bounds.Height) - raw.Bottom
        };
        using (var align = new Matrix()) { align.Translate(x, y); path.Transform(align); }
        ThemeSemanticRect f = ThemeSemanticRect.From(path.GetBounds());
        bool outlineVisible = style.Outline is { Color.IsVisible: true, Width: > 0d };
        double radius = outlineVisible ? style.Outline!.Width * scale / 2d : 0d;
        ThemeSemanticRect o = f.Inflate(radius, radius);
        double cx = anchor.Bounds.X + anchor.Bounds.Width / 2d;
        double cy = anchor.Bounds.Y + anchor.Bounds.Height / 2d;
        ThemeSemanticRect r = RotateBounds(o, cx, cy, anchor.Rotation);
        if (Math.Abs(anchor.Rotation) > FitTolerance)
        {
            using var rotate = new Matrix();
            rotate.RotateAt(checked((float)anchor.Rotation), new PointF((float)cx, (float)cy));
            path.Transform(rotate);
        }
        bool baseVisible = style.Color.IsVisible || outlineVisible;
        bool shadowVisible = style.Shadow is { Color.IsVisible: true };
        ThemeSemanticRect? s = null;
        if (shadowVisible)
        {
            NormalizedShadowStyle shadow = style.Shadow!;
            double support = 3d * shadow.Blur * scale;
            s = r.Translate(shadow.OffsetX * scale, shadow.OffsetY * scale).Inflate(support, support);
        }
        ThemeSemanticRect? styled = baseVisible && shadowVisible ? ThemeSemanticRect.Union(r, s!.Value) :
            baseVisible ? r : shadowVisible ? s : null;
        if (styled == null) { path.Dispose(); return ThemeDynamicLayoutResult.NoDraw(text); }
        if (!allowOverflow && !Contained(styled.Value, anchor.Bounds)) { path.Dispose(); return null; }
        return new(true, text, scale, f, o, r, s, styled, path);
    }

    private static GraphicsPath? BuildTextPath(string text, FontFamily font, float size,
        float maximumWidth, int? maxLines)
    {
        string[] elements = TextElements(text);
        var lines = new List<string>();
        string current = string.Empty;
        foreach (string element in elements)
        {
            if (element == "\r") continue;
            if (element == "\n") { lines.Add(current); current = string.Empty; continue; }
            string candidate = current + element;
            using GraphicsPath probe = AddString(candidate, font, size, PointF.Empty);
            if (current.Length > 0 && probe.GetBounds().Width > maximumWidth &&
                (!maxLines.HasValue || lines.Count < maxLines.Value - 1))
            {
                lines.Add(current);
                current = element;
            }
            else current = candidate;
        }
        if (current.Length > 0 || lines.Count == 0) lines.Add(current);
        float lineHeight = size * font.GetLineSpacing(FontStyle.Regular) / font.GetEmHeight(FontStyle.Regular);
        var result = new GraphicsPath();
        for (int line = 0; line < lines.Count; line++)
        {
            using GraphicsPath value = AddString(lines[line], font, size, new PointF(0f, line * lineHeight));
            result.AddPath(value, false);
        }
        return result;
    }

    private static GraphicsPath AddString(string text, FontFamily font, float size, PointF origin)
    {
        var path = new GraphicsPath();
        path.AddString(text, font, (int)FontStyle.Regular, size, origin, StringFormat.GenericTypographic);
        return path;
    }

    private static string[] TextElements(string text)
    {
        var values = new List<string>();
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) values.Add(enumerator.GetTextElement());
        return values.ToArray();
    }

    private static ThemeSemanticRect RotateBounds(ThemeSemanticRect value, double cx, double cy, double degrees)
    {
        double radians = degrees * Math.PI / 180d;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        var corners = new[] { (value.Left,value.Top), (value.Right,value.Top),
            (value.Right,value.Bottom), (value.Left,value.Bottom) };
        var rotated = corners.Select(p =>
        {
            double x = p.Item1-cx, y=p.Item2-cy;
            return (X: cx + x*cos - y*sin, Y: cy + x*sin + y*cos);
        }).ToArray();
        return new(rotated.Min(x=>x.X), rotated.Min(x=>x.Y), rotated.Max(x=>x.X), rotated.Max(x=>x.Y));
    }

    private static bool Contained(ThemeSemanticRect value, NormalizedReferenceBounds bounds) =>
        value.Left >= bounds.X-FitTolerance && value.Top >= bounds.Y-FitTolerance &&
        value.Right <= bounds.X+bounds.Width+FitTolerance &&
        value.Bottom <= bounds.Y+bounds.Height+FitTolerance;
}

internal sealed record ThemeDynamicSprite(Bitmap Bitmap, Point Location)
{
    public long Bytes => checked((long)Bitmap.Width * Bitmap.Height * 4L);
}

internal sealed class ThemeDynamicRasterizer
{
    private readonly NormalizedRenderPlan _plan;
    private readonly ThemeFontSession _fonts;
    private readonly IThemeRuntimeSymbolProvider _symbols;
    private readonly UniversalRadialParameters? _universalParameters;
    private readonly UniversalRadialRenderPlan? _universalPlan;

    public int LayerRasterCount { get; private set; }
    public int LocalSpriteBitmapCount { get; private set; }
    public long LocalSpriteBytes { get; private set; }

    public ThemeDynamicRasterizer(NormalizedRenderPlan plan, ThemeFontSession fonts,
        IThemeRuntimeSymbolProvider? symbols = null,
        UniversalRadialParameters? universalParameters = null)
    {
        _plan=plan;
        _fonts=fonts;
        _symbols=symbols ?? new LeftPadRuntimeSymbolProvider();
        _universalParameters = universalParameters;
        _universalPlan = universalParameters == null
            ? null
            : UniversalRadialRenderPlan.Create(plan, universalParameters);
    }

    public Bitmap RenderLayer(FullStateFrameLayer layer, FullStateFrameState state,
        ThemeMappingSnapshot mappings, int dpi, out ThemeDynamicLayoutResult semantic)
    {
        Bitmap? target = null;
        _ = RenderLayerInto(
            layer,
            state,
            mappings,
            dpi,
            () => EmptySurface(dpi),
            ref target,
            out semantic);
        return target ?? EmptySurface(dpi);
    }

    public bool RenderLayerInto(
        FullStateFrameLayer layer,
        FullStateFrameState state,
        ThemeMappingSnapshot mappings,
        int dpi,
        Func<Bitmap> targetFactory,
        ref Bitmap? target,
        out ThemeDynamicLayoutResult semantic)
    {
        ArgumentNullException.ThrowIfNull(targetFactory);
        if (!TryLayout(layer, state, mappings, out NormalizedDynamicAnchor anchor,
                out NormalizedDynamicStyle style, out semantic))
            return false;
        target ??= targetFactory();
        LayerRasterCount++;
        try
        {
            Draw(target, anchor, style, semantic, dpi);
        }
        catch
        {
            semantic.Dispose();
            throw;
        }
        return true;
    }

    public ThemeDynamicSprite? RenderSprite(
        FullStateFrameLayer layer,
        FullStateFrameState state,
        ThemeMappingSnapshot mappings,
        int dpi,
        out ThemeDynamicLayoutResult semantic)
    {
        if (!TryLayout(layer, state, mappings, out NormalizedDynamicAnchor anchor,
                out NormalizedDynamicStyle style, out semantic))
            return null;
        ThemeDynamicSprite? sprite;
        try
        {
            sprite = DrawSprite(anchor, style, semantic, dpi);
        }
        catch
        {
            semantic.Dispose();
            throw;
        }
        if (sprite == null) return null;
        LayerRasterCount++;
        LocalSpriteBitmapCount++;
        LocalSpriteBytes += sprite.Bytes;
        return sprite;
    }

    private bool TryLayout(
        FullStateFrameLayer layer,
        FullStateFrameState state,
        ThemeMappingSnapshot mappings,
        out NormalizedDynamicAnchor anchor,
        out NormalizedDynamicStyle style,
        out ThemeDynamicLayoutResult semantic)
    {
        NormalizedDynamicThemeModel model = _plan.DynamicTheme ?? throw new InvalidOperationException();
        anchor = model.AnchorForLayer(layer.Id);
        style = model.Styles[anchor.StyleRole];
        if (!FullStateFrameCache.IsVisible(anchor.VisibleStates, state.Name, state.SlotId))
        { semantic = ThemeDynamicLayoutResult.NoDraw(); return false; }
        if (_universalPlan != null)
            anchor = UniversalDynamicContentTransform.TranslateAnchor(_universalPlan, layer.Id, anchor);
        string key = model.OwnershipByLayer[layer.Id].ContentKey;
        ResolvedDynamicContent content = ThemeDynamicContentResolver.Resolve(key, state.SlotId ?? 0, mappings);
        if (content.IsEmpty) { semantic = ThemeDynamicLayoutResult.NoDraw(); return false; }
        Func<float, GraphicsPath>? symbolFactory = null;
        string text = content.LabelText;
        if (anchor.Role == "glyph")
        {
            ResolvedGlyphSource? source = ThemeGlyphResolver.Resolve(model.GlyphRoles[anchor.GlyphRole!], content, _symbols);
            if (source == null)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"[V2 dynamic glyph] No declared source can render '{content.GlyphId}' for anchor '{anchor.Id}'.");
                semantic = ThemeDynamicLayoutResult.NoDraw(); return false;
            }
            if (source.StyleRole != null) style = model.Styles[source.StyleRole];
            text = source.Text;
            if (source.Type == "runtimeSymbol")
                symbolFactory = size => _symbols.TryCreatePath(source.SymbolSet!, source.GlyphId, size, out GraphicsPath path)
                    ? path : new GraphicsPath();
        }
        style = ApplyUniversalStyle(style, scaleFont: symbolFactory == null);
        FontFamily family = _fonts.Get(style.FontRole);
        semantic = ThemeDynamicLayoutEngine.Layout(anchor, style, family, text, symbolFactory);
        return semantic.Draw;
    }

    private Bitmap EmptySurface(int dpi)
    {
        double presentationScale = _universalParameters?.SurfaceScale ?? 1d;
        Size size = RadialDpiScaling.LogicalSizeToPhysical(
            _plan.ReferenceScale.LogicalWidth * presentationScale,
            _plan.ReferenceScale.LogicalHeight * presentationScale,
            dpi);
        return new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
    }

    private void Draw(Bitmap target, NormalizedDynamicAnchor anchor, NormalizedDynamicStyle style,
        ThemeDynamicLayoutResult semantic, int dpi)
    {
        double presentationScale = _universalParameters?.SurfaceScale ?? 1d;
        double referenceFactor = Math.Min(_plan.ReferenceScale.LogicalWidth/_plan.ReferenceCanvas.Width,
            _plan.ReferenceScale.LogicalHeight/_plan.ReferenceCanvas.Height) * presentationScale;
        float physicalScale = checked((float)(referenceFactor * RadialDpiScaling.GetScale(dpi)));
        float originX = checked((float)(_plan.ReferenceScale.ContentOrigin.X * presentationScale * RadialDpiScaling.GetScale(dpi)));
        float originY = checked((float)(_plan.ReferenceScale.ContentOrigin.Y * presentationScale * RadialDpiScaling.GetScale(dpi)));
        using GraphicsPath path = (GraphicsPath)semantic.Path!.Clone();
        using (var transform = new Matrix(physicalScale, 0, 0, physicalScale, originX, originY)) path.Transform(transform);
        Rectangle clip = RadialDpiScaling.LogicalRectToPhysical(
            _plan.ReferenceScale.ContentOrigin.X * presentationScale + anchor.Bounds.X*referenceFactor,
            _plan.ReferenceScale.ContentOrigin.Y * presentationScale + anchor.Bounds.Y*referenceFactor,
            _plan.ReferenceScale.ContentOrigin.X * presentationScale + (anchor.Bounds.X+anchor.Bounds.Width)*referenceFactor,
            _plan.ReferenceScale.ContentOrigin.Y * presentationScale + (anchor.Bounds.Y+anchor.Bounds.Height)*referenceFactor, dpi);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SetClip(clip);
        if (style.Shadow is { Color.IsVisible: true } shadow)
        {
            using Bitmap shadowBitmap = RenderShadow(path, style.Outline, shadow,
                semantic.Scale, physicalScale, target.Size);
            graphics.DrawImageUnscaled(shadowBitmap, 0, 0);
        }
        if (style.Color.IsVisible)
        {
            using var brush = new SolidBrush(style.Color.ToColor());
            graphics.FillPath(brush, path);
        }
        if (style.Outline is { Color.IsVisible: true, Width: > 0d } outline)
        {
            using var pen = new Pen(outline.Color.ToColor(), checked((float)(outline.Width*semantic.Scale*physicalScale)))
            { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawPath(pen, path);
        }
    }

    private ThemeDynamicSprite? DrawSprite(
        NormalizedDynamicAnchor anchor,
        NormalizedDynamicStyle style,
        ThemeDynamicLayoutResult semantic,
        int dpi)
    {
        double presentationScale = _universalParameters?.SurfaceScale ?? 1d;
        double referenceFactor = Math.Min(
            _plan.ReferenceScale.LogicalWidth / _plan.ReferenceCanvas.Width,
            _plan.ReferenceScale.LogicalHeight / _plan.ReferenceCanvas.Height) * presentationScale;
        float physicalScale = checked((float)(referenceFactor * RadialDpiScaling.GetScale(dpi)));
        float originX = checked((float)(
            _plan.ReferenceScale.ContentOrigin.X * presentationScale * RadialDpiScaling.GetScale(dpi)));
        float originY = checked((float)(
            _plan.ReferenceScale.ContentOrigin.Y * presentationScale * RadialDpiScaling.GetScale(dpi)));
        Size canvas = RadialDpiScaling.LogicalSizeToPhysical(
            _plan.ReferenceScale.LogicalWidth * presentationScale,
            _plan.ReferenceScale.LogicalHeight * presentationScale,
            dpi);
        Rectangle clip = RadialDpiScaling.LogicalRectToPhysical(
            _plan.ReferenceScale.ContentOrigin.X * presentationScale + anchor.Bounds.X * referenceFactor,
            _plan.ReferenceScale.ContentOrigin.Y * presentationScale + anchor.Bounds.Y * referenceFactor,
            _plan.ReferenceScale.ContentOrigin.X * presentationScale +
                (anchor.Bounds.X + anchor.Bounds.Width) * referenceFactor,
            _plan.ReferenceScale.ContentOrigin.Y * presentationScale +
                (anchor.Bounds.Y + anchor.Bounds.Height) * referenceFactor,
            dpi);
        clip.Intersect(new Rectangle(Point.Empty, canvas));
        if (clip.Width <= 0 || clip.Height <= 0) return null;

        using GraphicsPath physicalPath = (GraphicsPath)semantic.Path!.Clone();
        using (var transform = new Matrix(physicalScale, 0, 0, physicalScale, originX, originY))
            physicalPath.Transform(transform);

        var target = new Bitmap(clip.Width, clip.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(target);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SetClip(new Rectangle(Point.Empty, clip.Size));
            if (style.Shadow is { Color.IsVisible: true } shadow)
            {
                using ShadowSprite shadowSprite = RenderShadowSprite(
                    physicalPath,
                    style.Outline,
                    shadow,
                    semantic.Scale,
                    physicalScale,
                    clip,
                    canvas);
                graphics.DrawImageUnscaled(
                    shadowSprite.Bitmap,
                    shadowSprite.Location.X - clip.X,
                    shadowSprite.Location.Y - clip.Y);
            }

            using GraphicsPath localPath = (GraphicsPath)physicalPath.Clone();
            using (var move = new Matrix())
            {
                move.Translate(-clip.X, -clip.Y);
                localPath.Transform(move);
            }
            if (style.Color.IsVisible)
            {
                using var brush = new SolidBrush(style.Color.ToColor());
                graphics.FillPath(brush, localPath);
            }
            if (style.Outline is { Color.IsVisible: true, Width: > 0d } outline)
            {
                using var pen = new Pen(
                    outline.Color.ToColor(),
                    checked((float)(outline.Width * semantic.Scale * physicalScale)))
                { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawPath(pen, localPath);
            }
            return new(target, clip.Location);
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    private NormalizedDynamicStyle ApplyUniversalStyle(
        NormalizedDynamicStyle style,
        bool scaleFont)
    {
        if (_universalParameters == null) return style;
        byte strength = _universalParameters.TextStrength;
        return style with
        {
            Size = scaleFont ? style.Size * _universalParameters.FontScale : style.Size,
            Color = ScaleColor(style.Color, strength),
            Outline = style.Outline == null
                ? null
                : style.Outline with { Color = ScaleColor(style.Outline.Color, strength) },
            Shadow = style.Shadow == null
                ? null
                : style.Shadow with { Color = ScaleColor(style.Shadow.Color, strength) }
        };
    }

    private static NormalizedThemeColor ScaleColor(NormalizedThemeColor color, byte strength) =>
        color with
        {
            Alpha = UniversalRadialSettingsNormalizer.ScaleAuthoredAlpha(color.Alpha, strength)
        };

    private static Bitmap RenderShadow(GraphicsPath source, NormalizedOutlineStyle? outline,
        NormalizedShadowStyle shadow,
        double scale, float physicalScale, Size size)
    {
        using var mask = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(mask))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath shifted = (GraphicsPath)source.Clone();
            using var move = new Matrix();
            move.Translate(checked((float)(shadow.OffsetX*scale*physicalScale)),
                checked((float)(shadow.OffsetY*scale*physicalScale)));
            shifted.Transform(move);
            using var brush = new SolidBrush(Color.White);
            graphics.FillPath(brush, shifted);
            if (outline is { Color.IsVisible: true, Width: > 0d })
            {
                using var pen = new Pen(Color.White, checked((float)(outline.Width*scale*physicalScale)))
                { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawPath(pen, shifted);
            }
        }
        int radius = checked((int)Math.Ceiling(3d*shadow.Blur*scale*physicalScale));
        byte[] alpha = ReadAlpha(mask);
        if (radius > 0)
            alpha = GaussianBlur(alpha, size.Width, size.Height,
                shadow.Blur*scale*physicalScale, radius);
        return Colorize(alpha, size.Width, size.Height, shadow.Color);
    }

    private static ShadowSprite RenderShadowSprite(
        GraphicsPath source,
        NormalizedOutlineStyle? outline,
        NormalizedShadowStyle shadow,
        double scale,
        float physicalScale,
        Rectangle clip,
        Size canvas)
    {
        int radius = checked((int)Math.Ceiling(3d * shadow.Blur * scale * physicalScale));
        Rectangle region = clip;
        region.Inflate(radius + 2, radius + 2);
        region.Intersect(new Rectangle(Point.Empty, canvas));
        var mask = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using (Graphics graphics = Graphics.FromImage(mask))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using GraphicsPath shifted = (GraphicsPath)source.Clone();
                using var move = new Matrix();
                move.Translate(
                    checked((float)(shadow.OffsetX * scale * physicalScale - region.X)),
                    checked((float)(shadow.OffsetY * scale * physicalScale - region.Y)));
                shifted.Transform(move);
                using var brush = new SolidBrush(Color.White);
                graphics.FillPath(brush, shifted);
                if (outline is { Color.IsVisible: true, Width: > 0d })
                {
                    using var pen = new Pen(
                        Color.White,
                        checked((float)(outline.Width * scale * physicalScale)))
                    { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                    graphics.DrawPath(pen, shifted);
                }
            }
            byte[] alpha = ReadAlpha(mask);
            if (radius > 0)
                alpha = GaussianBlur(
                    alpha,
                    region.Width,
                    region.Height,
                    shadow.Blur * scale * physicalScale,
                    radius);
            return new(Colorize(alpha, region.Width, region.Height, shadow.Color), region.Location);
        }
        finally
        {
            mask.Dispose();
        }
    }

    private static byte[] ReadAlpha(Bitmap bitmap)
    {
        Rectangle rect = new(0,0,bitmap.Width,bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] raw = new byte[data.Stride*data.Height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            byte[] alpha = new byte[bitmap.Width*bitmap.Height];
            for (int y=0;y<bitmap.Height;y++) for (int x=0;x<bitmap.Width;x++)
                alpha[y*bitmap.Width+x] = raw[y*data.Stride+x*4+3];
            return alpha;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static byte[] GaussianBlur(byte[] source, int width, int height, double sigma, int radius)
    {
        if (sigma <= 0d || radius == 0) return source;
        double[] kernel = new double[radius*2+1]; double sum=0;
        for (int i=-radius;i<=radius;i++) { double v=Math.Exp(-(i*i)/(2*sigma*sigma)); kernel[i+radius]=v; sum+=v; }
        for (int i=0;i<kernel.Length;i++) kernel[i]/=sum;
        double[] horizontal = new double[source.Length];
        for (int y=0;y<height;y++) for (int x=0;x<width;x++)
        { double value=0; for(int k=-radius;k<=radius;k++){int sx=x+k;if((uint)sx<(uint)width)value+=source[y*width+sx]*kernel[k+radius];} horizontal[y*width+x]=value; }
        byte[] result = new byte[source.Length];
        for (int y=0;y<height;y++) for (int x=0;x<width;x++)
        { double value=0; for(int k=-radius;k<=radius;k++){int sy=y+k;if((uint)sy<(uint)height)value+=horizontal[sy*width+x]*kernel[k+radius];} result[y*width+x]=(byte)Math.Clamp((int)Math.Round(value),0,255); }
        return result;
    }

    private static Bitmap Colorize(byte[] alpha, int width, int height, NormalizedThemeColor color)
    {
        var bitmap = new Bitmap(width,height,PixelFormat.Format32bppPArgb);
        Rectangle rect = new(0,0,width,height);
        BitmapData data = bitmap.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
        try
        {
            byte[] raw = new byte[data.Stride*height];
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                int a = alpha[y*width+x]*color.Alpha/255;
                int offset=y*data.Stride+x*4;
                raw[offset]=(byte)(color.Blue*a/255); raw[offset+1]=(byte)(color.Green*a/255);
                raw[offset+2]=(byte)(color.Red*a/255); raw[offset+3]=(byte)a;
            }
            Marshal.Copy(raw,0,data.Scan0,raw.Length);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private sealed record ShadowSprite(Bitmap Bitmap, Point Location) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }
}
