namespace PcDs4Server;

public static class RadialActionResolver
{
    public static RadialSlotMapping GetMapping(
        RadialMenuSettings activeSettings,
        string profileId,
        int slotId)
    {
        ArgumentNullException.ThrowIfNull(activeSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        RadialSlotMappings mappings = activeSettings.GetProfileMappings(profileId);
        if (slotId < 1 || slotId > mappings.Count) return RadialSlotMapping.None;

        return mappings[slotId - 1];
    }

    public static string FormatShortcut(RadialSlotMapping mapping)
    {
        var parts = new List<string>(5);
        if (mapping.Ctrl) parts.Add("Ctrl");
        if (mapping.Alt) parts.Add("Alt");
        if (mapping.Shift) parts.Add("Shift");
        if (mapping.Win) parts.Add("Win");
        if (mapping.Key != null) parts.Add(mapping.Key.Value.ToString());
        return string.Join("+", parts);
    }
}
