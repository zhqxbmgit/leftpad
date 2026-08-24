namespace PcDs4Server;

public partial class MainForm
{
    private DreamscapeOverviewHost? _dreamscapeOverviewHost;
    private System.Windows.Forms.Timer? _dreamscapeWindowDragTimer;
    private Point _dreamscapeDragPointerOrigin;
    private Point _dreamscapeDragWindowOrigin;

    internal DreamscapeOverviewHost? DreamscapeOverviewHost => _dreamscapeOverviewHost;

    private void InitializeDreamscapeOverviewFeature()
    {
        if (!DreamscapeOverviewFeature.IsEnabled)
            return;

        _dreamscapeOverviewHost = new DreamscapeOverviewHost(
            CreateDreamscapeOverviewState,
            HandleDreamscapeOverviewCommand,
            AppendLog);
        _dreamscapeOverviewHost.InitializationFailed += message =>
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {message}");
            _dreamscapeOverviewHost.Visible = false;
        };
        _dreamscapeOverviewHost.FrontendReady += RunDreamscapeSmokeReportIfRequested;
        ClientSize = DreamscapeReferenceMetadata.ReferenceSize;
        _dreamscapeOverviewHost.Dock = DockStyle.None;
        _dreamscapeOverviewHost.Bounds = ClientRectangle;
        _dreamscapeOverviewHost.Anchor =
            AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_dreamscapeOverviewHost);
        _dreamscapeOverviewHost.BringToFront();
        Disposed += (_, _) => StopDreamscapeWindowDrag();
    }

    private void InitializeDreamscapeOverviewRuntime() =>
        _dreamscapeOverviewHost?.InitializeAsync();

    private void SetDreamscapeOverviewVisibility(bool showOverview)
    {
        if (_dreamscapeOverviewHost == null)
            return;

        _dreamscapeOverviewHost.Visible = showOverview;
        if (showOverview)
        {
            _dreamscapeOverviewHost.BringToFront();
            _dreamscapeOverviewHost.PostState();
        }
    }

    private ReceiverOverviewState CreateDreamscapeOverviewState()
    {
        bool stopped = !_service.IsRunning;
        KeyboardBindings bindings = _service.KeyboardBindings;
        IReadOnlyDictionary<string, string> mappings = new Dictionary<string, string>
        {
            ["CR"] = bindings.Cross.ToString(),
            ["SQ"] = bindings.Square.ToString(),
            ["L1"] = bindings.L1.ToString(),
            ["L2"] = bindings.L2.ToString(),
            ["L3"] = bindings.L3.ToString(),
            ["CIR"] = bindings.Circle.ToString(),
            ["TRI"] = bindings.Triangle.ToString(),
            ["R1"] = bindings.R1.ToString(),
            ["R2"] = bindings.R2.ToString(),
            ["R3"] = bindings.R3.ToString()
        };

        return new ReceiverOverviewState(
            ReceiverOverviewState.MessageType,
            _cardVigem.Value,
            _cardDs4.Value,
            _cardPhone.Value,
            _service.Port,
            GetOutputModeDisplayText(_service.OutputMode),
            stopped ? "启动" : "停止",
            mappings);
    }

    private void HandleDreamscapeOverviewCommand(ReceiverOverviewCommand command)
    {
        switch (command)
        {
            case ReceiverOverviewCommand.StartStop:
                ToggleServer();
                _dreamscapeOverviewHost?.PostState();
                break;
            case ReceiverOverviewCommand.RequestState:
                _dreamscapeOverviewHost?.PostState();
                break;
            case ReceiverOverviewCommand.BeginDrag:
                BeginDreamscapeWindowDrag();
                break;
            case ReceiverOverviewCommand.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case ReceiverOverviewCommand.CloseWindow:
                Close();
                break;
            case ReceiverOverviewCommand.ShowGamepad:
                NavigateTo(ReceiverPage.Gamepad);
                break;
            case ReceiverOverviewCommand.ShowSettings:
                NavigateTo(ReceiverPage.Settings);
                break;
            case ReceiverOverviewCommand.ShowLogs:
                NavigateTo(ReceiverPage.Log);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private void BeginDreamscapeWindowDrag()
    {
        StopDreamscapeWindowDrag();
        _dreamscapeDragPointerOrigin = Cursor.Position;
        _dreamscapeDragWindowOrigin = Location;
        _dreamscapeWindowDragTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _dreamscapeWindowDragTimer.Tick += HandleDreamscapeWindowDragTick;
        _dreamscapeWindowDragTimer.Start();
    }

    private void HandleDreamscapeWindowDragTick(object? sender, EventArgs eventArgs)
    {
        if ((Control.MouseButtons & MouseButtons.Left) == 0)
        {
            StopDreamscapeWindowDrag();
            return;
        }

        Point pointer = Cursor.Position;
        Location = new Point(
            _dreamscapeDragWindowOrigin.X + pointer.X - _dreamscapeDragPointerOrigin.X,
            _dreamscapeDragWindowOrigin.Y + pointer.Y - _dreamscapeDragPointerOrigin.Y);
    }

    private void StopDreamscapeWindowDrag()
    {
        if (_dreamscapeWindowDragTimer == null)
            return;
        _dreamscapeWindowDragTimer.Stop();
        _dreamscapeWindowDragTimer.Tick -= HandleDreamscapeWindowDragTick;
        _dreamscapeWindowDragTimer.Dispose();
        _dreamscapeWindowDragTimer = null;
    }

    private async void RunDreamscapeSmokeReportIfRequested()
    {
        const string smokeReportVariable = "LEFTPAD_WEBVIEW2_SMOKE_REPORT";
        string? reportPath = Environment.GetEnvironmentVariable(smokeReportVariable);
        if (string.IsNullOrWhiteSpace(reportPath) || _dreamscapeOverviewHost == null)
            return;

        try
        {
            await Task.Delay(500);
            _dreamscapeOverviewHost.PostState();
            await Task.Delay(250);
            string? before = await _dreamscapeOverviewHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadSpike.diagnostics())");
            string? capturePath = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_SMOKE_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capturePath))
                await _dreamscapeOverviewHost.CapturePreviewAsync(capturePath);

            // Exercise the real HTML click -> WebMessage -> C# handler path.
            await _dreamscapeOverviewHost.ExecuteScriptAsync(
                "document.getElementById('start-stop').click(); true");
            await Task.Delay(500);
            _dreamscapeOverviewHost.PostState();
            await Task.Delay(250);
            string? after = await _dreamscapeOverviewHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadSpike.diagnostics())");

            // Unknown commands must be rejected and logged without terminating the host.
            await _dreamscapeOverviewHost.ExecuteScriptAsync(
                "window.chrome.webview.postMessage({command:'notAllowed'}); true");
            await Task.Delay(100);

            await _dreamscapeOverviewHost.ExecuteScriptAsync(
                "window.leftpadSpike.postCommand('minimize'); true");
            await Task.Delay(250);
            bool minimizeRoundTrip = WindowState == FormWindowState.Minimized;
            WindowState = FormWindowState.Normal;
            await Task.Delay(150);

            // Exercise the HTML close button and verify the existing WinForms
            // close-to-tray lifecycle still owns the behavior.
            await _dreamscapeOverviewHost.ExecuteScriptAsync(
                "document.querySelector('.window-button.close').click(); true");
            await Task.Delay(250);
            bool closeToTrayRoundTrip = !Visible && _notifyIcon is { Visible: true };
            ShowMainForm();
            await Task.Delay(150);

            int visibleTopLevelWindows = Application.OpenForms.Cast<Form>()
                .Count(form => form.TopLevel && form.Visible);
            bool borderless = FormBorderStyle == FormBorderStyle.None;
            bool dragCommandAllowListed = ReceiverOverviewCommandAllowList.AllowedNames
                .Contains("beginDrag");

            string? directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                package = "Microsoft.Web.WebView2",
                packageVersion = "1.0.4129.50",
                runtimeVersion = _dreamscapeOverviewHost.RuntimeVersion,
                initialization = "ready",
                capturePath,
                before = DecodeScriptJson(before),
                after = DecodeScriptJson(after),
                unknownCommandSent = true,
                minimizeRoundTrip,
                closeToTrayRoundTrip,
                visibleTopLevelWindows,
                borderless,
                dragCommandAllowListed
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Spike] Smoke report: {reportPath}");

            if (Environment.GetEnvironmentVariable("LEFTPAD_WEBVIEW2_SMOKE_AUTO_EXIT") == "1")
            {
                _isReallyClosing = true;
                _service.Stop();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                Application.Exit();
            }
        }
        catch (Exception exception)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Spike] Smoke report failed: {exception.Message}");
        }
    }

    private static System.Text.Json.JsonElement? DecodeScriptJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null")
            return null;
        string? decoded = System.Text.Json.JsonSerializer.Deserialize<string>(value);
        if (string.IsNullOrWhiteSpace(decoded))
            return null;
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(decoded);
        return document.RootElement.Clone();
    }
}
