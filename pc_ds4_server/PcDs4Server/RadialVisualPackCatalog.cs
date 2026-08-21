namespace PcDs4Server;

public sealed class RadialVisualPackCatalogEntry
{
    internal RadialVisualPackCatalogEntry(RadialVisualPackDefinition definition)
    {
        Definition = definition;
    }

    internal RadialVisualPackDefinition Definition { get; }
    public string Id => Definition.Manifest.Id;
    public string Name => Definition.Manifest.Name;
    public int Version => Definition.Manifest.Version;
    public string DirectoryPath => Definition.DirectoryPath;

    public override string ToString() => Name;
}

public sealed record RadialVisualPackCatalogIssue(
    string DirectoryPath,
    string? PackId,
    string Message);

public sealed class RadialVisualPackCatalogSnapshot
{
    internal RadialVisualPackCatalogSnapshot(
        IReadOnlyList<RadialVisualPackCatalogEntry> packs,
        IReadOnlyList<RadialVisualPackCatalogIssue> issues)
    {
        Packs = packs;
        Issues = issues;
    }

    public IReadOnlyList<RadialVisualPackCatalogEntry> Packs { get; }
    public IReadOnlyList<RadialVisualPackCatalogIssue> Issues { get; }

    public RadialVisualPackCatalogEntry? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Packs.FirstOrDefault(pack => string.Equals(pack.Id, id, StringComparison.Ordinal));

    public RadialVisualPackCatalogEntry? ResolveSelection(string? requestedId) =>
        Find(requestedId) ??
        Find(RadialVisualPackContract.DefaultVisualPackId) ??
        Packs.FirstOrDefault();
}

public sealed class RadialVisualPackCatalog
{
    public RadialVisualPackCatalog(string? discoveryRoot = null)
    {
        DiscoveryRoot = Path.GetFullPath(
            discoveryRoot ?? RadialVisualPackContract.DiscoveryRoot);
    }

    public string DiscoveryRoot { get; }

    public RadialVisualPackCatalogSnapshot Discover()
    {
        var issues = new List<RadialVisualPackCatalogIssue>();
        if (!Directory.Exists(DiscoveryRoot))
        {
            issues.Add(new RadialVisualPackCatalogIssue(
                DiscoveryRoot,
                null,
                "Visual pack discovery root was not found."));
            return new RadialVisualPackCatalogSnapshot(
                Array.Empty<RadialVisualPackCatalogEntry>(),
                issues);
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(DiscoveryRoot)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (IsDiscoveryException(exception))
        {
            issues.Add(new RadialVisualPackCatalogIssue(
                DiscoveryRoot,
                null,
                exception.Message));
            return new RadialVisualPackCatalogSnapshot(
                Array.Empty<RadialVisualPackCatalogEntry>(),
                issues);
        }

        var manifests = new List<ManifestCandidate>();
        foreach (string directory in directories)
        {
            try
            {
                UiVisualPackManifest manifest = RadialVisualPackDefinition.ReadManifest(directory);
                if (string.IsNullOrWhiteSpace(manifest.Id))
                {
                    issues.Add(new RadialVisualPackCatalogIssue(
                        directory,
                        null,
                        "Visual pack id is required."));
                    continue;
                }
                manifests.Add(new ManifestCandidate(directory, manifest));
            }
            catch (Exception exception) when (IsDiscoveryException(exception))
            {
                issues.Add(new RadialVisualPackCatalogIssue(
                    directory,
                    null,
                    exception.Message));
            }
        }

        var entries = new List<RadialVisualPackCatalogEntry>();
        foreach (IGrouping<string, ManifestCandidate> group in manifests.GroupBy(
                     candidate => candidate.Manifest.Id,
                     StringComparer.Ordinal))
        {
            ManifestCandidate[] candidates = group.ToArray();
            if (candidates.Length > 1)
            {
                string directoriesText = string.Join(
                    ", ",
                    candidates.Select(candidate => candidate.DirectoryPath));
                issues.Add(new RadialVisualPackCatalogIssue(
                    DiscoveryRoot,
                    group.Key,
                    $"Duplicate visual pack id '{group.Key}': {directoriesText}"));
                continue;
            }

            ManifestCandidate candidate = candidates[0];
            try
            {
                entries.Add(new RadialVisualPackCatalogEntry(
                    RadialVisualPackDefinition.Load(candidate.DirectoryPath)));
            }
            catch (Exception exception) when (IsDiscoveryException(exception))
            {
                issues.Add(new RadialVisualPackCatalogIssue(
                    candidate.DirectoryPath,
                    candidate.Manifest.Id,
                    exception.Message));
            }
        }

        RadialVisualPackCatalogEntry[] sorted = entries
            .OrderBy(
                entry => string.Equals(
                    entry.Id,
                    RadialVisualPackContract.DefaultVisualPackId,
                    StringComparison.Ordinal)
                    ? 0
                    : 1)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
        return new RadialVisualPackCatalogSnapshot(sorted, issues);
    }

    private static bool IsDiscoveryException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or
        ArgumentException or NotSupportedException;

    private sealed record ManifestCandidate(
        string DirectoryPath,
        UiVisualPackManifest Manifest);
}
