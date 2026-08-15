using System.Drawing;

namespace PcDs4Server;

public interface IRadialMenuOverlay : IDisposable
{
    bool IsVisible { get; }
    void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot);
    void Hide();
}

public sealed class RadialMenuController : IDisposable
{
    private readonly IRadialMenuOverlay _overlay;
    private RadialMenuSettings _activeSettings;
    private Point? _normalAnchor;
    private RadialTriggerSource? _openSource;
    private Point? _previewAnchor;
    private int _selectedSlot;
    private bool _isNormalMenuOpen;
    private bool _isPreviewActive;
    private bool _disposed;

    public RadialMenuController(IRadialMenuOverlay overlay, RadialMenuSettings settings)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out string error)) throw new ArgumentException(error, nameof(settings));
        _activeSettings = settings with { };
    }

    public event Action? NormalMenuStateChanged;

    public bool IsOpen => !_disposed && (_isNormalMenuOpen || _isPreviewActive);
    public bool IsNormalMenuOpen => !_disposed && _isNormalMenuOpen;
    public Point? NormalAnchor => IsNormalMenuOpen ? _normalAnchor : null;
    public RadialTriggerSource? OpenSource => IsNormalMenuOpen ? _openSource : null;
    public int SelectedSlot => IsNormalMenuOpen ? _selectedSlot : 0;
    public bool IsPreviewActive => !_disposed && _isPreviewActive;
    public Point? PreviewAnchor => IsPreviewActive ? _previewAnchor : null;
    public RadialMenuSettings ActiveSettings => _activeSettings with { };

    public void OpenAt(Point screenPoint, RadialTriggerSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isPreviewActive)
        {
            ClosePreview();
            return;
        }

        if (_isNormalMenuOpen)
            return;

        _normalAnchor = screenPoint;
        _openSource = source;
        _selectedSlot = 0;
        _isNormalMenuOpen = true;
        _overlay.ShowAt(screenPoint, _activeSettings, selectedSlot: 0);
        NormalMenuStateChanged?.Invoke();
    }

    public bool TryCompleteFrom(RadialTriggerSource source, out RadialMenuCompletion completion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        completion = default;
        if (_isPreviewActive || !_isNormalMenuOpen || _openSource != source) return false;

        completion = new RadialMenuCompletion(_selectedSlot);
        CloseNormalMenu();
        return true;
    }

    public void ApplySettings(RadialMenuSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out string error)) throw new ArgumentException(error, nameof(settings));

        _activeSettings = settings with { };
        if (_isPreviewActive && _previewAnchor is Point previewAnchor)
            _overlay.ShowAt(previewAnchor, _activeSettings, selectedSlot: 0);
        else if (_isNormalMenuOpen && _normalAnchor is Point normalAnchor)
            _overlay.ShowAt(normalAnchor, _activeSettings, _selectedSlot);
    }

    public void PreviewAt(Point screenPoint, RadialMenuSettings temporarySettings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(temporarySettings);
        if (!temporarySettings.TryValidate(out string error))
            throw new ArgumentException(error, nameof(temporarySettings));

        bool normalMenuWasOpen = _isNormalMenuOpen;
        EndNormalMenuSession();
        _previewAnchor = screenPoint;
        _isPreviewActive = true;
        _overlay.ShowAt(screenPoint, temporarySettings with { }, selectedSlot: 0);
        if (normalMenuWasOpen) NormalMenuStateChanged?.Invoke();
    }

    public void UpdatePreview(RadialMenuSettings temporarySettings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(temporarySettings);
        if (!temporarySettings.TryValidate(out string error))
            throw new ArgumentException(error, nameof(temporarySettings));
        if (!_isPreviewActive || _previewAnchor is not Point anchor) return;

        _overlay.ShowAt(anchor, temporarySettings with { }, selectedSlot: 0);
    }

    public bool UpdateSelectionForCursor(Point cursor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isNormalMenuOpen || _isPreviewActive || _normalAnchor is not Point anchor) return false;

        int selectedSlot = RadialSelectionEngine.GetSelectedSlot(
            anchor,
            cursor,
            _activeSettings.SelectionDeadZone);
        if (selectedSlot == _selectedSlot) return false;

        _selectedSlot = selectedSlot;
        _overlay.ShowAt(anchor, _activeSettings, _selectedSlot);
        return true;
    }

    public void ClosePreview()
    {
        if (_disposed || !_isPreviewActive) return;
        if (_overlay.IsVisible) _overlay.Hide();
        EndPreviewSession();
    }

    public void Close()
    {
        if (_disposed) return;
        bool normalMenuWasOpen = _isNormalMenuOpen;
        if (_overlay.IsVisible) _overlay.Hide();
        EndNormalMenuSession();
        EndPreviewSession();
        if (normalMenuWasOpen) NormalMenuStateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        bool normalMenuWasOpen = _isNormalMenuOpen;
        if (_overlay.IsVisible) _overlay.Hide();
        _overlay.Dispose();
        EndNormalMenuSession();
        EndPreviewSession();
        _disposed = true;
        if (normalMenuWasOpen) NormalMenuStateChanged?.Invoke();
        GC.SuppressFinalize(this);
    }

    private void CloseNormalMenu()
    {
        if (!_isNormalMenuOpen) return;
        if (_overlay.IsVisible) _overlay.Hide();
        EndNormalMenuSession();
        NormalMenuStateChanged?.Invoke();
    }

    private void EndNormalMenuSession()
    {
        _isNormalMenuOpen = false;
        _normalAnchor = null;
        _openSource = null;
        _selectedSlot = 0;
    }

    private void EndPreviewSession()
    {
        _isPreviewActive = false;
        _previewAnchor = null;
    }
}
