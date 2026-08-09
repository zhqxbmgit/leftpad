namespace PcDs4Server;

public readonly record struct ScreenPoint(int X, int Y);

public enum JoystickResetReason
{
    MoveUp,
    Disconnect,
    SessionReplacement,
    ServiceStop,
    NormalExit,
    ControllerDispose,
    CursorPositionFailure,
    CursorSamplingFailure
}

public interface ICursorPositionProvider
{
    bool TryGetPosition(out ScreenPoint position, out int win32Error);
}

public interface IJoystickOverlay
{
    bool IsVisible { get; }
    void Show(ScreenPoint center);
    void UpdateKnob(double x, double y);
    void Hide();
}

public interface ILeftStickOutput
{
    void SetLeftStick(byte x, byte y);
}

public sealed record VirtualJoystickSnapshot(
    bool MoveButtonPressed,
    bool JoystickActive,
    ScreenPoint? Center,
    ScreenPoint? CurrentCursor,
    double CursorDeltaX,
    double CursorDeltaY,
    double CursorDistance,
    bool DirectionActive,
    double LogicalKnobX,
    double LogicalKnobY,
    double StickX,
    double StickY,
    byte Ds4X,
    byte Ds4Y,
    bool OverlayVisible,
    JoystickResetReason? LastResetReason);

public sealed class VirtualJoystickController
{
    public const double JoystickRadius = 20.0;
    public const double ActivationRadius = 4.0;
    public const byte NeutralAxis = 128;

    private readonly object _sync = new();
    private readonly ILeftStickOutput _stickOutput;
    private readonly IJoystickOverlay _overlay;
    private readonly ICursorPositionProvider _cursor;

    private bool _moveButtonPressed;
    private bool _joystickActive;
    private ScreenPoint? _center;
    private ScreenPoint? _currentCursor;
    private double _cursorDeltaX;
    private double _cursorDeltaY;
    private double _cursorDistance;
    private bool _directionActive;
    private double _logicalKnobX;
    private double _logicalKnobY;
    private double _stickX;
    private double _stickY;
    private byte _ds4X = NeutralAxis;
    private byte _ds4Y = NeutralAxis;
    private JoystickResetReason? _lastResetReason;

    public VirtualJoystickController(
        ILeftStickOutput stickOutput,
        IJoystickOverlay overlay,
        ICursorPositionProvider cursor)
    {
        _stickOutput = stickOutput;
        _overlay = overlay;
        _cursor = cursor;
    }

    public event Action<VirtualJoystickSnapshot>? StateChanged;
    public event Action<int>? CursorPositionReadFailed;

