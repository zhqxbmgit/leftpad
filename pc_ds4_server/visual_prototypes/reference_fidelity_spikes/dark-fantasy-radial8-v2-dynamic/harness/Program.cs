using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PcDs4Server;

internal static class Program
{
    private const string ThemeId = "reference-dark-fantasy-radial8-dynamic-spike";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: SpikeLoaderHarness <package> <radial-8 V1 authority>");
            return 2;
        }

        string packagePath = Path.GetFullPath(args[0]);
        string authorityPath = Path.GetFullPath(args[1]);
        string spikeRoot = Directory.GetParent(packagePath)!.FullName;
        string reviewPath = Path.Combine(spikeRoot, "review");
        string workingPath = Path.Combine(spikeRoot, "working");
        Directory.CreateDirectory(reviewPath);
        Directory.CreateDirectory(workingPath);

        RadialVisualPackDefinition authority = RadialVisualPackDefinition.Load(authorityPath);
        Assembly assembly = typeof(RadialVisualPackDefinition).Assembly;
        object package = LoadPackage(assembly, packagePath, authority.LayoutDefinition);
        object manifest = RequiredProperty(package, "Manifest");
        object plan = RequiredProperty(package, "Plan");
        ValidatePlan(manifest, plan);

        RadialMenuSettings setA = BuildSetA();
        RadialMenuSettings setB = BuildSetB();
        setA = setA with { VisualPackId = ThemeId, MappingProfileId = "radial-8" };
        setB = setB with { VisualPackId = ThemeId, MappingProfileId = "radial-8" };

        var report = new Dictionary<string, object?>
        {
            ["themeId"] = ThemeId,
            ["protocolVersion"] = Convert.ToInt32(RequiredProperty(plan, "SourceProtocolVersion")),
            ["layerCount"] = Count(RequiredProperty(RequiredProperty(plan, "RenderModel"), "OrderedLayers")),
            ["anchorCount"] = Count(RequiredProperty(RequiredProperty(plan, "DynamicTheme"), "Anchors")),
            ["mappingSetA"] = MappingDescription(setA),
            ["mappingSetB"] = MappingDescription(setB),
        };

        ValidateGlyphResolution(assembly, plan, setA, report);
        ValidateUnicodeOverflow(assembly, plan, report);
        RenderAndRebuild(assembly, plan, packagePath, reviewPath, setA, setB, report);
        ValidateCatalogAndIdlePreview(packagePath, authorityPath, authority.LayoutDefinition, setA, report);

        string reportPath = Path.Combine(workingPath, "runtime-report.json");
        string reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        File.WriteAllText(reportPath, reportJson);
        Console.WriteLine("SPIKE02 PRODUCTION RUNTIME VALID");
        Console.WriteLine($"Theme/Layers/Anchors: {ThemeId}/19/18");
        Console.WriteLine("192 DPI: 840x840; Mapping A/B rebuild and authored decoder reuse: PASS");
        Console.WriteLine("DS4 runtime symbols CROSS/TRIANGLE/DPAD_DOWN: PASS");
        Console.WriteLine("Idle HWND preview with all outer mappings: PASS");
        return 0;
    }

    private static object LoadPackage(Assembly assembly, string packagePath, LayoutDefinition authority)
    {
        Type loaderType = assembly.GetType("PcDs4Server.UiThemeV2Loader", true)!;
        MethodInfo load = loaderType.GetMethod("Load", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("UiThemeV2Loader.Load was not found.");
        try
        {
            return load.Invoke(null, new object[] { packagePath, authority })
                ?? throw new InvalidOperationException("UiThemeV2Loader returned null.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw exception.InnerException;
        }
    }

    private static void ValidatePlan(object manifest, object plan)
    {
        string id = (string)RequiredProperty(manifest, "Id");
        int sourceProtocol = Convert.ToInt32(RequiredProperty(plan, "SourceProtocolVersion"));
        object renderModel = RequiredProperty(plan, "RenderModel");
        object canvas = RequiredProperty(plan, "ReferenceCanvas");
        object scale = RequiredProperty(plan, "ReferenceScale");
        object placement = RequiredProperty(plan, "Placement");
        object activation = RequiredProperty(placement, "ActivationAnchor");
        object dynamic = RequiredProperty(plan, "DynamicTheme");
        string[] capabilities = Items(RequiredProperty(plan, "RequiredRuntimeCapabilities"))
            .Select(x => x.ToString()!).ToArray();
        if (id != ThemeId || sourceProtocol != 2 || Count(RequiredProperty(renderModel, "OrderedLayers")) != 19 ||
            Count(RequiredProperty(renderModel, "States")) != 9 || Count(RequiredProperty(dynamic, "Anchors")) != 18 ||
            Convert.ToInt32(RequiredProperty(canvas, "Width")) != 1254 ||
            Convert.ToInt32(RequiredProperty(canvas, "Height")) != 1254 ||
            Convert.ToDouble(RequiredProperty(scale, "LogicalWidth")) != 420 ||
            Convert.ToDouble(RequiredProperty(scale, "LogicalHeight")) != 420 ||
            Convert.ToDouble(RequiredProperty(activation, "X")) != 210 ||
            Convert.ToDouble(RequiredProperty(activation, "Y")) != 210 ||
            !capabilities.SequenceEqual(new[] { "fullStateFrame", "dynamicAnchors", "instantTransitions" }))
            throw new InvalidOperationException("Unexpected normalized Spike02 plan.");

        int dynamicLayers = Items(RequiredProperty(renderModel, "OrderedLayers"))
            .Count(layer => RequiredProperty(layer, "Kind").ToString() is "dynamicText" or "dynamicGlyph");
        if (dynamicLayers != 18)
            throw new InvalidOperationException($"Expected 18 dynamic layers, got {dynamicLayers}.");
    }

    private static RadialMenuSettings BuildSetA()
    {
        RadialSlotMappings slots = RadialSlotMappings.Create(8, new RadialSlotMapping[]
        {
            new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.E },
            new() { Kind = RadialActionKind.KeyboardShortcut, Key = KeyboardKey.K, Ctrl = true, Shift = true },
            new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "cross" },
            new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.Tab },
            RadialSlotMapping.None,
            new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "triangle" },
            new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F1 },
            new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "dpad_down" },
        });
        return (RadialMenuSettings.Default with { MappingProfileId = "radial-8" })
            .SetProfileMappings("radial-8", slots);
    }

    private static RadialMenuSettings BuildSetB()
    {
        RadialSlotMappings slots = RadialSlotMappings.Create(8, new RadialSlotMapping[]
        {
            new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F2 },
            new() { Kind = RadialActionKind.KeyboardShortcut, Key = KeyboardKey.K, Ctrl = true, Shift = true },
            new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "square" },
            new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.Tab },
            RadialSlotMapping.None,
            new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "circle" },
            new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F1 },
            new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "dpad_down" },
        });
        return (RadialMenuSettings.Default with { MappingProfileId = "radial-8" })
            .SetProfileMappings("radial-8", slots);
    }

    private static string[] MappingDescription(RadialMenuSettings settings) =>
        settings.GetProfileMappings("radial-8")
            .Select((mapping, index) => $"Slot{index + 1}:{Describe(mapping)}")
            .ToArray();

    private static string Describe(RadialSlotMapping mapping) => mapping.Kind switch
    {
        RadialActionKind.None => "<empty>",
        RadialActionKind.KeyboardKey => mapping.Key?.ToString() ?? "<invalid>",
        RadialActionKind.KeyboardShortcut => string.Join("+", new[]
        {
            mapping.Ctrl ? "Ctrl" : null,
            mapping.Alt ? "Alt" : null,
            mapping.Shift ? "Shift" : null,
            mapping.Win ? "Win" : null,
            mapping.Key?.ToString(),
        }.Where(x => x != null)),
        RadialActionKind.Ds4Button => mapping.Ds4Button?.ToUpperInvariant() ?? "<invalid>",
        _ => "<invalid>",
    };

    private static void ValidateGlyphResolution(Assembly assembly, object plan, RadialMenuSettings settings,
        IDictionary<string, object?> report)
    {
        object dynamic = RequiredProperty(plan, "DynamicTheme");
        object role = DictionaryValue(RequiredProperty(dynamic, "GlyphRoles"), "darkFantasyAction");
        Type snapshotType = assembly.GetType("PcDs4Server.ThemeMappingSnapshot", true)!;
        object snapshot = snapshotType.GetMethod("Capture", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { settings, "radial-8" })!;
        Type contentType = assembly.GetType("PcDs4Server.ThemeDynamicContentResolver", true)!;
        MethodInfo resolveContent = contentType.GetMethod("Resolve", BindingFlags.Static | BindingFlags.Public)!;
        Type providerType = assembly.GetType("PcDs4Server.LeftPadRuntimeSymbolProvider", true)!;
        object provider = Activator.CreateInstance(providerType)!;
        Type resolverType = assembly.GetType("PcDs4Server.ThemeGlyphResolver", true)!;
        MethodInfo resolveGlyph = resolverType.GetMethod("Resolve", BindingFlags.Static | BindingFlags.Public)!;
        var values = new Dictionary<string, string>();
        foreach ((string key, string expectedType, string expectedId) in new[]
        {
            ("slot1ActionGlyph", "no-draw", "E"),
            ("slot2ActionGlyph", "no-draw", "K"),
            ("slot3ActionGlyph", "runtimeSymbol", "CROSS"),
            ("slot6ActionGlyph", "runtimeSymbol", "TRIANGLE"),
            ("slot8ActionGlyph", "runtimeSymbol", "DPAD_DOWN"),
        })
        {
            object content = resolveContent.Invoke(null, new[] { key, (object)0, snapshot })!;
            object? source = resolveGlyph.Invoke(null, new[] { role, content, provider, (object)100f });
            if (expectedType == "no-draw")
            {
                if (source != null)
                    throw new InvalidOperationException($"Keyboard glyph source must be incapable for {key}.");
                values[key] = $"no-draw:{expectedId}";
                continue;
            }
            if (source == null)
                throw new InvalidOperationException($"No glyph source for {key}.");
            string type = (string)RequiredProperty(source, "Type");
            string glyphId = (string)RequiredProperty(source, "GlyphId");
            if (type != expectedType || glyphId != expectedId)
                throw new InvalidOperationException($"Unexpected glyph resolution for {key}: {type}/{glyphId}.");
            values[key] = $"{type}:{glyphId}";
        }
        report["glyphResolution"] = values;
    }

    private static void ValidateUnicodeOverflow(Assembly assembly, object plan, IDictionary<string, object?> report)
    {
        object dynamic = RequiredProperty(plan, "DynamicTheme");
        object selectedAnchor = Items(RequiredProperty(dynamic, "Anchors"))
            .Single(x => (string)RequiredProperty(x, "Id") == "selectedLabelAnchor");
        object selectedStyle = DictionaryValue(RequiredProperty(dynamic, "Styles"), "darkFantasySelectedLabel");
        Type engine = assembly.GetType("PcDs4Server.ThemeDynamicLayoutEngine", true)!;
        MethodInfo layout = engine.GetMethod("Layout", BindingFlags.Static | BindingFlags.Public)!;
        const string text = "超長動作—Δυναμική—Действие—Ctrl+Shift+K";
        using var family = new FontFamily("Segoe UI Semibold");
        object?[] layoutArguments = { selectedAnchor, selectedStyle, family, text, null };
        object result = layout.Invoke(null, layoutArguments)!;
        try
        {
            bool draw = (bool)RequiredProperty(result, "Draw");
            double scale = Convert.ToDouble(RequiredProperty(result, "Scale"));
            if (!draw || scale < 0.30 || scale > 1.0)
                throw new InvalidOperationException($"Unicode overflow layout failed: draw={draw}, scale={scale}.");
            report["unicodeOverflow"] = new Dictionary<string, object?>
            {
                ["input"] = text,
                ["displayed"] = RequiredProperty(result, "DisplayedText"),
                ["draw"] = draw,
                ["scale"] = scale,
                ["styledBounds"] = RequiredProperty(result, "StyledBounds")?.ToString(),
            };
        }
        finally
        {
            if (result is IDisposable disposable) disposable.Dispose();
        }
    }

    private static void RenderAndRebuild(Assembly assembly, object plan, string packagePath, string reviewPath,
        RadialMenuSettings setA, RadialMenuSettings setB, IDictionary<string, object?> report)
    {
        Type bundleType = assembly.GetType("PcDs4Server.RuntimeRenderBundle", true)!;
        MethodInfo build = bundleType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method => method.Name == "Build" && method.GetParameters().Length == 4);
        object bundle = build.Invoke(null, new[] { plan, setA, (object)280, (object)192 })!;
        try
        {
            Size physical = (Size)RequiredProperty(bundle, "PhysicalSurfaceSize");
            if (physical != new Size(840, 840) || !(bool)RequiredProperty(bundle, "IsFullStateFrame"))
                throw new InvalidOperationException($"Unexpected production bundle size/model: {physical}.");
            object cache = RequiredProperty(bundle, "FullStateCache");
            var dynamicLayers = (IDictionary)(cache.GetType().GetField("_dynamicLayers",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(cache)
                ?? throw new InvalidOperationException("Production dynamic-layer cache was not found."));
            int decodedBefore = Convert.ToInt32(RequiredProperty(cache, "DecodedAssetCount"));
            int authoredBefore = Convert.ToInt32(RequiredProperty(cache, "AuthoredLayerCount"));
            int dynamicBefore = Convert.ToInt32(RequiredProperty(cache, "DynamicBuildCount"));
            int rebuildBefore = Convert.ToInt32(RequiredProperty(cache, "MappingRebuildCount"));
            MethodInfo getState = bundleType.GetMethod("GetFinalState", BindingFlags.Instance | BindingFlags.Public)!;
            var hashesA = new Dictionary<int, string>();
            foreach (int slot in new[] { 0, 1, 2, 3, 5, 8 })
            {
                Bitmap bitmap = (Bitmap)getState.Invoke(bundle, new object[] { slot })!;
                ValidateBitmap(bitmap, physical, slot);
                string name = slot == 0 ? "idle" : $"selected-{slot}";
                bitmap.Save(Path.Combine(reviewPath, $"runtime-200pct-set-a-{name}.png"), ImageFormat.Png);
                hashesA[slot] = RawSha(bitmap);
            }

            // All non-None outer mapping zones change in idle. Exact no-draw semantics are checked
            // against the production dynamic-layer cache because separately rescaling the authored
            // frame can differ by interpolation pixels even when the dynamic layer is fully empty.
            Bitmap idleFinal = (Bitmap)getState.Invoke(bundle, new object[] { 0 })!;
            using Bitmap idleAuthored = ScaleAuthored(Path.Combine(packagePath, "states", "idle.png"), physical);
            object geometry = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory.GetParent(packagePath)!.FullName,
                "working", "geometry.json"))).RootElement.Clone();
            JsonElement slots = ((JsonElement)geometry).GetProperty("slots");
            var idleZoneChanged = new Dictionary<string, bool>();
            for (int slot = 1; slot <= 8; slot++)
            {
                JsonElement slotValue = slots.GetProperty(slot.ToString());
                Rectangle union = Union(ScaleRect(slotValue.GetProperty("glyph"), physical),
                    ScaleRect(slotValue.GetProperty("label"), physical));
                bool changed = RegionSha(idleFinal, union) != RegionSha(idleAuthored, union);
                if (slot != 5 && !changed)
                    throw new InvalidOperationException($"Idle outer mapping no-draw mismatch at slot {slot}: {changed}.");
                idleZoneChanged[$"slot{slot}"] = changed;
            }

            var idleDynamicDraw = new Dictionary<string, bool>();
            for (int slot = 1; slot <= 8; slot++)
            {
                bool glyphDraw = BitmapHasAlpha((Bitmap)dynamicLayers[$"idle\0slot{slot}GlyphLayer"]!);
                bool labelDraw = BitmapHasAlpha((Bitmap)dynamicLayers[$"idle\0slot{slot}LabelLayer"]!);
                bool expectedGlyph = slot is 3 or 6 or 8; // DS4 symbol; keyboard identity is label-owned.
                bool expectedLabel = slot != 5;
                if (glyphDraw != expectedGlyph || labelDraw != expectedLabel)
                    throw new InvalidOperationException(
                        $"Idle dynamic-layer draw mismatch at slot {slot}: glyph={glyphDraw}, label={labelDraw}.");
                idleDynamicDraw[$"slot{slot}Glyph"] = glyphDraw;
                idleDynamicDraw[$"slot{slot}Label"] = labelDraw;
            }

            // Central selected identity must draw for mapped slots, and selected-5 None must be exact no-draw.
            Rectangle central = new(292, 278, 264, 207);
            var centralChanged = new Dictionary<string, bool>();
            foreach (int slot in new[] { 1, 2, 3, 5, 8 })
            {
                Bitmap final = (Bitmap)getState.Invoke(bundle, new object[] { slot })!;
                using Bitmap authored = ScaleAuthored(Path.Combine(packagePath, "states", $"selected-{slot}.png"), physical);
                bool changed = RegionSha(final, central) != RegionSha(authored, central);
                if (slot != 5 && !changed)
                    throw new InvalidOperationException($"Central selected draw mismatch at slot {slot}: {changed}.");
                centralChanged[$"selected-{slot}"] = changed;
            }


            var selectedDynamicDraw = new Dictionary<string, bool>();
            foreach (int slot in new[] { 1, 2, 3, 5, 8 })
            {
                bool glyphDraw = BitmapHasAlpha((Bitmap)dynamicLayers[$"selected-{slot}\0selectedGlyphLayer"]!);
                bool labelDraw = BitmapHasAlpha((Bitmap)dynamicLayers[$"selected-{slot}\0selectedLabelLayer"]!);
                bool expectedLabel = slot != 5;
                bool expectedGlyph = slot is 3 or 8; // Selected keyboard is single label text; DS4 keeps symbol + label.
                if (glyphDraw != expectedGlyph || labelDraw != expectedLabel)
                    throw new InvalidOperationException(
                        $"Selected dynamic-layer draw mismatch at slot {slot}: glyph={glyphDraw}, label={labelDraw}.");
                selectedDynamicDraw[$"selected-{slot}Glyph"] = glyphDraw;
                selectedDynamicDraw[$"selected-{slot}Label"] = labelDraw;
            }

            MethodInfo ensure = bundleType.GetMethod("EnsureDynamicContent", BindingFlags.Instance | BindingFlags.Public)!;
            ensure.Invoke(bundle, new object[] { setB });
            int decodedAfter = Convert.ToInt32(RequiredProperty(cache, "DecodedAssetCount"));
            int authoredAfter = Convert.ToInt32(RequiredProperty(cache, "AuthoredLayerCount"));
            int dynamicAfter = Convert.ToInt32(RequiredProperty(cache, "DynamicBuildCount"));
            int rebuildAfter = Convert.ToInt32(RequiredProperty(cache, "MappingRebuildCount"));
            var hashesB = new Dictionary<int, string>();
            foreach (int slot in new[] { 0, 1, 3, 6 })
            {
                Bitmap bitmap = (Bitmap)getState.Invoke(bundle, new object[] { slot })!;
                string name = slot == 0 ? "idle" : $"selected-{slot}";
                bitmap.Save(Path.Combine(reviewPath, $"runtime-200pct-set-b-{name}.png"), ImageFormat.Png);
                hashesB[slot] = RawSha(bitmap);
            }
            if (decodedAfter != decodedBefore || authoredAfter != authoredBefore ||
                dynamicAfter != dynamicBefore + 1 || rebuildAfter != rebuildBefore + 1 ||
                hashesA[1] == hashesB[1] || hashesA[3] == hashesB[3])
                throw new InvalidOperationException("Mapping rebuild did not preserve authored decode/cache and change relevant states.");

            report["runtime"] = new Dictionary<string, object?>
            {
                ["physical"] = "840x840",
                ["dpi"] = 192,
                ["decodedAssetsBeforeAfter"] = new[] { decodedBefore, decodedAfter },
                ["authoredLayersBeforeAfter"] = new[] { authoredBefore, authoredAfter },
                ["dynamicBuildsBeforeAfter"] = new[] { dynamicBefore, dynamicAfter },
                ["mappingRebuildsBeforeAfter"] = new[] { rebuildBefore, rebuildAfter },
                ["idleOuterZonesChanged"] = idleZoneChanged,
                ["selectedCentralZonesChanged"] = centralChanged,
                ["idleDynamicLayersDraw"] = idleDynamicDraw,
                ["selectedDynamicLayersDraw"] = selectedDynamicDraw,
                ["setAStateSha"] = hashesA,
                ["setBStateSha"] = hashesB,
            };
        }
        finally
        {
            if (bundle is IDisposable disposable) disposable.Dispose();
        }
    }

    private static void ValidateCatalogAndIdlePreview(string packagePath, string authorityPath,
        LayoutDefinition authorityLayout, RadialMenuSettings settings, IDictionary<string, object?> report)
    {
        string v1Root = Directory.GetParent(authorityPath)!.FullName;
        string themeRoot = Directory.GetParent(packagePath)!.FullName;
        var catalog = new RadialVisualPackCatalog(v1Root, themeRoot);
        RadialVisualPackCatalogSnapshot snapshot = catalog.Discover();
        RadialVisualPackCatalogEntry entry = snapshot.Find(ThemeId)
            ?? throw new InvalidOperationException("Production catalog did not discover Spike02.");
        if (entry.Name != "Dark Fantasy Radial 8 Dynamic Spike")
            throw new InvalidOperationException($"Unexpected catalog display name: {entry.Name}.");

        var logs = new List<string>();
        using var overlay = new RadialMenuOverlay(catalog, logs.Add);
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default, authorityLayout);
        controller.PreviewAt(new Point(700, 500), settings);
        Application.DoEvents();
        Thread.Sleep(250);
        Application.DoEvents();
        bool visible = controller.IsPreviewActive && overlay.IsVisible && overlay.IsHandleCreated;
        int dpi = overlay.ActiveDpi;
        controller.ClosePreview();
        Application.DoEvents();
        bool hidden = !controller.IsPreviewActive && !overlay.IsVisible;
        if (!visible || !hidden || dpi <= 0)
            throw new InvalidOperationException($"Idle HWND preview failed: visible={visible}, hidden={hidden}, dpi={dpi}.");
        report["catalog"] = new Dictionary<string, object?>
        {
            ["discovered"] = true,
            ["displayName"] = entry.Name,
            ["issuesForTheme"] = snapshot.Issues.Where(x => x.PackId == ThemeId).Select(x => x.Message).ToArray(),
        };
        report["idleHwndPreview"] = new Dictionary<string, object?>
        {
            ["visible"] = visible,
            ["hiddenAfterClose"] = hidden,
            ["activeDpi"] = dpi,
            ["selectedSlot"] = 0,
            ["outerMappingsVisibleByManifest"] = true,
            ["logs"] = logs,
        };
    }

    private static Bitmap ScaleAuthored(string path, Size targetSize)
    {
        using var source = new Bitmap(path);
        var target = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(source, new Rectangle(Point.Empty, targetSize), 0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel, attributes);
        return target;
    }

    private static Rectangle ScaleRect(JsonElement value, Size target)
    {
        double factor = target.Width / 1254d;
        int left = (int)Math.Round(value[0].GetDouble() * factor, MidpointRounding.AwayFromZero);
        int top = (int)Math.Round(value[1].GetDouble() * factor, MidpointRounding.AwayFromZero);
        int right = (int)Math.Round((value[0].GetDouble() + value[2].GetDouble()) * factor, MidpointRounding.AwayFromZero);
        int bottom = (int)Math.Round((value[1].GetDouble() + value[3].GetDouble()) * factor, MidpointRounding.AwayFromZero);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Rectangle Union(Rectangle first, Rectangle second) => Rectangle.Union(first, second);

    private static string RegionSha(Bitmap bitmap, Rectangle region)
    {
        using Bitmap crop = bitmap.Clone(region, PixelFormat.Format32bppPArgb);
        return RawSha(crop);
    }

    private static void ValidateBitmap(Bitmap bitmap, Size expected, int slot)
    {
        if (bitmap.Size != expected || bitmap.PixelFormat != PixelFormat.Format32bppPArgb ||
            bitmap.GetPixel(0, 0).A != 0 || bitmap.GetPixel(expected.Width - 1, expected.Height - 1).A != 0)
            throw new InvalidOperationException($"Invalid runtime state {slot}: {bitmap.Size}/{bitmap.PixelFormat}.");
    }

    private static bool BitmapHasAlpha(Bitmap bitmap)
    {
        Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (int index = 3; index < bytes.Length; index += 4)
                if (bytes[index] != 0) return true;
            return false;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static string RawSha(Bitmap bitmap)
    {
        Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static object DictionaryValue(object dictionary, string key) =>
        dictionary.GetType().GetProperty("Item")?.GetValue(dictionary, new object[] { key })
        ?? throw new InvalidOperationException($"Missing dictionary key {key}.");

    private static object RequiredProperty(object owner, string name) =>
        owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(owner)
        ?? throw new InvalidOperationException($"Missing property {owner.GetType().FullName}.{name}");

    private static IEnumerable<object> Items(object sequence) => ((IEnumerable)sequence).Cast<object>();
    private static int Count(object sequence) => Items(sequence).Count();
}
