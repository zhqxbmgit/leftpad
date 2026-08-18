using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server;

public enum RadialDs4ActionKind
{
    DigitalButton,
    DPad
}

public sealed record RadialDs4ActionMapping(
    string Id,
    string DisplayName,
    RadialDs4ActionKind Kind,
    DualShock4Button? DigitalButton = null,
    DualShock4DPadDirection? DPadDirection = null);

public static class RadialDs4ActionCatalog
{
    private static readonly RadialDs4ActionMapping[] Catalog =
    {
        Digital("cross", "CROSS", DualShock4Button.Cross),
        Digital("circle", "CIRCLE", DualShock4Button.Circle),
        Digital("square", "SQUARE", DualShock4Button.Square),
        Digital("triangle", "TRIANGLE", DualShock4Button.Triangle),
        Digital("l1", "L1", DualShock4Button.ShoulderLeft),
        Digital("l3", "L3", DualShock4Button.ThumbLeft),
        Digital("r3", "R3", DualShock4Button.ThumbRight),
        new(
            "dpad_down",
            "十字键下",
            RadialDs4ActionKind.DPad,
            DPadDirection: DualShock4DPadDirection.South)
    };

    private static readonly IReadOnlyDictionary<string, RadialDs4ActionMapping> ById =
        Catalog.ToDictionary(action => action.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<RadialDs4ActionMapping> Actions => Catalog;

    public static bool TryGet(string? id, out RadialDs4ActionMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(id) &&
            ById.TryGetValue(id, out RadialDs4ActionMapping? found))
        {
            mapping = found;
            return true;
        }

        mapping = null!;
        return false;
    }

    private static RadialDs4ActionMapping Digital(
        string id,
        string displayName,
        DualShock4Button button) =>
        new(id, displayName, RadialDs4ActionKind.DigitalButton, DigitalButton: button);
}
