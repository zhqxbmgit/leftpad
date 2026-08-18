using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server;

public sealed class Ds4Service : ILeftStickOutput, IServerLifecycle, IDisposable
{
    public const int RadialDs4TapDurationMs = 40;

    private readonly IDirectDs4Factory _directDs4Factory;
    private readonly KeyboardKeyState _keyboardState;
    private readonly KeyboardMoveOutput _keyboardMoveOutput;
    private readonly RadialKeyboardActionExecutor _radialKeyboardActionExecutor;
    private readonly Action<int> _radialDs4Delay;
    private readonly IKeyboardBindingStore _bindingStore;
    private readonly Ds4ControlState _controlState = new();
    private readonly Stopwatch _inputClock = Stopwatch.StartNew();
    private readonly Func<TimeSpan>? _inputTimestampProvider;
    private readonly ActionDoubleTapRecognizer _actionDoubleTapRecognizer;
    private readonly MoveTapRecognizer _moveTapRecognizer;
    private readonly object _lock = new();
    private readonly object _outputLock = new();
    private IDirectDs4Session? _directDs4;
    private TcpListener? _server;
    private CancellationTokenSource? _cts;
    private TcpClient? _activeClient;
    private long _activeSessionId;
    private VirtualJoystickController? _joystick;
    private CursorJoystickSampler? _joystickSampler;
    private OutputMode _outputMode = OutputMode.DirectDs4;
    private KeyboardBindings _keyboardBindings;
    private int _radialDoubleTapWindowMs;
    private RadialTriggerSource? _radialOpenSource;
    private RadialTriggerSource? _radialSuppressedUpSource;
    private bool _disposed;

    public Ds4Service(
        IDirectDs4Factory? directDs4Factory = null,
        IKeyboardOutput? keyboardOutput = null,
        IKeyboardBindingStore? bindingStore = null,
        TimeSpan? doubleTapWindow = null,
        Func<TimeSpan>? inputTimestampProvider = null,
        Action<int>? radialDs4Delay = null)
    {
        _directDs4Factory = directDs4Factory ?? new VigemDirectDs4Factory();
        _keyboardState = new KeyboardKeyState(keyboardOutput ?? new SendInputKeyboardOutput());
        _keyboardMoveOutput = new KeyboardMoveOutput(_keyboardState);
        _radialKeyboardActionExecutor = new RadialKeyboardActionExecutor(_keyboardState);
        _radialDs4Delay = radialDs4Delay ?? Thread.Sleep;
        _bindingStore = bindingStore ?? new JsonKeyboardBindingStore();
        _keyboardBindings = _bindingStore.Load();
        TimeSpan initialDoubleTapWindow = doubleTapWindow ??
            TimeSpan.FromMilliseconds(RadialMenuSettings.Default.DoubleTapWindowMs);
        int initialDoubleTapWindowMs = checked((int)initialDoubleTapWindow.TotalMilliseconds);
        ValidateRadialDoubleTapWindow(initialDoubleTapWindowMs);
        _radialDoubleTapWindowMs = initialDoubleTapWindowMs;
        _inputTimestampProvider = inputTimestampProvider;
        _actionDoubleTapRecognizer = new ActionDoubleTapRecognizer(CurrentRadialDoubleTapWindow);
        _moveTapRecognizer = new MoveTapRecognizer(CurrentRadialDoubleTapWindow);
        LocalIp = GetLocalIp();
    }

    public event Action<string>? OnLog;
    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnConnectionChanged;
    public event Action<string>? OnButtonEvent;
    public event Action<VirtualJoystickSnapshot>? OnJoystickStateChanged;
    public event Action<RadialTriggerSource>? RadialMenuTriggered;
    public event Action<RadialTriggerSource>? RadialMenuConfirmationRequested;
    public event Action? OnStopped;

    public bool IsRunning { get; private set; }
    public OutputMode OutputMode => _outputMode;
    public KeyboardBindings KeyboardBindings => _keyboardBindings.Clone();
    public string LocalIp { get; private set; }
    public int Port { get; } = 8888;
    public int RadialDoubleTapWindowMs => Volatile.Read(ref _radialDoubleTapWindowMs);

