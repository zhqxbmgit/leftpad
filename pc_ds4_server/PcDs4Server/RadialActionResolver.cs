namespace PcDs4Server;

public static class RadialActionResolver
{
    public static RadialSlotMapping GetMapping(
        RadialMenuSettings activeSettings,
        RadialMenuCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(activeSettings);
        if (completion.IsCancelled ||
            completion.SelectedSlot is < 1 or > RadialSlotMappings.SlotCount)
        {
            return RadialSlotMapping.None;
        }

        return activeSettings.SlotMappings[completion.SelectedSlot - 1];
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
