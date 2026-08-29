using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PcDs4Server;

internal static partial class UiThemeV2Contract
{
    public const int ProtocolVersion = 2;
    public const string ManifestFileName = "manifest.json";
    public static string DiscoveryRoot => Path.Combine(AppContext.BaseDirectory, "Assets", "UIThemes");

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]{0,95}$", RegexOptions.CultureInvariant)]
    public static partial Regex IdentifierPattern();
    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    public static partial Regex Sha256Pattern();
    [GeneratedRegex("^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$", RegexOptions.CultureInvariant)]
    public static partial Regex PackagePathPattern();
}

internal sealed record UiThemeV2Manifest(
    int ProtocolVersion,
    int PackageRevision,
    string Id,
    string Name,
    string Surface,
    string RenderStrategy,
    string LayoutProfile);

internal sealed record UiThemeV2Package(
    string DirectoryPath,
    UiThemeV2Manifest Manifest,
    NormalizedRenderPlan Plan)
{
    public int PackageRevision => Manifest.PackageRevision;
}

internal static class UiThemeV2Loader
{
    private static readonly HashSet<string> TopRequired = new(StringComparer.Ordinal)
    {
        "protocolVersion", "packageRevision", "id", "name", "surface", "renderStrategy",
        "layoutProfile", "compatibleLayouts", "referenceCanvas", "referenceScale", "placement",
        "states", "layers", "dynamicAnchors", "styles", "glyphs", "elementOwnership", "masks",
        "fallback", "capabilities", "transitions", "assetHashes"
    };
    private static readonly HashSet<string> TopOptional = new(StringComparer.Ordinal)
    { "description", "exampleOnly", "authoring", "visualRegions" };
    private static readonly HashSet<string> SupportedRequiredCapabilities = new(StringComparer.Ordinal)
    { "fullStateFrame", "instantTransitions", "dynamicAnchors" };
    private static readonly HashSet<string> SupportedContentKeys = BuildSupportedContentKeys();
    private static readonly HashSet<string> SupportedSymbolSets = new(StringComparer.Ordinal)
    { "leftpad-basic", "leftpad-ds4" };
    private static readonly HashSet<string> KnownCapabilities = new(StringComparer.Ordinal)
    {
        "fullStateFrame", "layeredState", "dynamicAnchors", "themeGlyphAssets",
        "visualRegions", "maskAssets", "occlusionLayers", "safeDynamicSurfaces",
        "instantTransitions", "futureStates"
    };

