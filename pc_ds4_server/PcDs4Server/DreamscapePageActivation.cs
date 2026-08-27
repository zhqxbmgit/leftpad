using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcDs4Server;

internal sealed class DreamscapePageActivationState
{
    private bool _pageActive;
    private bool _frontendReady;
    private bool? _lastPublishedPageActive;

    public bool PageActive => _pageActive;
    public bool FrontendReady => _frontendReady;

    public void SetPageActive(bool active) => _pageActive = active;

    public void NavigationStarting()
    {
        _frontendReady = false;
        _lastPublishedPageActive = null;
    }

    public void NavigationCompleted()
    {
        _frontendReady = true;
        _lastPublishedPageActive = null;
    }

    public bool TryTakePending(out bool active)
    {
        active = _pageActive;
        if (!_frontendReady || _lastPublishedPageActive == active)
            return false;

        _lastPublishedPageActive = active;
        return true;
    }
}

internal sealed record DreamscapePageActivationMessage(
    [property: JsonPropertyName("active")] bool Active)
{
    public const string MessageType = "pageActivation";

    [JsonPropertyName("type")]
    public string Type => MessageType;

    public string ToJson() => JsonSerializer.Serialize(this);
}
