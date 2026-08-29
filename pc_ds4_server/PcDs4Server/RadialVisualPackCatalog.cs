namespace PcDs4Server;

public sealed class RadialVisualPackCatalogEntry
{
    private readonly RadialVisualPackDefinition? _definition;

    internal RadialVisualPackCatalogEntry(RadialVisualPackDefinition definition)
    {
        _definition = definition;
        Plan = V1VisualPackCompatibilityAdapter.BuildPlan(definition);
        DirectoryPath = definition.DirectoryPath;
        Version = definition.Manifest.Version;
    }

    internal RadialVisualPackCatalogEntry(UiThemeV2Package package,
        RadialVisualPackDefinition layoutAuthority)
    {
        _definition = layoutAuthority;
        Plan = package.Plan;
        DirectoryPath = package.DirectoryPath;
        Version = UiThemeV2Contract.ProtocolVersion;
    }

    internal RadialVisualPackDefinition Definition => _definition!;
    internal RadialVisualPackDefinition? V1Definition => IsV2 ? null : _definition;
    internal NormalizedRenderPlan Plan { get; }
    internal LayoutDefinition LayoutDefinition => Plan.LayoutDefinition;
    internal bool IsV2 => Plan.SourceProtocolVersion == 2;
    public string Id => Plan.ThemeId;
    public string Name => Plan.DisplayName;
    public int Version { get; }
    public string DirectoryPath { get; }
    public override string ToString() => Name;
}

public sealed record RadialVisualPackCatalogIssue(string DirectoryPath, string? PackId, string Message);

public sealed class RadialVisualPackCatalogSnapshot
{
    internal RadialVisualPackCatalogSnapshot(IReadOnlyList<RadialVisualPackCatalogEntry> packs,
        IReadOnlyList<RadialVisualPackCatalogIssue> issues)
    { Packs = packs; Issues = issues; }
    public IReadOnlyList<RadialVisualPackCatalogEntry> Packs { get; }
    public IReadOnlyList<RadialVisualPackCatalogIssue> Issues { get; }
    public RadialVisualPackCatalogEntry? Find(string? id) => string.IsNullOrWhiteSpace(id) ? null :
        Packs.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
    public RadialVisualPackCatalogEntry? ResolveSelection(string? requestedId)
    {
        if (string.IsNullOrWhiteSpace(requestedId))
        {
            return Find(RadialVisualPackContract.DefaultVisualPackId) ??
                Find(RadialVisualPackContract.FallbackVisualPackId) ??
                Packs.FirstOrDefault();
        }

        return Find(requestedId) ??
            Find(RadialVisualPackContract.FallbackVisualPackId) ??
            Find(RadialVisualPackContract.DefaultVisualPackId) ??
            Packs.FirstOrDefault();
    }
}

public sealed class RadialVisualPackCatalog
{
    public RadialVisualPackCatalog(string? discoveryRoot = null, string? themeDiscoveryRoot = null)
    {
        DiscoveryRoot = Path.GetFullPath(discoveryRoot ?? RadialVisualPackContract.DiscoveryRoot);
        ThemeDiscoveryRoot = Path.GetFullPath(themeDiscoveryRoot ??
            (discoveryRoot == null ? UiThemeV2Contract.DiscoveryRoot :
                Path.Combine(DiscoveryRoot, ".v2-themes-not-configured")));
    }

    public string DiscoveryRoot { get; }
    public string ThemeDiscoveryRoot { get; }

    public RadialVisualPackCatalogSnapshot Discover()
    {
        var issues = new List<RadialVisualPackCatalogIssue>();
        List<RadialVisualPackCatalogEntry> v1 = DiscoverV1(issues);
        List<RadialVisualPackCatalogEntry> entries = new(v1);
        if (Directory.Exists(ThemeDiscoveryRoot)) DiscoverV2(v1, entries, issues);
        RadialVisualPackCatalogEntry[] sorted = entries
            .OrderBy(x => x.Id == RadialVisualPackContract.LegacyV1DefaultPackId ? 0 : 1)
            .ThenBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return new(sorted, issues);
    }

