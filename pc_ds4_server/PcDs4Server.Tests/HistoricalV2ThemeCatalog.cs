namespace PcDs4Server.Tests;

// V2 coverage uses an isolated copy of the retained historical prototype package.
// It is test data only and is never discovered by the production catalog or publish output.
internal static class HistoricalV2ThemeCatalog
{
    internal const string ReferenceThemeId = "reference-dark-fantasy-radial8-dynamic-spike";
    private static readonly object Sync = new();

    internal static RadialVisualPackCatalog Create()
    {
        EnsureSelectedEmphasisCompanion();
        return new(themeDiscoveryRoot: Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "ReferenceV2Themes"));
    }

    private static void EnsureSelectedEmphasisCompanion()
    {
        string root = UniversalSelectedEmphasisCatalog.DiscoveryRoot;
        string target = Path.Combine(root, ReferenceThemeId);
        if (File.Exists(Path.Combine(target, "manifest.json"))) return;

        lock (Sync)
        {
            if (File.Exists(Path.Combine(target, "manifest.json"))) return;
            string source = Directory.GetDirectories(root).Single(directory =>
            {
                string manifest = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifest)) return false;
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(manifest));
                return document.RootElement.GetProperty("sourceProtocolVersion").GetInt32() == 2;
            });
            foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(source, path));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(path, destination, overwrite: true);
            }

            string manifestPath = Path.Combine(target, "manifest.json");
            string manifestContent = File.ReadAllText(manifestPath);
            using var parsed = System.Text.Json.JsonDocument.Parse(manifestContent);
            string sourceThemeId = parsed.RootElement.GetProperty("themeId").GetString()!;
            File.WriteAllText(manifestPath, manifestContent.Replace(sourceThemeId, ReferenceThemeId));
        }
    }
}
