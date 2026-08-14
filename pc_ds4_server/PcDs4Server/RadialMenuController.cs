using System.Drawing;

namespace PcDs4Server;

public interface IRadialMenuOverlay : IDisposable
{
    bool IsVisible { get; }
    void ShowAt(Point screenPoint, RadialMenuSettings settings);
    void Hide();
}

public sealed class RadialMenuController : IDisposable
{
    private readonly IRadialMenuOverlay _overlay;
    private RadialMenuSettings _activeSettings;
    private Point? _lastAnchor;
    private bool _previewVisible;
    private bool _disposed;

    public RadialMenuController(IRadialMenuOverlay overlay, RadialMenuSettings settings)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out string error)) throw new ArgumentException(error, nameof(settings));
        _activeSettings = settings with { };
    }

    public bool IsOpen => !_disposed && _overlay.IsVisible;
    public RadialMenuSettings ActiveSettings => _activeSettings with { };

    public void ToggleAt(Point screenPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_overlay.IsVisible)
        {
            _overlay.Hide();
            _previewVisible = false;
        }
        else
        {
            _lastAnchor = screenPoint;
            _previewVisible = false;
            _overlay.ShowAt(screenPoint, _activeSettings);
        }
    }

    public void ApplySettings(RadialMenuSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out string error)) throw new ArgumentException(error, nameof(settings));

        _activeSettings = settings with { };
        _previewVisible = false;
        if (_overlay.IsVisible && _lastAnchor is Point anchor)
            _overlay.ShowAt(anchor, _activeSettings);
    }

    public void PreviewAt(Point screenPoint, RadialMenuSettings temporarySettings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(temporarySettings);
        if (!temporarySettings.TryValidate(out string error))
            throw new ArgumentException(error, nameof(temporarySettings));

        _lastAnchor = screenPoint;
        _previewVisible = true;
        _overlay.ShowAt(screenPoint, temporarySettings with { });
    }

    public void ClosePreview()
    {
        if (_disposed || !_previewVisible) return;
        if (_overlay.IsVisible) _overlay.Hide();
        _previewVisible = false;
    }

    public void Close()
    {
        if (_disposed || !_overlay.IsVisible) return;
        _overlay.Hide();
        _previewVisible = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_overlay.IsVisible) _overlay.Hide();
        _overlay.Dispose();
        _previewVisible = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
