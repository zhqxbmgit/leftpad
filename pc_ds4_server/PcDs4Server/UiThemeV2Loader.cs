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
    { "fullStateFrame", "instantTransitions" };
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
        ValidateEmptyArray(manifest, "dynamicAnchors", "dynamicAnchors");
        ValidateEmptyArray(manifest, "masks", "maskAssets");
        if (manifest.TryGetProperty("visualRegions", out JsonElement regions) &&
            (regions.ValueKind != JsonValueKind.Array || regions.GetArrayLength() != 0))
            throw Invalid("visualRegions are not supported by the Phase 2 runtime.");
        ValidateStylesAndGlyphs(manifest);
        ValidateFallback(manifest);
        string[] capabilities = ValidateCapabilities(manifest);
        ValidateTransitions(manifest);

        List<LayerDraft> layerDrafts = ParseLayers(manifest, canvas);
        ValidateOwnership(manifest, layerDrafts);
        List<StateDraft> stateDrafts = ParseStates(manifest, layout.SlotCount, layerDrafts);
        HashSet<string> stateNames = stateDrafts.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        if (layerDrafts.SelectMany(x => x.VisibleStates).Any(x =>
                x is not ("all" or "selected") && !stateNames.Contains(x)))
            throw Invalid("A layer visibility selector names a state outside the single layout.");
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
            new(RadialVisualPackContract.DefaultVisualPackId, true), capabilities, placement);
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
            if (kind is not ("staticAsset" or "stateAsset"))
                throw Invalid($"Unsupported V2 layer kind: {kind}");
            int zIndex = RequireInt32(layer, "zIndex", -32768, 32767);
            string ownership = RequireString(layer, "ownership", 1, 32);
            if ((kind == "staticAsset" && ownership != "STATIC") ||
                (kind == "stateAsset" && ownership != "STATE_ASSET"))
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
            else if (layer.TryGetProperty("asset", out _)) throw Invalid("stateAsset layers cannot declare asset.");
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
        JsonElement values = RequireArray(manifest, "elementOwnership");
        if (values.GetArrayLength() == 0) throw Invalid("elementOwnership cannot be empty.");
        Dictionary<string, LayerDraft> byId = layers.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (JsonElement value in values.EnumerateArray())
        {
            JsonElement item = RequireObject(value, "elementOwnership", new[] { "element", "owner", "layerId" }, new[] { "contentKey" });
            _ = RequireIdentifier(item, "element");
            string owner = RequireString(item, "owner", 1, 32);
            string layerId = RequireIdentifier(item, "layerId");
            if (!byId.TryGetValue(layerId, out LayerDraft? layer) || owner != layer.Ownership)
                throw Invalid("elementOwnership does not match a supported layer.");
            if (item.TryGetProperty("contentKey", out _)) throw Invalid("Dynamic element ownership is not supported.");
        }
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
        if (!SupportedRequiredCapabilities.All(required.Contains))
            throw Invalid("fullStateFrame and instantTransitions are required.");
        return required;
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
                    _ = RequireIdentifier(raw, "symbolSet");
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

    private static void ValidateFallback(JsonElement manifest)
    {
        JsonElement value = RequireObject(manifest.GetProperty("fallback"), "fallback",
            new[] { "onInvalidCandidate", "onUnsupportedVersion", "startupThemeId" });
        RequireStringValue(value, "onInvalidCandidate", "retain-active");
        RequireStringValue(value, "onUnsupportedVersion", "retain-active");
        _ = RequireIdentifier(value, "startupThemeId");
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