    public static string? TryReadId(string directoryPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(
                Path.Combine(directoryPath, UiThemeV2Contract.ManifestFileName)));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("id", out JsonElement id) ||
                id.ValueKind != JsonValueKind.String)
                return null;
            return id.GetString();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static UiThemeV2Package Load(string directoryPath, LayoutDefinition layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(layout);
        string root = Path.GetFullPath(directoryPath);
        string manifestPath = Path.Combine(root, UiThemeV2Contract.ManifestFileName);
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("V2 theme manifest was not found.", manifestPath);

        using JsonDocument document = Parse(manifestPath);
        JsonElement manifest = RequireObject(document.RootElement, "manifest", TopRequired, TopOptional);
        RequireInteger(manifest, "protocolVersion", UiThemeV2Contract.ProtocolVersion);
        int revision = RequireInt32(manifest, "packageRevision", 1, int.MaxValue);
        string id = RequireIdentifier(manifest, "id");
        string name = RequireString(manifest, "name", 1, 128);
        RequireStringValue(manifest, "surface", "radial-overlay");
        RequireStringValue(manifest, "renderStrategy", "full-state-frame");
        if (manifest.TryGetProperty("exampleOnly", out JsonElement exampleOnly))
        {
            if (exampleOnly.ValueKind is not (JsonValueKind.False or JsonValueKind.True))
                throw Invalid("exampleOnly must be a boolean.");
            if (exampleOnly.GetBoolean()) throw Invalid("exampleOnly themes cannot enter the production runtime.");
        }
        ValidateOptionalMetadata(manifest);

        string profile = RequireString(manifest, "layoutProfile", 1, 32);
        if (!string.Equals(profile, layout.ProfileId, StringComparison.Ordinal))
            throw Invalid("The V2 layoutProfile does not match the authoritative layout.");
        JsonElement compatible = RequireArray(manifest, "compatibleLayouts");
        if (compatible.GetArrayLength() != 1 || compatible[0].ValueKind != JsonValueKind.String ||
            !string.Equals(compatible[0].GetString(), profile, StringComparison.Ordinal))
            throw Invalid("full-state-frame requires exactly one matching compatible layout.");

        NormalizedReferenceCanvas canvas = ParseCanvas(manifest);
        NormalizedReferenceScale scale = ParseScale(manifest, canvas);
        NormalizedPlacement placement = ParsePlacement(manifest, scale);
        ValidateEmptyArray(manifest, "masks", "maskAssets");
        if (manifest.TryGetProperty("visualRegions", out JsonElement regions) &&
            (regions.ValueKind != JsonValueKind.Array || regions.GetArrayLength() != 0))
            throw Invalid("visualRegions are not supported by the Phase 2 runtime.");
        ValidateStylesAndGlyphs(manifest);
        NormalizedFallbackMetadata fallback = ParseFallback(manifest);
        string[] capabilities = ValidateCapabilities(manifest);
        ValidateTransitions(manifest);

        List<LayerDraft> layerDrafts = ParseLayers(manifest, canvas);
        NormalizedDynamicThemeModel? dynamicTheme = ParseDynamicTheme(
            manifest, canvas, profile, layerDrafts);
        List<StateDraft> stateDrafts = ParseStates(manifest, layout.SlotCount, layerDrafts);
        HashSet<string> stateNames = stateDrafts.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        if (layerDrafts.SelectMany(x => x.VisibleStates).Any(x =>
                x is not ("all" or "selected") && !stateNames.Contains(x)))
            throw Invalid("A layer visibility selector names a state outside the single layout.");
        if (dynamicTheme?.Anchors.SelectMany(x => x.VisibleStates).Any(x =>
                x is not ("all" or "selected") && !stateNames.Contains(x)) == true)
            throw Invalid("A dynamic anchor visibility selector names a state outside the single layout.");
        IReadOnlyDictionary<string, VerifiedThemeAsset> assets = VerifyAssetClosure(
            root, manifest, layerDrafts, stateDrafts);

        FullStateFrameLayer[] layers = layerDrafts.Select(layer => new FullStateFrameLayer(
            layer.Id, layer.Kind, layer.ZIndex, layer.Order, layer.Bounds,
            Array.AsReadOnly(layer.VisibleStates),
            layer.AssetPath == null ? null : assets[layer.AssetPath])).ToArray();
        FullStateFrameState[] states = stateDrafts.Select(state => new FullStateFrameState(
            state.Name, state.SlotId,
            new ReadOnlyDictionary<string, VerifiedThemeAsset>(state.Assets
                .ToDictionary(pair => pair.Key, pair => assets[pair.Value], StringComparer.Ordinal)))).ToArray();
        var fullState = new FullStateFrameRenderPlan(layers, states);
        var plan = new NormalizedRenderPlan(2, id, name, layout, canvas, scale, fullState,
            layers.Where(x => x.StaticAsset != null)
                .Select(x => new NormalizedStaticLayer(x.Id, x.StaticAsset!.PackagePath)),
            new(NormalizedDynamicContentDescriptor.None), Array.Empty<NormalizedGeometryTransform>(),
            fallback, capabilities, placement,
            dynamicTheme);
        return new UiThemeV2Package(root,
            new UiThemeV2Manifest(2, revision, id, name, "radial-overlay", "full-state-frame", profile),
            plan);
    }

    private static NormalizedReferenceCanvas ParseCanvas(JsonElement manifest)
    {
        JsonElement value = RequireObject(manifest.GetProperty("referenceCanvas"), "referenceCanvas",
            new[] { "width", "height", "colorSpace", "alphaMode" });
        int width = RequireInt32(value, "width", 1, 8192);
        int height = RequireInt32(value, "height", 1, 8192);
        RequireStringValue(value, "colorSpace", "sRGB");
        RequireStringValue(value, "alphaMode", "straight");
        return new(width, height, "sRGB");
    }

    private static NormalizedReferenceScale ParseScale(JsonElement manifest, NormalizedReferenceCanvas canvas)
    {
        JsonElement value = RequireObject(manifest.GetProperty("referenceScale"), "referenceScale",
            new[] { "logicalWidth", "logicalHeight", "fit", "contentOrigin" });
        double width = RequireNumber(value, "logicalWidth", exclusiveMinimum: 0d, maximum: 4096d);
        double height = RequireNumber(value, "logicalHeight", exclusiveMinimum: 0d, maximum: 4096d);
        RequireStringValue(value, "fit", "contain");
        JsonElement origin = RequireObject(value.GetProperty("contentOrigin"), "contentOrigin", new[] { "x", "y" });
        double x = RequireNumber(origin, "x", 0d, double.MaxValue);
        double y = RequireNumber(origin, "y", 0d, double.MaxValue);
        double factor = Math.Min(width / canvas.Width, height / canvas.Height);
        if (x + canvas.Width * factor > width + 0.000001d ||
            y + canvas.Height * factor > height + 0.000001d)
            throw Invalid("Reference content is not contained by the logical surface.");
        return new(NormalizedReferenceScale.Contain, width, height, new(x, y), NormalizedReferenceScale.Contain);
    }

    private static NormalizedPlacement ParsePlacement(JsonElement manifest, NormalizedReferenceScale scale)
    {
        JsonElement placement = RequireObject(manifest.GetProperty("placement"), "placement", new[] { "activationAnchor" });
        JsonElement anchor = RequireObject(placement.GetProperty("activationAnchor"), "activationAnchor", new[] { "x", "y" });
        double x = RequireNumber(anchor, "x", 0d, scale.LogicalWidth);
        double y = RequireNumber(anchor, "y", 0d, scale.LogicalHeight);
        return new(new(x, y));
    }

    private static List<LayerDraft> ParseLayers(JsonElement manifest, NormalizedReferenceCanvas canvas)
    {
        JsonElement values = RequireArray(manifest, "layers");
        if (values.GetArrayLength() == 0) throw Invalid("At least one layer is required.");
        var result = new List<LayerDraft>();
        int order = 0;
        foreach (JsonElement element in values.EnumerateArray())
        {
            JsonElement layer = RequireObject(element, "layer",
                new[] { "id", "kind", "zIndex", "bounds", "visibleStates", "ownership", "required" },
                new[] { "asset" });
            string id = RequireIdentifier(layer, "id");
            string kind = RequireString(layer, "kind", 1, 32);
            if (kind is not ("staticAsset" or "stateAsset" or "dynamicText" or "dynamicGlyph"))
                throw Invalid($"Unsupported V2 layer kind: {kind}");
            int zIndex = RequireInt32(layer, "zIndex", -32768, 32767);
            string ownership = RequireString(layer, "ownership", 1, 32);
            if ((kind == "staticAsset" && ownership != "STATIC") ||
                (kind == "stateAsset" && ownership != "STATE_ASSET") ||
                (kind is "dynamicText" or "dynamicGlyph" && ownership != "DYNAMIC"))
                throw Invalid($"Layer '{id}' has incompatible ownership.");
            if (layer.GetProperty("required").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !layer.GetProperty("required").GetBoolean())
                throw Invalid("Phase 2 supported layers must be required.");
            JsonElement bounds = RequireObject(layer.GetProperty("bounds"), "bounds", new[] { "x", "y", "width", "height" });
            double x = RequireNumber(bounds, "x", 0d, canvas.Width);
            double y = RequireNumber(bounds, "y", 0d, canvas.Height);
            double width = RequireNumber(bounds, "width", exclusiveMinimum: 0d, maximum: canvas.Width);
            double height = RequireNumber(bounds, "height", exclusiveMinimum: 0d, maximum: canvas.Height);
            if (x + width > canvas.Width + 0.000001d || y + height > canvas.Height + 0.000001d)
                throw Invalid($"Layer '{id}' bounds escape the reference canvas.");
            string[] visible = ParseVisibility(layer, id);
            string? asset = null;
            if (kind == "staticAsset") asset = RequirePackagePath(layer, "asset");
            else if (layer.TryGetProperty("asset", out _))
                throw Invalid($"{kind} layers cannot declare asset.");
            result.Add(new(id, kind, zIndex, order++, new(x, y, width, height), visible, ownership, asset));
        }
        if (result.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw Invalid("Layer IDs must be unique.");
        return result;
    }

    private static string[] ParseVisibility(JsonElement layer, string id)
    {
        JsonElement array = RequireArray(layer, "visibleStates");
        if (array.GetArrayLength() == 0) throw Invalid($"Layer '{id}' requires visibility.");
        string[] values = array.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw Invalid("Visibility selectors must be strings.")).ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length) throw Invalid("Visibility selectors must be unique.");
        foreach (string value in values)
            if (value != "all" && value != "selected" && value != "idle" &&
                !(value.StartsWith("selected-", StringComparison.Ordinal) &&
                  int.TryParse(value.AsSpan("selected-".Length), out int slot) && slot is >= 1 and <= 8))
                throw Invalid($"Unsupported visibility selector: {value}");
        return values;
    }

    private static List<StateDraft> ParseStates(JsonElement manifest, int slotCount, IReadOnlyList<LayerDraft> layers)
    {
        JsonElement states = manifest.GetProperty("states");
        if (states.ValueKind != JsonValueKind.Object) throw Invalid("states must be an object.");
        var result = new List<StateDraft>();
        string[] stateLayerIds = layers.Where(x => x.Kind == "stateAsset").Select(x => x.Id).Order().ToArray();
        foreach (JsonProperty property in states.EnumerateObject())
        {
            JsonElement state = RequireObject(property.Value, $"state '{property.Name}'", new[] { "slotId", "assets" });
            int? slotId;
            if (state.GetProperty("slotId").ValueKind == JsonValueKind.Null) slotId = null;
            else slotId = RequireInt32(state, "slotId", 1, slotCount);
            string expected = slotId == null ? "idle" : $"selected-{slotId}";
            if (!string.Equals(property.Name, expected, StringComparison.Ordinal))
                throw Invalid($"State '{property.Name}' has an incoherent slotId.");
            JsonElement assets = state.GetProperty("assets");
            if (assets.ValueKind != JsonValueKind.Object) throw Invalid("State assets must be an object.");
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty binding in assets.EnumerateObject())
            {
                if (!UiThemeV2Contract.IdentifierPattern().IsMatch(binding.Name) || binding.Value.ValueKind != JsonValueKind.String)
                    throw Invalid("State asset binding is invalid.");
                bindings.Add(binding.Name, ValidatePackagePath(binding.Value.GetString()!));
            }
            if (!bindings.Keys.Order().SequenceEqual(stateLayerIds, StringComparer.Ordinal))
                throw Invalid($"State '{property.Name}' does not bind every stateAsset layer exactly once.");
            result.Add(new(property.Name, slotId, bindings));
        }
        string[] required = Enumerable.Range(1, slotCount).Select(x => $"selected-{x}").Prepend("idle").Order().ToArray();
        if (!result.Select(x => x.Name).Order().SequenceEqual(required, StringComparer.Ordinal))
            throw Invalid("The full-state package does not contain the exact required state set.");
        return result;
    }

    private static IReadOnlyDictionary<string, VerifiedThemeAsset> VerifyAssetClosure(string root,
        JsonElement manifest, IReadOnlyList<LayerDraft> layers, IReadOnlyList<StateDraft> states)
    {
        HashSet<string> referenced = layers.Where(x => x.AssetPath != null).Select(x => x.AssetPath!)
            .Concat(states.SelectMany(x => x.Assets.Values)).ToHashSet(StringComparer.Ordinal);
        JsonElement hashesElement = manifest.GetProperty("assetHashes");
        if (hashesElement.ValueKind != JsonValueKind.Object) throw Invalid("assetHashes must be an object.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in hashesElement.EnumerateObject())
        {
            string path = ValidatePackagePath(property.Name);
            if (property.Value.ValueKind != JsonValueKind.String ||
                !UiThemeV2Contract.Sha256Pattern().IsMatch(property.Value.GetString()!))
                throw Invalid($"Invalid SHA-256 for '{path}'.");
            hashes.Add(path, property.Value.GetString()!);
        }
        if (!hashes.Keys.Order().SequenceEqual(referenced.Order(), StringComparer.Ordinal))
            throw Invalid("assetHashes must exactly close over all referenced assets.");

        var verified = new Dictionary<string, VerifiedThemeAsset>(StringComparer.Ordinal);
        foreach (string packagePath in referenced.Order(StringComparer.Ordinal))
        {
            string filePath = ResolvePackagePath(root, packagePath);
            if (!File.Exists(filePath)) throw new FileNotFoundException("V2 theme asset was not found.", filePath);
            EnsureResolvedInsideRoot(root, packagePath);
            byte[] bytes = File.ReadAllBytes(filePath);
            string actual = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(actual, hashes[packagePath], StringComparison.OrdinalIgnoreCase))
                throw Invalid($"SHA-256 mismatch for '{packagePath}'.");
            verified.Add(packagePath, new(packagePath, bytes));
        }
        return new ReadOnlyDictionary<string, VerifiedThemeAsset>(verified);
    }

    private static void ValidateOwnership(JsonElement manifest, IReadOnlyList<LayerDraft> layers)
    {
        _ = ValidateOwnership(manifest, layers, Array.Empty<NormalizedDynamicAnchor>(), string.Empty);
    }

    private static IReadOnlyList<NormalizedDynamicOwnership> ValidateOwnership(
        JsonElement manifest,
        IReadOnlyList<LayerDraft> layers,
        IReadOnlyList<NormalizedDynamicAnchor> anchors,
        string profile)
    {
        JsonElement values = RequireArray(manifest, "elementOwnership");
        if (values.GetArrayLength() == 0) throw Invalid("elementOwnership cannot be empty.");
        Dictionary<string, LayerDraft> byId = layers.ToDictionary(x => x.Id, StringComparer.Ordinal);
        Dictionary<string, NormalizedDynamicAnchor> anchorsById = anchors.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var dynamic = new List<NormalizedDynamicOwnership>();
        var seenLayers = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement value in values.EnumerateArray())
        {
            JsonElement item = RequireObject(value, "elementOwnership", new[] { "element", "owner", "layerId" }, new[] { "contentKey" });
            string element = RequireIdentifier(item, "element");
            string owner = RequireString(item, "owner", 1, 32);
            string layerId = RequireIdentifier(item, "layerId");
            if (!byId.TryGetValue(layerId, out LayerDraft? layer) || owner != layer.Ownership)
                throw Invalid("elementOwnership does not match a supported layer.");
            if (!seenLayers.Add(layerId)) throw Invalid("A layer cannot have multiple ownership records.");
            if (owner == "DYNAMIC")
            {
                if (!item.TryGetProperty("contentKey", out JsonElement keyValue) ||
                    keyValue.ValueKind != JsonValueKind.String)
                    throw Invalid("Dynamic element ownership requires contentKey.");
                string contentKey = keyValue.GetString()!;
                if (!SupportedContentKeys.Contains(contentKey)) throw Invalid($"Unknown dynamic contentKey: {contentKey}");
                if (!anchorsById.TryGetValue(element, out NormalizedDynamicAnchor? anchor) || anchor.LayerId != layerId)
                    throw Invalid("Dynamic ownership must match its anchor and layer.");
                if ((layer.Kind == "dynamicText") != contentKey.EndsWith("Label", StringComparison.Ordinal) ||
                    (layer.Kind == "dynamicGlyph") != contentKey.EndsWith("Glyph", StringComparison.Ordinal))
                    throw Invalid("Dynamic contentKey does not match the layer kind.");
                int slot = ParseSlotContentKey(contentKey);
                if (profile == LayoutProfileRegistry.Radial6ProfileId && slot > 6)
                    throw Invalid("radial-6 cannot reference slot7 or slot8 dynamic content.");
                dynamic.Add(new(element, layerId, contentKey));
            }
            else if (item.TryGetProperty("contentKey", out _))
            {
                throw Invalid("Only dynamic ownership can declare contentKey.");
            }
        }
        if (!seenLayers.SetEquals(byId.Keys)) throw Invalid("Every layer requires exactly one ownership record.");
        return Array.AsReadOnly(dynamic.ToArray());
    }

    private static int ParseSlotContentKey(string contentKey)
    {
        if (!contentKey.StartsWith("slot", StringComparison.Ordinal)) return 0;
        int end = contentKey.IndexOf("Action", StringComparison.Ordinal);
        return int.Parse(contentKey.AsSpan(4, end - 4), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string[] ValidateCapabilities(JsonElement manifest)
    {
        JsonElement value = RequireObject(manifest.GetProperty("capabilities"), "capabilities", new[] { "required", "optional" });
        string[] required = ParseStringArray(value, "required");
        string[] optional = ParseStringArray(value, "optional");
        if (required.Distinct(StringComparer.Ordinal).Count() != required.Length ||
            optional.Distinct(StringComparer.Ordinal).Count() != optional.Length)
            throw Invalid("Capabilities must be unique.");
        if (required.Concat(optional).Any(x => !KnownCapabilities.Contains(x)))
            throw Invalid("The package declares an unknown runtime capability.");
        if (required.Intersect(optional, StringComparer.Ordinal).Any())
            throw Invalid("A capability cannot be both required and optional.");
        if (required.Any(x => !SupportedRequiredCapabilities.Contains(x)))
            throw Invalid("The package requires an unsupported runtime capability.");
        if (!required.Contains("fullStateFrame", StringComparer.Ordinal) ||
            !required.Contains("instantTransitions", StringComparer.Ordinal))
            throw Invalid("fullStateFrame and instantTransitions are required.");
        return required;
    }

    private static NormalizedDynamicThemeModel? ParseDynamicTheme(
        JsonElement manifest,
        NormalizedReferenceCanvas canvas,
        string profile,
        IReadOnlyList<LayerDraft> layers)
    {
        LayerDraft[] dynamicLayers = layers.Where(x => x.Kind is "dynamicText" or "dynamicGlyph").ToArray();
        JsonElement anchorValues = RequireArray(manifest, "dynamicAnchors");
        if (dynamicLayers.Length == 0)
        {
            if (anchorValues.GetArrayLength() != 0)
                throw Invalid("dynamicAnchors require dynamic layers.");
            if (ParseStringArray(manifest.GetProperty("capabilities"), "required")
                .Contains("dynamicAnchors", StringComparer.Ordinal))
                throw Invalid("The dynamicAnchors capability requires dynamic layers and anchors.");
            ValidateOwnership(manifest, layers);
            return null;
        }

        JsonElement capabilities = manifest.GetProperty("capabilities");
        string[] requiredCapabilities = ParseStringArray(capabilities, "required");
        if (!requiredCapabilities.Contains("dynamicAnchors", StringComparer.Ordinal))
            throw Invalid("Dynamic layers require the dynamicAnchors capability.");

        JsonElement stylesValue = manifest.GetProperty("styles");
        var fonts = RequireNonEmptyRoleObject(stylesValue, "fontRoles").EnumerateObject()
            .ToDictionary(x => x.Name, x => x.Value.GetString()!, StringComparer.Ordinal);
        var colors = RequireNonEmptyRoleObject(stylesValue, "colorRoles").EnumerateObject()
            .ToDictionary(x => x.Name, x => ParseColor(x.Value.GetString()!), StringComparer.Ordinal);
        var outlines = new Dictionary<string, NormalizedOutlineStyle>(StringComparer.Ordinal);
        foreach (JsonProperty role in RequireRoleObject(stylesValue, "outlineRoles").EnumerateObject())
        {
            string colorRole = RequireIdentifier(role.Value, "colorRole");
            if (!colors.TryGetValue(colorRole, out NormalizedThemeColor color))
                throw Invalid($"Outline role '{role.Name}' references an unknown colorRole.");
            outlines.Add(role.Name, new(color, RequireNumber(role.Value, "width", 0d, 32d)));
        }
        var shadows = new Dictionary<string, NormalizedShadowStyle>(StringComparer.Ordinal);
        foreach (JsonProperty role in RequireRoleObject(stylesValue, "shadowRoles").EnumerateObject())
        {
            string colorRole = RequireIdentifier(role.Value, "colorRole");
            if (!colors.TryGetValue(colorRole, out NormalizedThemeColor color))
                throw Invalid($"Shadow role '{role.Name}' references an unknown colorRole.");
            shadows.Add(role.Name, new(color,
                RequireNumber(role.Value, "offsetX"), RequireNumber(role.Value, "offsetY"),
                RequireNumber(role.Value, "blur", 0d, 64d)));
        }
        var dynamicStyles = new Dictionary<string, NormalizedDynamicStyle>(StringComparer.Ordinal);
        foreach (JsonProperty role in RequireNonEmptyRoleObject(stylesValue, "dynamicRoles").EnumerateObject())
        {
            string fontRole = RequireIdentifier(role.Value, "fontRole");
            string colorRole = RequireIdentifier(role.Value, "colorRole");
            if (!fonts.ContainsKey(fontRole)) throw Invalid($"Dynamic role '{role.Name}' references an unknown fontRole.");
            if (!colors.TryGetValue(colorRole, out NormalizedThemeColor color))
                throw Invalid($"Dynamic role '{role.Name}' references an unknown colorRole.");
            NormalizedOutlineStyle? outline = null;
            if (role.Value.TryGetProperty("outlineRole", out JsonElement outlineValue) &&
                !outlines.TryGetValue(outlineValue.GetString()!, out outline))
                throw Invalid($"Dynamic role '{role.Name}' references an unknown outlineRole.");
            NormalizedShadowStyle? shadow = null;
            if (role.Value.TryGetProperty("shadowRole", out JsonElement shadowValue) &&
                !shadows.TryGetValue(shadowValue.GetString()!, out shadow))
                throw Invalid($"Dynamic role '{role.Name}' references an unknown shadowRole.");
            dynamicStyles.Add(role.Name, new(fontRole, color,
                RequireNumber(role.Value, "size", exclusiveMinimum: 0d, maximum: 512d), outline, shadow));
        }

        var glyphRoles = new Dictionary<string, NormalizedGlyphRole>(StringComparer.Ordinal);
        JsonElement glyphRoleValues = manifest.GetProperty("glyphs").GetProperty("roles");
        foreach (JsonProperty role in glyphRoleValues.EnumerateObject())
        {
            glyphRoles.Add(role.Name, new(
                ParseGlyphFamily(role.Value.GetProperty("keyboard"), dynamicStyles),
                ParseGlyphFamily(role.Value.GetProperty("keyboardShortcut"), dynamicStyles),
                ParseGlyphFamily(role.Value.GetProperty("ds4"), dynamicStyles),
                ParseGlyphFamily(role.Value.GetProperty("genericAction"), dynamicStyles)));
        }

        Dictionary<string, LayerDraft> dynamicById = dynamicLayers.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var anchors = new List<NormalizedDynamicAnchor>();
        foreach (JsonElement value in anchorValues.EnumerateArray())
        {
            JsonElement anchor = RequireObject(value, "dynamic anchor",
                new[] { "id", "layerId", "role", "bounds", "horizontalAlignment", "verticalAlignment",
                    "overflowPolicy", "minimumScale", "rotation", "visibleStates", "styleRole" },
                new[] { "maxLines", "glyphRole", "safeSurface" });
            string id = RequireIdentifier(anchor, "id");
            string layerId = RequireIdentifier(anchor, "layerId");
            if (!dynamicById.TryGetValue(layerId, out LayerDraft? layer))
                throw Invalid($"Dynamic anchor '{id}' does not name a dynamic layer.");
            string role = RequireString(anchor, "role", 1, 16);
            if ((role == "text" && layer.Kind != "dynamicText") ||
                (role == "glyph" && layer.Kind != "dynamicGlyph") ||
                role is not ("text" or "glyph"))
                throw Invalid($"Dynamic anchor '{id}' role does not match layer '{layerId}'.");
            JsonElement boundsValue = RequireObject(anchor.GetProperty("bounds"), "anchor bounds",
                new[] { "x", "y", "width", "height" });
            double x = RequireNumber(boundsValue, "x", 0d, canvas.Width);
            double y = RequireNumber(boundsValue, "y", 0d, canvas.Height);
            double width = RequireNumber(boundsValue, "width", exclusiveMinimum: 0d, maximum: canvas.Width);
            double height = RequireNumber(boundsValue, "height", exclusiveMinimum: 0d, maximum: canvas.Height);
            if (x + width > canvas.Width + 0.000001d || y + height > canvas.Height + 0.000001d)
                throw Invalid($"Dynamic anchor '{id}' escapes the reference canvas.");
            if (x < layer.Bounds.X - 0.000001d || y < layer.Bounds.Y - 0.000001d ||
                x + width > layer.Bounds.X + layer.Bounds.Width + 0.000001d ||
                y + height > layer.Bounds.Y + layer.Bounds.Height + 0.000001d)
                throw Invalid($"Dynamic anchor '{id}' escapes its dynamic layer bounds.");
            if (anchor.TryGetProperty("safeSurface", out _))
                throw Invalid("safeSurface references require the unsupported maskAssets capability.");
            string horizontal = RequireString(anchor, "horizontalAlignment", 1, 16);
            string vertical = RequireString(anchor, "verticalAlignment", 1, 16);
            string overflow = RequireString(anchor, "overflowPolicy", 1, 16);
            if (horizontal is not ("left" or "center" or "right") ||
                vertical is not ("top" or "center" or "bottom") ||
                overflow is not ("ellipsis" or "shrink" or "clip" or "hide"))
                throw Invalid($"Dynamic anchor '{id}' uses unsupported layout semantics.");
            string styleRole = RequireIdentifier(anchor, "styleRole");
            if (!dynamicStyles.ContainsKey(styleRole)) throw Invalid($"Dynamic anchor '{id}' references an unknown styleRole.");
            string? glyphRole = anchor.TryGetProperty("glyphRole", out JsonElement glyphValue)
                ? glyphValue.GetString() : null;
            if (role == "glyph" && (glyphRole == null || !glyphRoles.ContainsKey(glyphRole)))
                throw Invalid($"Dynamic anchor '{id}' references an unknown glyphRole.");
            if (role == "text" && glyphRole != null) throw Invalid("Text anchors cannot declare glyphRole.");
            int? maxLines = anchor.TryGetProperty("maxLines", out _)
                ? RequireInt32(anchor, "maxLines", 1, 8) : null;
            anchors.Add(new(id, layerId, role, new(x, y, width, height), horizontal, vertical,
                overflow, RequireNumber(anchor, "minimumScale", exclusiveMinimum: 0d, maximum: 1d),
                RequireNumber(anchor, "rotation", -360d, 360d), maxLines,
                Array.AsReadOnly(ParseVisibility(anchor, id)), styleRole, glyphRole));
        }
        if (anchors.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != anchors.Count ||
            anchors.Select(x => x.LayerId).Distinct(StringComparer.Ordinal).Count() != anchors.Count ||
            anchors.Count != dynamicLayers.Length)
            throw Invalid("Every dynamic layer requires exactly one unique dynamic anchor.");

        IReadOnlyList<NormalizedDynamicOwnership> ownership = ValidateOwnership(manifest, layers, anchors, profile);
        return new(anchors, fonts, dynamicStyles, glyphRoles, ownership);
    }

    private static NormalizedGlyphFamily ParseGlyphFamily(JsonElement value,
        IReadOnlyDictionary<string, NormalizedDynamicStyle> styles)
    {
        var result = new List<NormalizedGlyphSource>();
        foreach (JsonElement raw in value.GetProperty("sources").EnumerateArray())
        {
            string type = raw.GetProperty("type").GetString()!;
            if (type == "text")
            {
                string styleRole = raw.GetProperty("styleRole").GetString()!;
                if (!styles.ContainsKey(styleRole)) throw Invalid("A text glyph source references an unknown styleRole.");
                result.Add(new(type, styleRole, null));
            }
            else
            {
                string symbolSet = raw.GetProperty("symbolSet").GetString()!;
                if (!SupportedSymbolSets.Contains(symbolSet))
                    throw Invalid($"Unsupported runtime symbol set: {symbolSet}");
                result.Add(new(type, null, symbolSet));
            }
        }
        return new(result);
    }

    private static NormalizedThemeColor ParseColor(string value)
    {
        const System.Globalization.NumberStyles hex = System.Globalization.NumberStyles.HexNumber;
        byte red = byte.Parse(value.AsSpan(1, 2), hex, System.Globalization.CultureInfo.InvariantCulture);
        byte green = byte.Parse(value.AsSpan(3, 2), hex, System.Globalization.CultureInfo.InvariantCulture);
        byte blue = byte.Parse(value.AsSpan(5, 2), hex, System.Globalization.CultureInfo.InvariantCulture);
        byte alpha = byte.Parse(value.AsSpan(7, 2), hex, System.Globalization.CultureInfo.InvariantCulture);
        return new(red, green, blue, alpha);
    }

    private static HashSet<string> BuildSupportedContentKeys()
    {
        var values = new HashSet<string>(StringComparer.Ordinal)
        { "selectedActionLabel", "selectedActionGlyph" };
        for (int slot = 1; slot <= 8; slot++)
        {
            values.Add($"slot{slot}ActionLabel");
            values.Add($"slot{slot}ActionGlyph");
        }
        return values;
    }

    private static void ValidateStylesAndGlyphs(JsonElement manifest)
    {
        JsonElement styles = RequireObject(manifest.GetProperty("styles"), "styles",
            new[] { "fontRoles", "colorRoles", "outlineRoles", "shadowRoles", "dynamicRoles" });
        JsonElement fonts = RequireNonEmptyRoleObject(styles, "fontRoles");
        foreach (JsonProperty role in fonts.EnumerateObject())
            if (role.Value.ValueKind != JsonValueKind.String ||
                role.Value.GetString() is not ("ui" or "display" or "monospace" or "symbol"))
                throw Invalid("A font role has an unsupported family.");
        JsonElement colors = RequireNonEmptyRoleObject(styles, "colorRoles");
        foreach (JsonProperty role in colors.EnumerateObject())
            if (role.Value.ValueKind != JsonValueKind.String ||
                !Regex.IsMatch(role.Value.GetString()!, "^#[A-Fa-f0-9]{8}$", RegexOptions.CultureInvariant))
                throw Invalid("A color role must be #RRGGBBAA.");
        JsonElement outlines = RequireRoleObject(styles, "outlineRoles");
        foreach (JsonProperty role in outlines.EnumerateObject())
        {
            JsonElement item = RequireObject(role.Value, "outline role", new[] { "colorRole", "width" });
            _ = RequireIdentifier(item, "colorRole");
            _ = RequireNumber(item, "width", 0d, 32d);
        }
        JsonElement shadows = RequireRoleObject(styles, "shadowRoles");
        foreach (JsonProperty role in shadows.EnumerateObject())
        {
            JsonElement item = RequireObject(role.Value, "shadow role",
                new[] { "colorRole", "offsetX", "offsetY", "blur" });
            _ = RequireIdentifier(item, "colorRole");
            _ = RequireNumber(item, "offsetX");
            _ = RequireNumber(item, "offsetY");
            _ = RequireNumber(item, "blur", 0d, 64d);
        }
        JsonElement dynamics = RequireNonEmptyRoleObject(styles, "dynamicRoles");
        foreach (JsonProperty role in dynamics.EnumerateObject())
        {
            JsonElement item = RequireObject(role.Value, "dynamic role",
                new[] { "fontRole", "colorRole", "size" }, new[] { "outlineRole", "shadowRole" });
            _ = RequireIdentifier(item, "fontRole");
            _ = RequireIdentifier(item, "colorRole");
            _ = RequireNumber(item, "size", exclusiveMinimum: 0d, maximum: 512d);
            if (item.TryGetProperty("outlineRole", out _)) _ = RequireIdentifier(item, "outlineRole");
            if (item.TryGetProperty("shadowRole", out _)) _ = RequireIdentifier(item, "shadowRole");
        }

        JsonElement glyphs = RequireObject(manifest.GetProperty("glyphs"), "glyphs", new[] { "roles" });
        JsonElement roles = glyphs.GetProperty("roles");
        if (roles.ValueKind != JsonValueKind.Object || !roles.EnumerateObject().Any())
            throw Invalid("glyph roles must be a non-empty object.");
        foreach (JsonProperty role in roles.EnumerateObject())
        {
            ValidateIdentifierName(role.Name, "glyph role");
            JsonElement value = RequireObject(role.Value, "glyph role",
                new[] { "keyboard", "keyboardShortcut", "ds4", "genericAction" });
            foreach (string family in new[] { "keyboard", "keyboardShortcut", "ds4", "genericAction" })
                ValidateGlyphFamily(value.GetProperty(family));
        }
    }

    private static void ValidateGlyphFamily(JsonElement value)
    {
        JsonElement family = RequireObject(value, "glyph family", new[] { "sources" });
        JsonElement sources = RequireArray(family, "sources");
        if (sources.GetArrayLength() is < 1 or > 3) throw Invalid("A glyph family requires one through three sources.");
        foreach (JsonElement raw in sources.EnumerateArray())
        {
            if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("type", out JsonElement typeValue) ||
                typeValue.ValueKind != JsonValueKind.String)
                throw Invalid("A glyph source requires a type.");
            string type = typeValue.GetString()!;
            switch (type)
            {
                case "text":
                    _ = RequireObject(raw, "text glyph source", new[] { "type", "styleRole" });
                    _ = RequireIdentifier(raw, "styleRole");
                    break;
                case "runtimeSymbol":
                    _ = RequireObject(raw, "runtime glyph source", new[] { "type", "symbolSet" });
                    string symbolSet = RequireIdentifier(raw, "symbolSet");
                    if (!SupportedSymbolSets.Contains(symbolSet))
                        throw Invalid($"Unsupported runtime symbol set: {symbolSet}");
                    break;
                case "themeAsset":
                    throw Invalid("themeGlyphAssets are not supported by the Phase 2 runtime.");
                default:
                    throw Invalid($"Unknown glyph source type: {type}");
            }
        }
    }

    private static JsonElement RequireRoleObject(JsonElement owner, string property)
    {
        JsonElement value = owner.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Object) throw Invalid($"{property} must be an object.");
        foreach (JsonProperty role in value.EnumerateObject()) ValidateIdentifierName(role.Name, property);
        return value;
    }

    private static JsonElement RequireNonEmptyRoleObject(JsonElement owner, string property)
    {
        JsonElement value = RequireRoleObject(owner, property);
        if (!value.EnumerateObject().Any()) throw Invalid($"{property} cannot be empty.");
        return value;
    }

    private static void ValidateIdentifierName(string value, string name)
    {
        if (!UiThemeV2Contract.IdentifierPattern().IsMatch(value)) throw Invalid($"{name} is not an identifier.");
    }

    private static NormalizedFallbackMetadata ParseFallback(JsonElement manifest)
    {
        JsonElement value = RequireObject(manifest.GetProperty("fallback"), "fallback",
            new[] { "onInvalidCandidate", "onUnsupportedVersion", "startupThemeId" });
        RequireStringValue(value, "onInvalidCandidate", "retain-active");
        RequireStringValue(value, "onUnsupportedVersion", "retain-active");
        return new(RequireIdentifier(value, "startupThemeId"), true);
    }

    private static void ValidateTransitions(JsonElement manifest)
    {
        JsonElement value = RequireObject(manifest.GetProperty("transitions"), "transitions", new[] { "mode" });
        RequireStringValue(value, "mode", "instant");
    }

    private static void ValidateOptionalMetadata(JsonElement manifest)
    {
        if (manifest.TryGetProperty("description", out JsonElement description) && description.ValueKind != JsonValueKind.String)
            throw Invalid("description must be a string.");
        if (manifest.TryGetProperty("authoring", out JsonElement authoring))
        {
            authoring = RequireObject(authoring, "authoring", new[] { "method" });
            string method = RequireString(authoring, "method", 1, 32);
            if (method is not ("external-artwork" or "procedural" or "mixed")) throw Invalid("Unknown authoring method.");
        }
    }

    private static void ValidateEmptyArray(JsonElement owner, string property, string feature)
    {
        JsonElement value = RequireArray(owner, property);
        if (value.GetArrayLength() != 0) throw Invalid($"{feature} are not supported by the Phase 2 runtime.");
    }

    private static JsonDocument Parse(string path)
    {
        try { return JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false }); }
        catch (JsonException exception) { throw new InvalidDataException($"Invalid V2 theme JSON: {path}", exception); }
    }

    private static JsonElement RequireObject(JsonElement value, string name, IEnumerable<string> required,
        IEnumerable<string>? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid($"{name} must be an object.");
        HashSet<string> requiredSet = required.ToHashSet(StringComparer.Ordinal);
        HashSet<string> allowed = requiredSet.Concat(optional ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        HashSet<string> actual = value.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        if (!requiredSet.IsSubsetOf(actual)) throw Invalid($"{name} is missing required properties.");
        if (!actual.IsSubsetOf(allowed)) throw Invalid($"{name} contains unknown properties.");
        return value;
    }
    private static JsonElement RequireObject(JsonElement value, string name, HashSet<string> required, HashSet<string> optional) =>
        RequireObject(value, name, (IEnumerable<string>)required, optional);
    private static JsonElement RequireArray(JsonElement owner, string property)
    {
        JsonElement value = owner.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Array) throw Invalid($"{property} must be an array.");
        return value;
    }
    private static string[] ParseStringArray(JsonElement owner, string property) => RequireArray(owner, property)
        .EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw Invalid($"{property} must contain strings.")).ToArray();
    private static string RequireString(JsonElement owner, string property, int min, int max)
    {
        JsonElement value = owner.GetProperty(property);
        if (value.ValueKind != JsonValueKind.String) throw Invalid($"{property} must be a string.");
        string result = value.GetString()!;
        if (result.Length < min || result.Length > max) throw Invalid($"{property} has invalid length.");
        return result;
    }
    private static string RequireIdentifier(JsonElement owner, string property)
    {
        string value = RequireString(owner, property, 1, 96);
        if (!UiThemeV2Contract.IdentifierPattern().IsMatch(value)) throw Invalid($"{property} is not an identifier.");
        return value;
    }
    private static void RequireStringValue(JsonElement owner, string property, string expected)
    {
        if (!string.Equals(RequireString(owner, property, 1, 128), expected, StringComparison.Ordinal))
            throw Invalid($"{property} must be '{expected}'.");
    }
    private static void RequireInteger(JsonElement owner, string property, int expected)
    {
        if (RequireInt32(owner, property, int.MinValue, int.MaxValue) != expected) throw Invalid($"{property} must be {expected}.");
    }
    private static int RequireInt32(JsonElement owner, string property, int minimum, int maximum)
    {
        JsonElement value = owner.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result) || result < minimum || result > maximum)
            throw Invalid($"{property} must be an integer from {minimum} through {maximum}.");
        return result;
    }
    private static double RequireNumber(JsonElement owner, string property, double minimum = double.MinValue,
        double maximum = double.MaxValue, double? exclusiveMinimum = null)
    {
        JsonElement value = owner.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double result) || !double.IsFinite(result) ||
            result < minimum || result > maximum || exclusiveMinimum.HasValue && result <= exclusiveMinimum.Value)
            throw Invalid($"{property} is outside its allowed numeric range.");
        return result;
    }
    private static string RequirePackagePath(JsonElement owner, string property) =>
        ValidatePackagePath(RequireString(owner, property, 1, 240));
    private static string ValidatePackagePath(string value)
    {
        if (!UiThemeV2Contract.PackagePathPattern().IsMatch(value) || value.Contains('\\') || value.Contains(':') ||
            value.Contains("..", StringComparison.Ordinal) || value.StartsWith('~') || Path.IsPathRooted(value) ||
            Uri.TryCreate(value, UriKind.Absolute, out _))
            throw Invalid($"Unsafe package path: {value}");
        if (!value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Phase 2 assets must be PNG files: {value}");
        return value;
    }
    private static string ResolvePackagePath(string root, string packagePath)
    {
        string candidate = Path.GetFullPath(Path.Combine(root, packagePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw Invalid("Package path escapes its root.");
        return candidate;
    }

    private static void EnsureResolvedInsideRoot(string root, string packagePath)
    {
        var rootInfo = new DirectoryInfo(root);
        string resolvedRoot = rootInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? rootInfo.FullName;
        string current = resolvedRoot;
        string[] segments = packagePath.Split('/');
        for (int index = 0; index < segments.Length - 1; index++)
        {
            var directory = new DirectoryInfo(Path.Combine(current, segments[index]));
            current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }
        var file = new FileInfo(Path.Combine(current, segments[^1]));
        string resolvedFile = file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName;
        string prefix = resolvedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? resolvedRoot
            : resolvedRoot + Path.DirectorySeparatorChar;
        if (!resolvedFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Package asset resolves outside its root: {packagePath}");
    }
    private static InvalidDataException Invalid(string message) => new(message);

    private sealed record LayerDraft(string Id, string Kind, int ZIndex, int Order,
        NormalizedReferenceBounds Bounds, string[] VisibleStates, string Ownership, string? AssetPath);
    private sealed record StateDraft(string Name, int? SlotId, Dictionary<string, string> Assets);
}
