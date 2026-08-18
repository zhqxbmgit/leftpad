using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server;

public enum Ds4ActionKind
{
    DigitalButton,
    AnalogTrigger
}

public sealed record Ds4ActionMapping(
    string ProtocolKey,
    Ds4ActionKind Kind,
    DualShock4Button? DigitalButton = null,
    DualShock4Slider? Trigger = null);

public static class Ds4ActionMapper
{
    private static readonly IReadOnlyDictionary<string, Ds4ActionMapping> Mappings =
        new Dictionary<string, Ds4ActionMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["cross"] = Digital("cross", DualShock4Button.Cross),
            ["circle"] = Digital("circle", DualShock4Button.Circle),
            ["square"] = Digital("square", DualShock4Button.Square),
            ["triangle"] = Digital("triangle", DualShock4Button.Triangle),
            ["l1"] = Digital("l1", DualShock4Button.ShoulderLeft),
            ["r1"] = Digital("r1", DualShock4Button.ShoulderRight),
            ["l3"] = Digital("l3", DualShock4Button.ThumbLeft),
            ["r3"] = Digital("r3", DualShock4Button.ThumbRight),
            ["l2"] = AnalogTrigger("l2", DualShock4Slider.LeftTrigger),
            ["r2"] = AnalogTrigger("r2", DualShock4Slider.RightTrigger)
        };

    public static bool TryGet(string? protocolKey, out Ds4ActionMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(protocolKey) &&
            Mappings.TryGetValue(protocolKey, out Ds4ActionMapping? found))
        {
            mapping = found;
            return true;
        }

        mapping = null!;
        return false;
    }

    public static bool TryGetPressedState(string? action, out bool isPressed)
    {
        if (string.Equals(action, "down", StringComparison.OrdinalIgnoreCase))
        {
            isPressed = true;
            return true;
        }

        if (string.Equals(action, "up", StringComparison.OrdinalIgnoreCase))
        {
            isPressed = false;
            return true;
        }

        isPressed = false;
        return false;
    }

    public static byte GetTriggerValue(bool isPressed)
    {
        return isPressed ? byte.MaxValue : byte.MinValue;
    }

    private static Ds4ActionMapping Digital(string key, DualShock4Button button)
    {
        return new Ds4ActionMapping(key, Ds4ActionKind.DigitalButton, DigitalButton: button);
    }

    private static Ds4ActionMapping AnalogTrigger(string key, DualShock4Slider trigger)
    {
        return new Ds4ActionMapping(key, Ds4ActionKind.AnalogTrigger, Trigger: trigger);
    }
}

public enum Ds4ControlResetReason
{
    Disconnect,
    SessionReplacement,
    ServiceStop,
    OutputFailure
}

public sealed record Ds4ControlRelease(
    IReadOnlyList<DualShock4Button> DigitalButtons,
    bool ResetDPad,
    bool ResetLeftTrigger,
    bool ResetRightTrigger,
    Ds4ControlResetReason Reason);

public sealed class Ds4ControlState
{
    private readonly HashSet<DualShock4Button> _activeButtons = new();

    public IReadOnlyCollection<DualShock4Button> ActiveButtons => _activeButtons;
    public DualShock4DPadDirection DPadDirection { get; private set; } = DualShock4DPadDirection.None;
    public byte LeftTrigger { get; private set; }
    public byte RightTrigger { get; private set; }
    public Ds4ControlResetReason? LastResetReason { get; private set; }

    public void SetDigitalButton(DualShock4Button button, bool isPressed)
    {
        if (isPressed)
        {
            _activeButtons.Add(button);
        }
        else
        {
            _activeButtons.Remove(button);
        }

        LastResetReason = null;
    }

    public bool SetTrigger(DualShock4Slider trigger, byte value)
    {
        if (trigger == DualShock4Slider.LeftTrigger)
        {
            LeftTrigger = value;
        }
        else if (trigger == DualShock4Slider.RightTrigger)
        {
            RightTrigger = value;
        }
        else
        {
            return false;
        }

        LastResetReason = null;
        return true;
    }

    public void SetDPadDirection(DualShock4DPadDirection direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        DPadDirection = direction;
        LastResetReason = null;
    }

    public Ds4ControlRelease ReleaseAll(Ds4ControlResetReason reason)
    {
        var release = new Ds4ControlRelease(
            _activeButtons.ToArray(),
            !Equals(DPadDirection, DualShock4DPadDirection.None),
            LeftTrigger != 0,
            RightTrigger != 0,
            reason);

        _activeButtons.Clear();
        DPadDirection = DualShock4DPadDirection.None;
        LeftTrigger = 0;
        RightTrigger = 0;
        LastResetReason = reason;
        return release;
    }
}