    public VirtualJoystickSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return CreateSnapshot();
            }
        }
    }

    public bool TryMoveDown(out int win32Error)
    {
        VirtualJoystickSnapshot? snapshot = null;
        bool activated;

        lock (_sync)
        {
            if (_moveButtonPressed)
            {
                win32Error = 0;
                return true;
            }

            if (!_cursor.TryGetPosition(out ScreenPoint cursor, out win32Error))
            {
                _moveButtonPressed = false;
                _joystickActive = false;
                _lastResetReason = JoystickResetReason.CursorPositionFailure;
                ClearPositionAndMotion();
                _stickOutput.SetLeftStick(NeutralAxis, NeutralAxis);
                _overlay.Hide();
                snapshot = CreateSnapshot();
                activated = false;
            }
            else
            {
                _moveButtonPressed = true;
                _joystickActive = true;
                _center = cursor;
                _currentCursor = cursor;
                _lastResetReason = null;
                ClearMotion();
                _directionActive = false;
                _stickOutput.SetLeftStick(NeutralAxis, NeutralAxis);
                _overlay.Show(cursor);
                snapshot = CreateSnapshot();
                activated = true;
            }
        }

        StateChanged?.Invoke(snapshot);
        return activated;
    }

    public void SampleCursor()
    {
        VirtualJoystickSnapshot? snapshot = null;
        int? readError = null;

        lock (_sync)
        {
            if (!_joystickActive)
            {
                return;
            }

            if (!_cursor.TryGetPosition(out ScreenPoint current, out int win32Error))
            {
                _moveButtonPressed = false;
                _joystickActive = false;
                _lastResetReason = JoystickResetReason.CursorPositionFailure;
                ClearPositionAndMotion();
                _stickOutput.SetLeftStick(NeutralAxis, NeutralAxis);
                _overlay.Hide();
                snapshot = CreateSnapshot();
                readError = win32Error;
            }
            else
            {
                ApplyCursorLocked(current);
                snapshot = CreateSnapshot();
            }
        }

        StateChanged?.Invoke(snapshot);
        if (readError.HasValue)
        {
            CursorPositionReadFailed?.Invoke(readError.Value);
        }
    }

    public void UpdateCursor(ScreenPoint current)
    {
        VirtualJoystickSnapshot snapshot;
        lock (_sync)
        {
            if (!_joystickActive)
            {
                return;
            }

            ApplyCursorLocked(current);
            snapshot = CreateSnapshot();
        }

        StateChanged?.Invoke(snapshot);
    }

    public void Reset(JoystickResetReason reason)
    {
        VirtualJoystickSnapshot snapshot;
        lock (_sync)
        {
            _moveButtonPressed = false;
            _joystickActive = false;
            _lastResetReason = reason;
            ClearPositionAndMotion();
            _stickOutput.SetLeftStick(NeutralAxis, NeutralAxis);
            _overlay.Hide();
            snapshot = CreateSnapshot();
        }

        StateChanged?.Invoke(snapshot);
    }

    public static byte MapAxis(double normalized)
    {
        normalized = Math.Clamp(normalized, -1.0, 1.0);
        double value = normalized >= 0
            ? NeutralAxis + (normalized * 127.0)
            : NeutralAxis + (normalized * 128.0);
        return (byte)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private void ApplyCursorLocked(ScreenPoint current)
    {
        if (!_center.HasValue)
        {
            return;
        }

        _currentCursor = current;
        _cursorDeltaX = (double)current.X - _center.Value.X;
        _cursorDeltaY = (double)current.Y - _center.Value.Y;
        _cursorDistance = Math.Sqrt(
            (_cursorDeltaX * _cursorDeltaX) +
            (_cursorDeltaY * _cursorDeltaY));
        _directionActive = _cursorDistance > ActivationRadius;

        if (!_directionActive)
        {
            _logicalKnobX = 0;
            _logicalKnobY = 0;
            _stickX = 0;
            _stickY = 0;
        }
        else
        {
            double inverseDistance = 1.0 / _cursorDistance;
            _stickX = _cursorDeltaX * inverseDistance;
            _stickY = _cursorDeltaY * inverseDistance;
            _logicalKnobX = _stickX * JoystickRadius;
            _logicalKnobY = _stickY * JoystickRadius;
        }

        _ds4X = MapAxis(_stickX);
        _ds4Y = MapAxis(_stickY);
        _stickOutput.SetLeftStick(_ds4X, _ds4Y);
        _overlay.UpdateKnob(_logicalKnobX, _logicalKnobY);
    }

    private void ClearPositionAndMotion()
    {
        _center = null;
        _currentCursor = null;
        ClearMotion();
        _directionActive = false;
    }

    private void ClearMotion()
    {
        _cursorDeltaX = 0;
        _cursorDeltaY = 0;
        _cursorDistance = 0;
        _logicalKnobX = 0;
        _logicalKnobY = 0;
        _stickX = 0;
        _stickY = 0;
        _ds4X = NeutralAxis;
        _ds4Y = NeutralAxis;
    }

    private VirtualJoystickSnapshot CreateSnapshot()
    {
        return new VirtualJoystickSnapshot(
            _moveButtonPressed,
            _joystickActive,
            _center,
            _currentCursor,
            _cursorDeltaX,
            _cursorDeltaY,
            _cursorDistance,
            _directionActive,
            _logicalKnobX,
            _logicalKnobY,
            _stickX,
            _stickY,
            _ds4X,
            _ds4Y,
            _overlay.IsVisible,
            _lastResetReason);
    }
}