    private List<RadialVisualPackCatalogEntry> DiscoverV1(List<RadialVisualPackCatalogIssue> issues)
    {
        var entries = new List<RadialVisualPackCatalogEntry>();
        if (!Directory.Exists(DiscoveryRoot))
        {
            issues.Add(new(DiscoveryRoot, null, "Visual pack discovery root was not found."));
            return entries;
        }
        string[] directories;
        try { directories = Directory.GetDirectories(DiscoveryRoot).Order(StringComparer.Ordinal).ToArray(); }
        catch (Exception exception) when (IsDiscoveryException(exception))
        { issues.Add(new(DiscoveryRoot, null, exception.Message)); return entries; }
        var candidates = new List<(string Directory, UiVisualPackManifest Manifest)>();
        foreach (string directory in directories)
        {
            // Empty build-output directories and authoring placeholders are not candidates.
            if (!File.Exists(Path.Combine(directory, "manifest.json"))) continue;
            try
            {
                UiVisualPackManifest manifest = RadialVisualPackDefinition.ReadManifest(directory);
                if (string.IsNullOrWhiteSpace(manifest.Id)) throw new InvalidDataException("Visual pack id is required.");
                candidates.Add((directory, manifest));
            }
            catch (Exception exception) when (IsDiscoveryException(exception)) { issues.Add(new(directory, null, exception.Message)); }
        }
        foreach (IGrouping<string, (string Directory, UiVisualPackManifest Manifest)> group in
                 candidates.GroupBy(x => x.Manifest.Id, StringComparer.Ordinal))
        {
            var values = group.ToArray();
            if (values.Length > 1)
            {
                issues.Add(new(DiscoveryRoot, group.Key,
                    $"Duplicate visual pack id '{group.Key}': {string.Join(", ", values.Select(x => x.Directory))}"));
                continue;
            }
            try { entries.Add(new(RadialVisualPackDefinition.Load(values[0].Directory))); }
            catch (Exception exception) when (IsDiscoveryException(exception))
            { issues.Add(new(values[0].Directory, group.Key, exception.Message)); }
        }
        return entries;
    }

    private void DiscoverV2(IReadOnlyList<RadialVisualPackCatalogEntry> v1,
        List<RadialVisualPackCatalogEntry> entries, List<RadialVisualPackCatalogIssue> issues)
    {
        string[] directories;
        try { directories = Directory.GetDirectories(ThemeDiscoveryRoot).Order(StringComparer.Ordinal).ToArray(); }
        catch (Exception exception) when (IsDiscoveryException(exception))
        { issues.Add(new(ThemeDiscoveryRoot, null, exception.Message)); return; }
        var identities = directories.Select(x => (Directory: x, Id: UiThemeV2Loader.TryReadId(x))).ToArray();
        HashSet<string> v1Ids = v1.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> duplicateIds = identities.Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id!, StringComparer.Ordinal).Where(x => x.Count() > 1)
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach ((string directory, string? id) in identities)
        {
            if (string.IsNullOrWhiteSpace(id))
            { issues.Add(new(directory, null, "V2 theme id is missing or its manifest is unreadable.")); continue; }
            if (duplicateIds.Contains(id))
            { issues.Add(new(directory, id, $"Duplicate V2 theme id '{id}'.")); continue; }
            if (v1Ids.Contains(id))
            { issues.Add(new(directory, id, $"V2 theme id '{id}' collides with a V1 visual pack.")); continue; }
            try
            {
                LayoutProfileRegistration registration = LayoutProfileRegistry.GetRequired(ReadLayoutProfile(directory));
                RadialVisualPackCatalogEntry? authority = v1.FirstOrDefault(x =>
                    string.Equals(x.LayoutDefinition.ProfileId, registration.ProfileId, StringComparison.Ordinal));
                if (authority == null) throw new InvalidDataException(
                    $"No authoritative V1 layout is available for '{registration.ProfileId}'.");
                entries.Add(new(UiThemeV2Loader.Load(directory, authority.LayoutDefinition), authority.Definition));
            }
            catch (Exception exception) when (IsDiscoveryException(exception))
            { issues.Add(new(directory, id, exception.Message)); }
        }
    }

    private static string ReadLayoutProfile(string directory)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(directory, UiThemeV2Contract.ManifestFileName)));
        if (!document.RootElement.TryGetProperty("layoutProfile", out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String)
            throw new InvalidDataException("V2 layoutProfile is required.");
        return value.GetString()!;
    }

    private static bool IsDiscoveryException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or
        ArgumentException or NotSupportedException or System.Text.Json.JsonException;
}
