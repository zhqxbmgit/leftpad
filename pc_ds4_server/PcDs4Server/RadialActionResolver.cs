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

    public static string CreateLogMessage(
        RadialMenuSettings activeSettings,
        RadialMenuCompletion completion)
    {
        if (completion.IsCancelled) return "[环形菜单] 已取消";

        RadialSlotMapping mapping = GetMapping(activeSettings, completion);
        string detail = mapping.Kind switch
        {
            RadialActionKind.None => "未配置动作",
            RadialActionKind.KeyboardKey => $"配置：键盘 {mapping.Key}；本阶段未执行",
            RadialActionKind.KeyboardShortcut =>
                $"配置：{FormatShortcut(mapping)}；本阶段未执行",
            RadialActionKind.Ds4Button =>
                $"配置：DS4 {mapping.Ds4Button!.ToUpperInvariant()}；本阶段未执行",
            _ => "未配置动作"
        };
        return $"[环形菜单] 已确认：Slot {completion.SelectedSlot}（{detail}）";
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
