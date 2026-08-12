using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server;

public sealed class Ds4Service : ILeftStickOutput, IDisposable
{
    private readonly IDirectDs4Factory _directDs4Factory;
    private readonly KeyboardKeyState _keyboardState;
    private readonly KeyboardMoveOutput _keyboardMoveOutput;
    private readonly IKeyboardBindingStore _bindingStore;
    private readonly Ds4ControlState _controlState = new();
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
    private bool _disposed;

    public Ds4Service(
        IDirectDs4Factory? directDs4Factory = null,
        IKeyboardOutput? keyboardOutput = null,
        IKeyboardBindingStore? bindingStore = null)
    {
        _directDs4Factory = directDs4Factory ?? new VigemDirectDs4Factory();
        _keyboardState = new KeyboardKeyState(keyboardOutput ?? new SendInputKeyboardOutput());
        _keyboardMoveOutput = new KeyboardMoveOutput(_keyboardState);
        _bindingStore = bindingStore ?? new JsonKeyboardBindingStore();
        _keyboardBindings = _bindingStore.Load();
        LocalIp = GetLocalIp();
    }

    public event Action<string>? OnLog;
    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnConnectionChanged;
    public event Action<string>? OnButtonEvent;
    public event Action<VirtualJoystickSnapshot>? OnJoystickStateChanged;

    public bool IsRunning { get; private set; }
    public OutputMode OutputMode => _outputMode;
    public KeyboardBindings KeyboardBindings => _keyboardBindings.Clone();
    public string LocalIp { get; private set; }
    public int Port { get; } = 8888;

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
            Log($"Cursor sampling stopped: GetCursorPos failed with Win32 error {error}.");
        OnJoystickStateChanged?.Invoke(_joystick.Snapshot);
    }

    public void ResetVirtualJoystick(JoystickResetReason reason) => _joystick?.Reset(reason);

    public bool Initialize()
    {
        ThrowIfDisposed();
        if (_outputMode == OutputMode.Keyboard)
        {
            Log("Keyboard output ready; ViGEm and virtual controllers are disabled.");
            OnStatusChanged?.Invoke("Keyboard output: Ready / Virtual controller: Disabled");
            return true;
        }

        try
        {
            _directDs4 = _directDs4Factory.Create();
            Log("Virtual DualShock 4 created and connected.");
            OnStatusChanged?.Invoke("ViGEmBus: Connected / Virtual DS4: Ready");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Unable to initialize ViGEm: {ex.Message}");
            OnStatusChanged?.Invoke("ViGEmBus: unavailable or initialization failed");
            return false;
        }
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (IsRunning) return;
        if (_outputMode == OutputMode.DirectDs4 && _directDs4 == null)
            throw new InvalidOperationException("Direct DS4 output must be initialized before starting.");

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
                Log($"Cursor sampling stopped after an unexpected error: {ex.Message}");
            };
        }

        Log($"TCP server listening on port {Port} in {_outputMode} mode.");
        OnConnectionChanged?.Invoke("Waiting for phone connection...");
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
                Log($"Client accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, long sessionId, CancellationToken token)
    {
        string remote = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        OnConnectionChanged?.Invoke($"Connected: {remote}");
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
        catch (Exception ex) { Log($"Connection error ({remote}): {ex.Message}"); }
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
                OnConnectionChanged?.Invoke("Phone disconnected; waiting for reconnection...");
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
            Log($"Input processing failed: {ex.Message}");
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
        if (_outputMode == OutputMode.Keyboard)
        {
            HandleKeyboardAction(protocolAction, pressed);
        }
        else if (Ds4ActionMapper.TryGet(protocolAction, out Ds4ActionMapping mapping))
        {
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
        OnButtonEvent?.Invoke($"{protocolAction} -> {action}");
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
            Log($"Keyboard output failure: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    private void HandleMoveMessage(string action)
    {
        if (_joystick == null) { Log("MOVE received before virtual joystick configuration."); return; }
        try
        {
            if (action.Equals("down", StringComparison.OrdinalIgnoreCase))
            {
                if (!_joystick.TryMoveDown(out int error)) Log($"MOVE activation failed with Win32 error {error}.");
            }
            else if (action.Equals("up", StringComparison.OrdinalIgnoreCase)) _joystick.ReleaseMove();
            else if (action.Equals("stop", StringComparison.OrdinalIgnoreCase)) _joystick.StopMovement();
        }
        catch (Exception ex)
        {
            _joystick.Reset(JoystickResetReason.CursorPositionFailure);
            ReleaseAllControls(Ds4ControlResetReason.OutputFailure);
            Log($"MOVE failed: {ex.Message}");
        }
    }

    public void ReleaseAllControls(Ds4ControlResetReason reason)
    {
        lock (_lock)
        {
            _keyboardState.ReleaseAll();
            lock (_outputLock)
            {
                Ds4ControlRelease release = _controlState.ReleaseAll(reason);
                if (_directDs4 == null) return;
                foreach (DualShock4Button button in release.DigitalButtons) _directDs4.SetButton(button, false);
                if (release.ResetLeftTrigger) _directDs4.SetTrigger(DualShock4Slider.LeftTrigger, 0);
                if (release.ResetRightTrigger) _directDs4.SetTrigger(DualShock4Slider.RightTrigger, 0);
                if (release.DigitalButtons.Count > 0 || release.ResetLeftTrigger || release.ResetRightTrigger)
                    _directDs4.SubmitReport();
            }
        }
    }

    void ILeftStickOutput.SetLeftStick(byte x, byte y)
    {
        if (_outputMode == OutputMode.Keyboard)
        {
            try { _keyboardMoveOutput.SetLeftStick(x, y); }
            catch (KeyboardOutputException ex) { Log($"Keyboard MOVE failure: {ex.InnerException?.Message ?? ex.Message}"); }
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
        Log("Service stopped and all output state released.");
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
        catch (Exception ex) { Log($"Unable to open Task Manager: {ex.Message}"); }
    }

    private void Log(string message) => OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");

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
