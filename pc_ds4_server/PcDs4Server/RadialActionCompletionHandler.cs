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

        RadialSlotMapping mapping = RadialActionResolver.GetMapping(
            activeSettings,
            activeSettings.MappingProfileId,
            completion.SelectedSlot);
        return mapping.Kind switch
        {
            RadialActionKind.KeyboardKey or RadialActionKind.KeyboardShortcut =>
                ExecuteKeyboardAction(service, mapping, completion.SelectedSlot),
            RadialActionKind.Ds4Button =>
                ExecuteDs4Action(service, mapping, completion.SelectedSlot),
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

    private static string ExecuteDs4Action(
        Ds4Service service,
        RadialSlotMapping mapping,
        int slot)
    {
        if (!service.TryExecuteRadialDs4Action(mapping, out string error))
            return $"[环形菜单] Slot {slot} DS4 动作执行失败：{error}";

        RadialDs4ActionCatalog.TryGet(mapping.Ds4Button, out RadialDs4ActionMapping action);
        return $"[环形菜单] 已执行：Slot {slot}（DS4 {action.DisplayName}）";
    }
}
