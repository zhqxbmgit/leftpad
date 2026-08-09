using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server
{
    public class Ds4Service : ILeftStickOutput
    {
        private ViGEmClient? _client;
        private IDualShock4Controller? _controller;
        private TcpListener? _server;
        private CancellationTokenSource? _cts;
        private readonly HashSet<DualShock4Button> _activeButtons = new();
        private readonly object _lock = new();
        private readonly object _controllerLock = new();
        private TcpClient? _activeClient;
        private long _activeSessionId;
        private VirtualJoystickController? _joystick;
        private CursorJoystickSampler? _joystickSampler;

        public event Action<string>? OnLog;
        public event Action<string>? OnStatusChanged;
        public event Action<string>? OnConnectionChanged;
        public event Action<string>? OnButtonEvent;
        public event Action<VirtualJoystickSnapshot>? OnJoystickStateChanged;

        public bool IsRunning { get; private set; }
        public string LocalIp { get; private set; } = "Unknown";
        public int Port { get; } = 8888;

        public Ds4Service()
        {
            LocalIp = GetLocalIp();
        }

        public void ConfigureVirtualJoystick(IJoystickOverlay overlay, ICursorPositionProvider cursor)
        {
            if (_joystick != null)
            {
                throw new InvalidOperationException("The virtual joystick has already been configured.");
            }

            _joystick = new VirtualJoystickController(this, overlay, cursor);
            _joystick.StateChanged += snapshot => OnJoystickStateChanged?.Invoke(snapshot);
            _joystick.CursorPositionReadFailed += error =>
                Log($"Cursor sampling stopped: GetCursorPos failed with Win32 error {error}.");
            OnJoystickStateChanged?.Invoke(_joystick.Snapshot);
        }

        public void ResetVirtualJoystick(JoystickResetReason reason)
        {
            _joystick?.Reset(reason);
        }

        public bool Initialize()
        {
            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateDualShock4Controller();
                _controller.Connect();
                Log("✅ 成功创建并连接虚拟 DualShock 4 手柄");
                OnStatusChanged?.Invoke("ViGEmBus: 已连接 / 虚拟 DS4: 就绪");
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ 无法初始化 ViGEm: {ex.Message}");
                OnStatusChanged?.Invoke("ViGEmBus: 未安装或初始化失败");
                return false;
            }
        }

        public void Start()
        {
            if (IsRunning) return;

            _server = new TcpListener(IPAddress.Any, Port);
            _server.Start();
            IsRunning = true;
            _cts = new CancellationTokenSource();
            if (_joystick != null)
            {
                _joystickSampler = new CursorJoystickSampler(_joystick);
                _joystickSampler.SamplingFailed += ex =>
                    Log($"Cursor sampling stopped after an unexpected error: {ex.Message}");
            }

            Log($"🚀 TCP 服务已启动，监听端口: {Port}");
            OnConnectionChanged?.Invoke("等待手机连接...");

            Task.Run(() => AcceptClientsAsync(_cts.Token));
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
                    ReleaseAllButtons();
                    previousClient?.Dispose();
                    _ = HandleClientAsync(client, sessionId, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"⚠️ 接收客户端出错: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, long sessionId, CancellationToken token)
        {
            string remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            Log($"✅ 手机已连接！来自: {remoteEp}");
            OnConnectionChanged?.Invoke($"已连接: {remoteEp}");

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
            catch (Exception ex)
            {
                Log($"⚠️ 连接异常 ({remoteEp}): {ex.Message}");
            }
            finally
            {
                bool wasActiveSession;
                lock (_lock)
                {
                    wasActiveSession = sessionId == _activeSessionId;
                    if (wasActiveSession)
                    {
                        _activeClient = null;
                    }
                }

                if (wasActiveSession)
                {
                    Log($"❌ 手机断开连接: {remoteEp}");
                    OnConnectionChanged?.Invoke("手机已断开，等待重连");
                    ResetVirtualJoystick(JoystickResetReason.Disconnect);
                    ReleaseAllButtons();
                }
            }
        }

        private void ProcessMessage(string rawData, long sessionId)
        {
            try
            {
                var message = JsonSerializer.Deserialize<ButtonMessage>(rawData.Trim());
                if (message != null)
                {
                    if (!string.IsNullOrEmpty(message.command))
                    {
                        lock (_lock)
                        {
                            if (sessionId == _activeSessionId)
                            {
                                HandleCommand(message.command);
                            }
                        }
                    }
                    else
                    {
                        UpdateControllerState(message, sessionId);
                    }
                }
            }
            catch { /* 忽略格式错误 */ }
        }

        private void HandleCommand(string command)
        {
            if (command == "task_manager")
            {
                try
                {
                    Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                    Log("💻 已通过远程指令打开任务管理器");
                }
                catch (Exception ex)
                {
                    Log($"❌ 打开任务管理器失败: {ex.Message}");
                }
            }
        }

        private void UpdateControllerState(ButtonMessage msg, long sessionId)
        {
            lock (_lock)
            {
                if (sessionId != _activeSessionId)
                {
                    return;
                }

                if (msg.button.Equals("move", StringComparison.OrdinalIgnoreCase))
                {
                    HandleMoveMessage(msg.action);
                    OnButtonEvent?.Invoke($"{msg.button} -> {msg.action}");
                    return;
                }

                if (TryGetButton(msg.button, out DualShock4Button button))
                {
                    bool isDown = msg.action == "down";
                    lock (_controllerLock)
                    {
                        if (_controller == null) return;
                        _controller.SetButtonState(button, isDown);

                        if (isDown) _activeButtons.Add(button);
                        else _activeButtons.Remove(button);

                        _controller.SubmitReport();
                    }
                    OnButtonEvent?.Invoke($"{msg.button} -> {msg.action}");
                }
            }
        }

        private void HandleMoveMessage(string action)
        {
            if (_joystick == null)
            {
                Log("❌ 收到 MOVE，但虚拟摇杆尚未配置");
                return;
            }

            try
            {
                if (action == "down")
                {
                    if (!_joystick.TryMoveDown(out int win32Error))
                    {
                        Log($"MOVE activation failed: GetCursorPos returned Win32 error {win32Error}.");
                    }
                }
                else if (action == "up")
                {
                    _joystick.Reset(JoystickResetReason.MoveUp);
                }
            }
            catch (Exception ex)
            {
                _joystick.Reset(JoystickResetReason.CursorPositionFailure);
                Log($"❌ MOVE 激活失败: {ex.Message}");
            }
        }

        private bool TryGetButton(string name, out DualShock4Button button)
        {
            button = DualShock4Button.Circle;
            switch (name.ToLower())
            {
                case "circle": button = DualShock4Button.Circle; return true;
                case "cross": button = DualShock4Button.Cross; return true;
                case "triangle": button = DualShock4Button.Triangle; return true;
                case "square": button = DualShock4Button.Square; return true;
                default: return false;
            }
        }

        public void ReleaseAllButtons()
        {
            lock (_lock)
            {
                if (_activeButtons.Count > 0)
                {
                    lock (_controllerLock)
                    {
                        if (_controller != null)
                        {
                            foreach (var btn in _activeButtons) _controller.SetButtonState(btn, false);
                            _controller.SubmitReport();
                        }
                        _activeButtons.Clear();
                    }
                }
            }
        }

        void ILeftStickOutput.SetLeftStick(byte x, byte y)
        {
            lock (_controllerLock)
            {
                if (_controller == null)
                {
                    return;
                }

                _controller.SetAxisValue(DualShock4Axis.LeftThumbX, x);
                _controller.SetAxisValue(DualShock4Axis.LeftThumbY, y);
                _controller.SubmitReport();
            }
        }

        public void Stop()
        {
            if (!IsRunning && _controller == null && _client == null) return;

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
            ReleaseAllButtons();
            ResetVirtualJoystick(JoystickResetReason.ControllerDispose);
            lock (_controllerLock)
            {
                _controller?.Disconnect();
                _client?.Dispose();
                _controller = null;
                _client = null;
            }
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
            Log("🛑 服务已停止并释放资源");
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private string GetLocalIp()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
                }
            }
            catch { return "127.0.0.1"; }
        }

        public class ButtonMessage
        {
            public string button { get; set; } = "";
            public string action { get; set; } = "";
            public string command { get; set; } = "";
        }
    }
}
