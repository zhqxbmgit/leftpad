namespace PcDs4Server;

public static class RadialActionCompletionHandler
{
    public static string Handle(
        Ds4Service service,
        RadialMenuSettings activeSettings,
        RadialMenuCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(activeSettings);

        if (completion.IsCancelled) return "[环形菜单] 已取消";

        RadialSlotMapping mapping = RadialActionResolver.GetMapping(activeSettings, completion);
        return mapping.Kind switch
        {
            RadialActionKind.KeyboardKey or RadialActionKind.KeyboardShortcut =>
                ExecuteKeyboardAction(service, mapping, completion.SelectedSlot),
            RadialActionKind.Ds4Button =>
                $"[环形菜单] 已确认：Slot {completion.SelectedSlot}（配置：DS4 {mapping.Ds4Button!.ToUpperInvariant()}；尚未执行）",
            _ => $"[环形菜单] 已确认：Slot {completion.SelectedSlot}（未配置动作）"
        };
    }

    private static string ExecuteKeyboardAction(
        Ds4Service service,
        RadialSlotMapping mapping,
        int slot)
    {
        if (!service.TryExecuteRadialKeyboardAction(mapping, out string error))
            return $"[环形菜单] Slot {slot} 键盘动作执行失败：{error}";

        string action = mapping.Kind == RadialActionKind.KeyboardKey
            ? $"键盘 {mapping.Key}"
            : RadialActionResolver.FormatShortcut(mapping);
        return $"[环形菜单] 已执行：Slot {slot}（{action}）";
    }
}
