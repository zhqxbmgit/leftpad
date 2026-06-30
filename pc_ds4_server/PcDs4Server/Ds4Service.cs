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
    public class Ds4Service
    {
        private ViGEmClient? _client;
        private IDualShock4Controller? _controller;
        private TcpListener? _server;
        private CancellationTokenSource? _cts;
        private readonly HashSet<DualShock4Button> _activeButtons = new();
        private readonly object _lock = new();

        public event Action<string>? OnLog;
        public event Action<string>? OnStatusChanged;
        public event Action<string>? OnConnectionChanged;
        public event Action<string>? OnButtonEvent;

        public bool IsRunning { get; private set; }
        public string LocalIp { get; private set; } = "Unknown";
        public int Port { get; } = 8888;

        public Ds4Service()
        {
            LocalIp = GetLocalIp();
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
                    _ = HandleClientAsync(client, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"⚠️ 接收客户端出错: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            string remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            Log($"✅ 手机已连接！来自: {remoteEp}");
            OnConnectionChanged?.Invoke($"已连接: {remoteEp}");

            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    byte[] buffer = new byte[1024];
                    while (!token.IsCancellationRequested)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead == 0) break;

                        string rawData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        ProcessMessages(rawData);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"⚠️ 连接异常 ({remoteEp}): {ex.Message}");
            }
            finally
            {
                Log($"❌ 手机断开连接: {remoteEp}");
                OnConnectionChanged?.Invoke("手机已断开，等待重连");
                ReleaseAllButtons();
            }
        }

        private void ProcessMessages(string rawData)
        {
            var lines = rawData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                try
                {
                    var message = JsonSerializer.Deserialize<ButtonMessage>(line.Trim());
                    if (message != null)
                    {
                        if (!string.IsNullOrEmpty(message.command))
                        {
                            HandleCommand(message.command);
                        }
                        else
                        {
                            UpdateControllerState(message);
                        }
                    }
                }
                catch { /* 忽略格式错误 */ }
            }
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

        private void UpdateControllerState(ButtonMessage msg)
        {
            if (_controller == null) return;

            if (TryGetButton(msg.button, out DualShock4Button button))
            {
                lock (_lock)
                {
                    bool isDown = msg.action == "down";
                    _controller.SetButtonState(button, isDown);

                    if (isDown) _activeButtons.Add(button);
                    else _activeButtons.Remove(button);

                    _controller.SubmitReport();
                    OnButtonEvent?.Invoke($"{msg.button} -> {msg.action}");
                }
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
            if (_controller == null) return;
            lock (_lock)
            {
                if (_activeButtons.Count > 0)
                {
                    foreach (var btn in _activeButtons) _controller.SetButtonState(btn, false);
                    _activeButtons.Clear();
                    _controller.SubmitReport();
                }
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _server?.Stop();
            ReleaseAllButtons();
            _controller?.Disconnect();
            _client?.Dispose();
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
