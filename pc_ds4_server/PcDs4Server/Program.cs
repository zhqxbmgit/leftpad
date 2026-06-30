using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server
{
    class Program
    {
        private static ViGEmClient? _client;
        private static IDualShock4Controller? _controller;
        // 直接存储枚举类型
        private static HashSet<DualShock4Button> _activeButtons = new();
        private static readonly object _lock = new();

        static async Task Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("   🎮 虚拟 PS4 手柄接收端 (ViGEmBus)");
            Console.WriteLine("========================================");

            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateDualShock4Controller();
                _controller.Connect();
                Console.WriteLine("✅ 成功创建并连接虚拟 DualShock 4 手柄");
                Console.WriteLine("💡 提示: 你可以在 'joy.cpl' 中查看到 Wireless Controller");
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ 无法创建虚拟手柄。请检查是否安装了 ViGEmBus 驱动。");
                Console.WriteLine($"错误详情: {ex.Message}");
                Console.WriteLine("\n驱动下载地址: https://github.com/nefarius/ViGEmBus/releases");
                return;
            }

            string localIp = GetLocalIp();
            int port = 8888;
            TcpListener server = new TcpListener(IPAddress.Any, port);
            server.Start();

            Console.WriteLine($"\n📍 电脑局域网 IP: {localIp}");
            Console.WriteLine($"🔌 监听端口: {port}");
            Console.WriteLine("\n等待手机连接... (按 Ctrl+C 退出)");

            Console.CancelKeyPress += (s, e) =>
            {
                ReleaseAllButtons();
                _controller?.Disconnect();
                Console.WriteLine("\n正在关闭程序...");
            };

            while (true)
            {
                try
                {
                    using TcpClient client = await server.AcceptTcpClientAsync();
                    Console.WriteLine($"✅ 手机已连接！来自: {client.Client.RemoteEndPoint}");

                    using NetworkStream stream = client.GetStream();
                    byte[] buffer = new byte[1024];

                    while (true)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        string rawData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        ProcessMessages(rawData);
                    }

                    Console.WriteLine("❌ 手机断开连接");
                    ReleaseAllButtons();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ 连接异常: {ex.Message}");
                    ReleaseAllButtons();
                }
            }
        }

        private static void ProcessMessages(string rawData)
        {
            var lines = rawData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                try
                {
                    var message = JsonSerializer.Deserialize<ButtonMessage>(line.Trim());
                    if (message != null)
                    {
                        UpdateControllerState(message);
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        private static void UpdateControllerState(ButtonMessage msg)
        {
            if (_controller == null) return;

            // 这里使用反射或映射来获取按键
            if (TryGetButton(msg.button, out DualShock4Button button))
            {
                lock (_lock)
                {
                    if (msg.action == "down")
                    {
                        _controller.SetButtonState(button, true);
                        _activeButtons.Add(button);
                        Console.WriteLine($"按下: {msg.button} -> DS4 {button}");
                    }
                    else if (msg.action == "up")
                    {
                        _controller.SetButtonState(button, false);
                        _activeButtons.Remove(button);
                        Console.WriteLine($"松开: {msg.button} -> DS4 {button}");
                    }
                    _controller.SubmitReport();
                }
            }
        }

        private static bool TryGetButton(string name, out DualShock4Button button)
        {
            button = DualShock4Button.Circle; // 默认
            switch (name.ToLower())
            {
                case "circle": button = DualShock4Button.Circle; return true;
                case "cross": button = DualShock4Button.Cross; return true;
                case "triangle": button = DualShock4Button.Triangle; return true;
                case "square": button = DualShock4Button.Square; return true;
                default: return false;
            }
        }

        private static void ReleaseAllButtons()
        {
            if (_controller == null) return;
            lock (_lock)
            {
                if (_activeButtons.Count > 0)
                {
                    Console.WriteLine("正在释放所有手柄按键...");
                    foreach (var btn in _activeButtons)
                    {
                        _controller.SetButtonState(btn, false);
                    }
                    _activeButtons.Clear();
                    _controller.SubmitReport();
                }
            }
        }

        private static string GetLocalIp()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint?.Address.ToString() ?? "127.0.0.1";
                }
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }

    public class ButtonMessage
    {
        public string button { get; set; } = "";
        public string action { get; set; } = "";
    }
}
