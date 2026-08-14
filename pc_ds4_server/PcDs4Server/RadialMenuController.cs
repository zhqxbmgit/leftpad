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
    private Point? _normalAnchor;
    private Point? _previewAnchor;
    private bool _isPreviewActive;
    private bool _disposed;

    public RadialMenuController(IRadialMenuOverlay overlay, RadialMenuSettings settings)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out string error)) throw new ArgumentException(error, nameof(settings));
        _activeSettings = settings with { };
    }

    public bool IsOpen => !_disposed && _overlay.IsVisible;
    public bool IsPreviewActive => !_disposed && _isPreviewActive;
    public Point? PreviewAnchor => IsPreviewActive ? _previewAnchor : null;
    public RadialMenuSettings ActiveSettings => _activeSettings with { };

    public void ToggleAt(Point screenPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_overlay.IsVisible)
        {
            _overlay.Hide();
            EndPreviewSession();
        }
        else
        {
            _normalAnchor = screenPoint;
            EndPreviewSession();
            _overlay.ShowAt(screenPoint, _activeSettings);
        }
    }

    public void ApplySettings(RadialMenuSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out string error)) throw new ArgumentException(error, nameof(settings));

        _activeSettings = settings with { };
        if (_isPreviewActive && _previewAnchor is Point previewAnchor)
            _overlay.ShowAt(previewAnchor, _activeSettings);
        else if (_overlay.IsVisible && _normalAnchor is Point normalAnchor)
            _overlay.ShowAt(normalAnchor, _activeSettings);
    }

    public void PreviewAt(Point screenPoint, RadialMenuSettings temporarySettings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(temporarySettings);
        if (!temporarySettings.TryValidate(out string error))
            throw new ArgumentException(error, nameof(temporarySettings));

        _previewAnchor = screenPoint;
        _isPreviewActive = true;
        _overlay.ShowAt(screenPoint, temporarySettings with { });
    }

    public void UpdatePreview(RadialMenuSettings temporarySettings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(temporarySettings);
        if (!temporarySettings.TryValidate(out string error))
            throw new ArgumentException(error, nameof(temporarySettings));
        if (!_isPreviewActive || _previewAnchor is not Point anchor) return;

        _overlay.ShowAt(anchor, temporarySettings with { });
    }

    public void ClosePreview()
    {
        if (_disposed || !_isPreviewActive) return;
        if (_overlay.IsVisible) _overlay.Hide();
        EndPreviewSession();
    }

    public void Close()
    {
        if (_disposed || !_overlay.IsVisible) return;
        _overlay.Hide();
        EndPreviewSession();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_overlay.IsVisible) _overlay.Hide();
        _overlay.Dispose();
        EndPreviewSession();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void EndPreviewSession()
    {
        _isPreviewActive = false;
        _previewAnchor = null;
    }
}