    public bool TryExecuteRadialKeyboardAction(RadialSlotMapping mapping, out string error) =>
        _radialKeyboardActionExecutor.TryExecute(mapping, out error);

    public bool TryExecuteRadialDs4Action(RadialSlotMapping mapping, out string error)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.Kind != RadialActionKind.Ds4Button ||
            !RadialDs4ActionCatalog.TryGet(mapping.Ds4Button, out RadialDs4ActionMapping action))
        {
            error = "该 Slot 没有有效的 Radial DS4 动作。";
            return false;
        }

        if (_outputMode != OutputMode.DirectDs4)
        {
            error = "当前输出模式不是 Direct DS4。";
            return false;
        }

        Exception? outputFailure = null;
        lock (_outputLock)
        {
            if (_directDs4 == null)
            {
                error = "虚拟 DS4 当前不可用。";
                return false;
            }

            if (action.Kind == RadialDs4ActionKind.DigitalButton &&
                _controlState.ActiveButtons.Contains(action.DigitalButton!))
            {
                error = "目标 DS4 按键当前已按下，为避免干扰现有输入，本次未执行。";
                return false;
            }

            if (action.Kind == RadialDs4ActionKind.DPad &&
                !Equals(_controlState.DPadDirection, DualShock4DPadDirection.None))
            {
                error = "目标 DS4 十字键当前非空闲，为避免干扰现有输入，本次未执行。";
                return false;
            }

            try
            {
                if (action.Kind == RadialDs4ActionKind.DigitalButton)
                    PulseRadialDigitalButton(action.DigitalButton!);
                else
                    PulseRadialDPad(action.DPadDirection!);
            }
            catch (Exception ex)
            {
                MarkRadialTargetForFailureCleanup(action);
                outputFailure = ex;
            }
        }

        if (outputFailure == null)
        {
            error = string.Empty;
            return true;
        }

        try
        {
            ReleaseAllControls(Ds4ControlResetReason.OutputFailure);
        }
        catch
        {
            // Best-effort cleanup must not let an output failure escape into the UI.
        }

        error = $"DS4 输出失败：{outputFailure.Message}";
        return false;
    }

    private void PulseRadialDigitalButton(DualShock4Button button)
    {
        _directDs4!.SetButton(button, true);
        _controlState.SetDigitalButton(button, true);
        _directDs4.SubmitReport();
        _radialDs4Delay(RadialDs4TapDurationMs);
        _directDs4.SetButton(button, false);
        _controlState.SetDigitalButton(button, false);
        _directDs4.SubmitReport();
    }

    private void PulseRadialDPad(DualShock4DPadDirection direction)
    {
        _directDs4!.SetDPadDirection(direction);
        _controlState.SetDPadDirection(direction);
        _directDs4.SubmitReport();
        _radialDs4Delay(RadialDs4TapDurationMs);
        _directDs4.SetDPadDirection(DualShock4DPadDirection.None);
        _controlState.SetDPadDirection(DualShock4DPadDirection.None);
        _directDs4.SubmitReport();
    }

    private void MarkRadialTargetForFailureCleanup(RadialDs4ActionMapping action)
    {
        if (action.Kind == RadialDs4ActionKind.DigitalButton)
            _controlState.SetDigitalButton(action.DigitalButton!, true);
        else
            _controlState.SetDPadDirection(action.DPadDirection!);
    }

    public void NotifyRadialMenuClosed()
    {
        lock (_lock) _radialOpenSource = null;
    }

    public void SetRadialDoubleTapWindow(int milliseconds)
    {
        ThrowIfDisposed();
        ValidateRadialDoubleTapWindow(milliseconds);
        Volatile.Write(ref _radialDoubleTapWindowMs, milliseconds);
    }

    public bool TrySetOutputMode(OutputMode mode)
    {
        lock (_lock)
        {
            if (IsRunning || _directDs4 != null) return false;
            _outputMode = mode;
            return true;
        }
    }

    public bool TryUpdateKeyboardBindings(KeyboardBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        lock (_lock)
        {
            if (IsRunning) return false;
            _keyboardBindings = bindings.Clone();
            _bindingStore.Save(_keyboardBindings);
            return true;
        }
    }

    public void ConfigureVirtualJoystick(IJoystickOverlay overlay, ICursorPositionProvider cursor)
    {
        if (_joystick != null) throw new InvalidOperationException("The virtual joystick has already been configured.");
        _joystick = new VirtualJoystickController(this, overlay, cursor);
        _joystick.StateChanged += snapshot => OnJoystickStateChanged?.Invoke(snapshot);
        _joystick.CursorPositionReadFailed += error =>
        {
            lock (_lock) ResetRadialRecognizers();
            Log($"光标采样已停止：GetCursorPos 失败，Win32 错误码 {error}。");
        };
        OnJoystickStateChanged?.Invoke(_joystick.Snapshot);
    }

    public void ResetVirtualJoystick(JoystickResetReason reason)
    {
        lock (_lock)
        {
            ResetRadialRecognizers();
            _joystick?.Reset(reason);
        }
    }

    public bool Initialize()
    {
        ThrowIfDisposed();
        if (_outputMode == OutputMode.Keyboard)
        {
            Log("键盘输出已就绪；ViGEm 和虚拟控制器已禁用。");
            OnStatusChanged?.Invoke("键盘输出：就绪 / 虚拟控制器：已禁用");
            return true;
        }

        try
        {
            _directDs4 = _directDs4Factory.Create();
            Log("虚拟 DualShock 4 已创建并连接。");
            OnStatusChanged?.Invoke("ViGEmBus：已连接 / 虚拟 DS4：就绪");
            return true;
        }
        catch (Exception ex)
        {
            Log($"ViGEm 初始化失败：{ex.Message}");
            OnStatusChanged?.Invoke("ViGEmBus：不可用或初始化失败");
            return false;
        }
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (IsRunning) return;
        if (_outputMode == OutputMode.DirectDs4 && _directDs4 == null)
            throw new InvalidOperationException("Direct DS4 output must be initialized before starting.");

        ResetRadialRecognizers();
        _server = new TcpListener(IPAddress.Any, Port);
        _server.Start();
        IsRunning = true;
        _cts = new CancellationTokenSource();
        if (_joystick != null)
        {
            _joystickSampler = new CursorJoystickSampler(_joystick);
            _joystickSampler.SamplingFailed += ex =>
            {
                ReleaseAllControls(Ds4ControlResetReason.OutputFailure);
                Log($"光标采样因意外错误而停止：{ex.Message}");
            };
        }

        Log($"TCP 服务器正在端口 {Port} 监听，输出模式：{_outputMode}。");
        Log($"[环形菜单] 双击窗口：{RadialDoubleTapWindowMs} ms");
        OnConnectionChanged?.Invoke("等待手机连接...");
        _ = Task.Run(() => AcceptClientsAsync(_cts.Token));
    }

    private async Task AcceptClientsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _server!.AcceptTcpClientAsync(token);
                long sessionId;
                TcpClient? previousClient;
                lock (_lock)
                {
                    previousClient = _activeClient;
                    _activeClient = client;
                    sessionId = ++_activeSessionId;
                }
                ResetVirtualJoystick(JoystickResetReason.SessionReplacement);
                ReleaseAllControls(Ds4ControlResetReason.SessionReplacement);
                previousClient?.Dispose();
                _ = HandleClientAsync(client, sessionId, token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                ReleaseAllControls(Ds4ControlResetReason.OutputFailure);
                Log($"接受客户端连接失败：{ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, long sessionId, CancellationToken token)
    {
        string remote = client.Client.RemoteEndPoint?.ToString() ?? "未知地址";
        OnConnectionChanged?.Invoke($"已连接：{remote}");
        try
        {
            using (client)
            using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
            {
                while (!token.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(token);
                    if (line == null) break;
                    ProcessMessage(line, sessionId);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Log($"连接错误（{remote}）：{ex.Message}"); }
        finally
        {
            bool active;
            lock (_lock)
            {
                active = sessionId == _activeSessionId;
                if (active) _activeClient = null;
            }
            if (active)
            {
                OnConnectionChanged?.Invoke("手机已断开，等待重新连接...");
                ResetVirtualJoystick(JoystickResetReason.Disconnect);
                ReleaseAllControls(Ds4ControlResetReason.Disconnect);
            }
        }
    }

    private void ProcessMessage(string rawData, long sessionId)
    {
        try
        {
            ButtonMessage? message = JsonSerializer.Deserialize<ButtonMessage>(rawData.Trim());
            if (message == null) return;
            lock (_lock)
            {
                if (sessionId != _activeSessionId) return;
                if (!string.IsNullOrEmpty(message.command)) HandleCommand(message.command);
                else ProcessProtocolAction(message.button, message.action);
            }
        }
        catch (JsonException) { }
        catch (Exception ex)
        {
            ReleaseAllControls(Ds4ControlResetReason.OutputFailure);
            Log($"输入处理失败：{ex.Message}");
        }
    }

    public void ProcessProtocolAction(string protocolAction, string action)
    {
        if (protocolAction.Equals("move", StringComparison.OrdinalIgnoreCase))
        {
            HandleMoveMessage(action);
            OnButtonEvent?.Invoke($"{protocolAction} -> {action}");
            return;
        }

        if (!Ds4ActionMapper.TryGetPressedState(action, out bool pressed)) return;
        bool isGameAction = Ds4ActionMapper.TryGet(protocolAction, out Ds4ActionMapping mapping);
        if (isGameAction)
        {
            RadialTriggerSource source = RadialTriggerSource.ForAction(mapping.ProtocolKey);
            if (TryConsumeRadialMenuInput(source, pressed, out bool requestConfirmation))
            {
                if (requestConfirmation) RadialMenuConfirmationRequested?.Invoke(source);
                OnButtonEvent?.Invoke($"{protocolAction} -> {action}");
                return;
            }

            if (!pressed && IsRadialMenuOpenFrom(source))
            {
                ActionDoubleTapResult triggerUp = _actionDoubleTapRecognizer.Process(
                    mapping.ProtocolKey,
                    isPressed: false,
                    CurrentInputTimestamp);
                if (triggerUp.ConsumeCurrentEvent)
                {
                    OnButtonEvent?.Invoke($"{protocolAction} -> {action}");
                    return;
                }
            }

            if (HasOpenRadialMenu())
            {
                RouteGameAction(protocolAction, pressed, mapping);
                OnButtonEvent?.Invoke($"{protocolAction} -> {action}");
                return;
            }

            ActionDoubleTapResult recognition = _actionDoubleTapRecognizer.Process(
                mapping.ProtocolKey,
                pressed,
                CurrentInputTimestamp);
            if (recognition.TriggerAccepted)
            {
                Log($"[环形菜单] Action 双击触发：{mapping.ProtocolKey}");
                OpenRadialMenu(source);
            }
            if (recognition.ConsumeCurrentEvent)
            {
                OnButtonEvent?.Invoke($"{protocolAction} -> {action}");
                return;
            }
        }

        if (isGameAction) RouteGameAction(protocolAction, pressed, mapping);
        else if (_outputMode == OutputMode.Keyboard) HandleKeyboardAction(protocolAction, pressed);
        OnButtonEvent?.Invoke($"{protocolAction} -> {action}");
    }

    private void RouteGameAction(string protocolAction, bool pressed, Ds4ActionMapping mapping)
    {
        if (_outputMode == OutputMode.Keyboard)
        {
            HandleKeyboardAction(protocolAction, pressed);
            return;
        }

        lock (_outputLock)
        {
            if (_directDs4 == null) return;
            if (mapping.Kind == Ds4ActionKind.DigitalButton)
            {
                _directDs4.SetButton(mapping.DigitalButton!, pressed);
                _controlState.SetDigitalButton(mapping.DigitalButton!, pressed);
            }
            else
            {
                byte value = Ds4ActionMapper.GetTriggerValue(pressed);
                _directDs4.SetTrigger(mapping.Trigger!, value);
                _controlState.SetTrigger(mapping.Trigger!, value);
            }
            _directDs4.SubmitReport();
        }
    }

    private void HandleKeyboardAction(string protocolAction, bool pressed)
    {
        if (!KeyboardBindings.ProtocolActions.Contains(protocolAction, StringComparer.OrdinalIgnoreCase)) return;
        try
        {
            string source = "action:" + protocolAction;
            if (pressed)
            {
                KeyboardKey key = _keyboardBindings.Get(protocolAction);
                if (key != KeyboardKey.None) _keyboardState.Press(source, key);
            }
            else _keyboardState.Release(source);
        }
        catch (KeyboardOutputException ex)
        {
            Log($"键盘输出失败：{ex.InnerException?.Message ?? ex.Message}");
        }
    }

    private void HandleMoveMessage(string action)
    {
        if (_joystick == null) { Log("虚拟摇杆尚未配置，无法处理 MOVE。"); return; }
        try
        {
            bool? pressed = action.Equals("down", StringComparison.OrdinalIgnoreCase)
                ? true
                : action.Equals("up", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : null;
            if (pressed is bool movePressed &&
                TryConsumeRadialMenuInput(RadialTriggerSource.Move, movePressed, out bool requestConfirmation))
            {
                if (requestConfirmation)
                    RadialMenuConfirmationRequested?.Invoke(RadialTriggerSource.Move);
                return;
            }

            bool radialMenuAlreadyOpen = HasOpenRadialMenu();

            if (action.Equals("down", StringComparison.OrdinalIgnoreCase))
            {
                bool wasPressed = _joystick.Snapshot.MoveButtonPressed;
                if (!_joystick.TryMoveDown(out int error))
                {
                    ResetRadialRecognizers();
                    Log($"MOVE 激活失败，Win32 错误码 {error}。");
                }
                else if (!radialMenuAlreadyOpen && !wasPressed && _joystick.Snapshot.MoveButtonPressed)
                {
                    _moveTapRecognizer.MoveDown(CurrentInputTimestamp);
                }
            }
            else if (action.Equals("up", StringComparison.OrdinalIgnoreCase))
            {
                bool directionCapturedDuringHold = _joystick.Snapshot.DirectionCapturedDuringHold;
                _joystick.ReleaseMove();
                if (radialMenuAlreadyOpen)
                {
                    _moveTapRecognizer.Reset();
                }
                else
                {
                    MoveTapResult recognition = _moveTapRecognizer.MoveUp(
                        CurrentInputTimestamp,
                        directionCapturedDuringHold);
                    if (recognition.TriggerAccepted)
                    {
                        Log("[环形菜单] MOVE 双击触发");
                        OpenRadialMenu(RadialTriggerSource.Move);
                    }
                }
            }
            else if (action.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                ClearRadialUpSuppression(RadialTriggerSource.Move);
                _joystick.StopMovement();
                _moveTapRecognizer.Reset();
            }
        }
        catch (Exception ex)
        {
            _joystick.Reset(JoystickResetReason.CursorPositionFailure);
            ReleaseAllControls(Ds4ControlResetReason.OutputFailure);
            Log($"MOVE 处理失败：{ex.Message}");
        }
    }

    public void ReleaseAllControls(Ds4ControlResetReason reason)
    {
        lock (_lock)
        {
            ResetRadialRecognizers();
            _keyboardState.ReleaseAll();
            lock (_outputLock)
            {
                Ds4ControlRelease release = _controlState.ReleaseAll(reason);
                if (_directDs4 == null) return;
                foreach (DualShock4Button button in release.DigitalButtons) _directDs4.SetButton(button, false);
                if (release.ResetDPad) _directDs4.SetDPadDirection(DualShock4DPadDirection.None);
                if (release.ResetLeftTrigger) _directDs4.SetTrigger(DualShock4Slider.LeftTrigger, 0);
                if (release.ResetRightTrigger) _directDs4.SetTrigger(DualShock4Slider.RightTrigger, 0);
                if (release.DigitalButtons.Count > 0 || release.ResetDPad ||
                    release.ResetLeftTrigger || release.ResetRightTrigger)
                    _directDs4.SubmitReport();
            }
        }
    }

    void ILeftStickOutput.SetLeftStick(byte x, byte y)
    {
        if (_outputMode == OutputMode.Keyboard)
        {
            try { _keyboardMoveOutput.SetLeftStick(x, y); }
            catch (KeyboardOutputException ex) { Log($"键盘 MOVE 输出失败：{ex.InnerException?.Message ?? ex.Message}"); }
            return;
        }
        lock (_outputLock)
        {
            if (_directDs4 == null) return;
            _directDs4.SetLeftStick(x, y);
            _directDs4.SubmitReport();
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _server?.Stop();
        lock (_lock)
        {
            _activeClient?.Dispose();
            _activeClient = null;
            _activeSessionId++;
        }
        _joystickSampler?.Dispose();
        _joystickSampler = null;
        ResetVirtualJoystick(JoystickResetReason.ServiceStop);
        ReleaseAllControls(Ds4ControlResetReason.ServiceStop);
        ResetVirtualJoystick(JoystickResetReason.ControllerDispose);
        lock (_outputLock)
        {
            _directDs4?.Dispose();
            _directDs4 = null;
        }
        _cts?.Dispose();
        _cts = null;
        _server = null;
        IsRunning = false;
        OnStopped?.Invoke();
        Log("服务已停止，所有输出状态均已释放。");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _keyboardState.ReleaseAll();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void HandleCommand(string command)
    {
        if (!command.Equals("task_manager", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
        catch (Exception ex) { Log($"无法打开任务管理器：{ex.Message}"); }
    }

    private void Log(string message) => OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");

    private TimeSpan CurrentInputTimestamp => _inputTimestampProvider?.Invoke() ?? _inputClock.Elapsed;

    private TimeSpan CurrentRadialDoubleTapWindow() =>
        TimeSpan.FromMilliseconds(RadialDoubleTapWindowMs);

    private bool HasOpenRadialMenu()
    {
        lock (_lock) return _radialOpenSource.HasValue;
    }

    private bool IsRadialMenuOpenFrom(RadialTriggerSource source)
    {
        lock (_lock) return _radialOpenSource == source;
    }

    private void OpenRadialMenu(RadialTriggerSource source)
    {
        lock (_lock) _radialOpenSource = source;
        RadialMenuTriggered?.Invoke(source);
    }

    private bool TryConsumeRadialMenuInput(
        RadialTriggerSource source,
        bool pressed,
        out bool requestConfirmation)
    {
        lock (_lock)
        {
            requestConfirmation = false;
            if (_radialSuppressedUpSource == source)
            {
                if (!pressed) _radialSuppressedUpSource = null;
                return true;
            }

            if (!pressed || _radialOpenSource != source) return false;

            _radialOpenSource = null;
            _radialSuppressedUpSource = source;
            _actionDoubleTapRecognizer.Reset();
            _moveTapRecognizer.Reset();
            requestConfirmation = true;
            return true;
        }
    }

    private void ClearRadialUpSuppression(RadialTriggerSource source)
    {
        lock (_lock)
        {
            if (_radialSuppressedUpSource == source) _radialSuppressedUpSource = null;
        }
    }

    private static void ValidateRadialDoubleTapWindow(int milliseconds)
    {
        if (milliseconds is < RadialMenuSettings.MinimumDoubleTapWindowMs or > RadialMenuSettings.MaximumDoubleTapWindowMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(milliseconds),
                $"Radial double-tap window must be between {RadialMenuSettings.MinimumDoubleTapWindowMs} and {RadialMenuSettings.MaximumDoubleTapWindowMs} ms.");
        }
    }

    private void ResetRadialRecognizers()
    {
        _actionDoubleTapRecognizer.Reset();
        _moveTapRecognizer.Reset();
        _radialOpenSource = null;
        _radialSuppressedUpSource = null;
    }

    private static string GetLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public sealed class ButtonMessage
    {
        public string button { get; set; } = "";
        public string action { get; set; } = "";
        public string command { get; set; } = "";
    }
}
