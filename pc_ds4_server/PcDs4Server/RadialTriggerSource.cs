namespace PcDs4Server;

public enum RadialTriggerSourceKind
{
    Action,
    Move
}

public readonly record struct RadialTriggerSource
{
    private RadialTriggerSource(RadialTriggerSourceKind kind, string? actionId)
    {
        Kind = kind;
        ActionId = actionId;
    }

    public RadialTriggerSourceKind Kind { get; }
    public string? ActionId { get; }

    public static RadialTriggerSource ForAction(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
            throw new ArgumentException("An action radial trigger requires an action ID.", nameof(actionId));

        return new RadialTriggerSource(
            RadialTriggerSourceKind.Action,
            actionId.Trim().ToLowerInvariant());
    }

    public static RadialTriggerSource Move { get; } =
        new(RadialTriggerSourceKind.Move, actionId: null);
}

public readonly record struct RadialMenuCompletion(int SelectedSlot)
{
    public bool IsCancelled => SelectedSlot == 0;
}
