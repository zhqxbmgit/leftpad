namespace PcDs4Server;

// Also serves as the identity token carried across asynchronous UI dispatch.
// Only Ds4Service mutates its lifecycle, under the service input lock.
public sealed class RadialActionPressSession
{
    internal RadialActionPressSession(RadialTriggerSource source) => Source = source;

    public RadialTriggerSource Source { get; }
    internal bool ReleaseRequested { get; set; }
    internal bool BeginHandled { get; set; }
    internal int SelectedSlot { get; set; }
    internal string[] KeyboardSources { get; set; } = [];
    internal RadialDs4ActionMapping? Ds4Target { get; set; }
    internal bool OrdinaryTargetPressed { get; set; }
}

internal readonly record struct RadialActionSelection(int Slot, RadialSlotMapping Mapping);
