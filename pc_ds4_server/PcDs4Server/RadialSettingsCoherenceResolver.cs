namespace PcDs4Server;

internal sealed record RadialSettingsCoherenceResult(
    RadialMenuSettings Settings,
    bool Repaired);

internal static class RadialSettingsCoherenceResolver
{
    public static RadialSettingsCoherenceResult Resolve(
        RadialMenuSettings settings,
        RadialVisualPackCatalogSnapshot catalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);

        RadialVisualPackCatalogEntry? pack = catalog.Find(settings.VisualPackId);
        if (pack == null)
            return new(settings, false);

        string authoritativeProfileId = pack.LayoutDefinition.ProfileId;
        if (string.Equals(
                settings.MappingProfileId,
                authoritativeProfileId,
                StringComparison.Ordinal))
        {
            return new(settings, false);
        }

        return new(
            settings with { MappingProfileId = authoritativeProfileId },
            true);
    }
}
