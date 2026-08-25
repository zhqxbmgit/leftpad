namespace PcDs4Server;

public partial class MainForm
{
    private DreamscapeControllerHost? _dreamscapeControllerHost;
    private VirtualJoystickSnapshot? _latestControllerSnapshot;
    private bool _dreamscapeControllerFailed;
    private bool _dreamscapeControllerSmokeStarted;

    internal DreamscapeControllerHost? DreamscapeControllerHost => _dreamscapeControllerHost;

    private void InitializeDreamscapeControllerFeature()
    {
        if (!DreamscapeControllerFeature.IsEnabled)
            return;

        _dreamscapeControllerHost = new DreamscapeControllerHost(
            CreateDreamscapeControllerState,
            HandleDreamscapeControllerCommand,
            AppendLog);
        _dreamscapeControllerHost.InitializationFailed += message =>
        {
            _dreamscapeControllerFailed = true;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {message}; using native Controller fallback.");
            BeginInvoke(() =>
            {
                _dreamscapeControllerHost.Visible = false;
                if (_currentPage == ReceiverPage.Gamepad)
                    ShowNativeControllerPage();
            });
        };
        _dreamscapeControllerHost.FrontendReady += RunDreamscapeControllerSmokeIfRequested;
        _dreamscapeControllerHost.Dock = DockStyle.None;
        _dreamscapeControllerHost.Bounds = ClientRectangle;
        _dreamscapeControllerHost.Anchor =
            AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _dreamscapeControllerHost.Visible = false;
        Controls.Add(_dreamscapeControllerHost);
    }

    private void InitializeDreamscapeControllerRuntime() =>
        _dreamscapeControllerHost?.InitializeAsync();

    private bool SetDreamscapeControllerVisibility(bool showController)
    {
        if (_dreamscapeControllerHost == null || _dreamscapeControllerFailed)
            return false;

        if (!showController)
        {
            _dreamscapeControllerHost.Visible = false;
            return false;
        }

        _dreamscapeControllerHost.Visible = true;
        _dreamscapeControllerHost.BringToFront();
        _dreamscapeControllerHost.PostState();
        return true;
    }

    private void ShowNativeControllerPage()
    {
        _gamepadMonitor.Visible = true;
        _joystickDebugSection.Visible = true;
    }

    private ReceiverControllerState CreateDreamscapeControllerState() =>
        ReceiverControllerState.Create(
            _latestControllerSnapshot,
            _btnStates,
            _cardPhone.Value);

    private void PublishDreamscapeControllerJoystickState(VirtualJoystickSnapshot snapshot)
    {
        _latestControllerSnapshot = snapshot;
        _dreamscapeControllerHost?.PostStateIfChanged();
    }

    private void PublishDreamscapeControllerDisplayState() =>
        _dreamscapeControllerHost?.PostStateIfChanged();

    private void HandleDreamscapeControllerCommand(ReceiverControllerCommand command)
    {
        switch (command)
        {
            case ReceiverControllerCommand.RequestState:
                _dreamscapeControllerHost?.PostState();
                break;
            case ReceiverControllerCommand.BeginDrag:
                BeginDreamscapeWindowDrag();
                break;
            case ReceiverControllerCommand.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case ReceiverControllerCommand.CloseWindow:
                Close();
                break;
            case ReceiverControllerCommand.ShowOverview:
                NavigateTo(ReceiverPage.Overview);
                break;
            case ReceiverControllerCommand.ShowSettings:
                NavigateTo(ReceiverPage.Settings);
                break;
            case ReceiverControllerCommand.ShowLogs:
                NavigateTo(ReceiverPage.Log);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private async void RunDreamscapeControllerSmokeIfRequested()
    {
        const string reportVariable = "LEFTPAD_WEBVIEW2_CONTROLLER_SMOKE_REPORT";
        string? reportPath = Environment.GetEnvironmentVariable(reportVariable);
        if (string.IsNullOrWhiteSpace(reportPath) ||
            _dreamscapeControllerHost == null ||
            _dreamscapeControllerSmokeStarted)
        {
            return;
        }

        _dreamscapeControllerSmokeStarted = true;
        try
        {
            await Task.Delay(750);
            NavigateTo(ReceiverPage.Gamepad);
            await Task.Delay(350);

            ReceiverControllerState baseline = CreateDreamscapeControllerState();
            _dreamscapeControllerHost.PostState(baseline);
            await Task.Delay(150);
            string? baselineDiagnostics = await _dreamscapeControllerHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadController.diagnostics())");

            string? controllerCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_CONTROLLER_CAPTURE");
            if (!string.IsNullOrWhiteSpace(controllerCapture))
                await _dreamscapeControllerHost.CapturePreviewAsync(controllerCapture);

            ReceiverControllerState activeState = baseline with
            {
                MoveState = "按住",
                Mode = "实时",
                CursorSampling = "已启用",
                DirectionCaptured = "是",
                CurrentDirection = "0.707 / -0.707",
                Joystick = "0.707 / -0.707",
                Ds4 = "218 / 38",
                Center = "960 / 540",
                Cursor = "986 / 514",
                TrianglePressed = true
            };
            _dreamscapeControllerHost.PostState(activeState);
            await Task.Delay(150);
            string? activeDiagnostics = await _dreamscapeControllerHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadController.diagnostics())");

            string? pressedCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_CONTROLLER_PRESSED_CAPTURE");
            if (!string.IsNullOrWhiteSpace(pressedCapture))
                await _dreamscapeControllerHost.CapturePreviewAsync(pressedCapture);

            var buttonChecks = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string button in new[] { "triangle", "square", "cross", "circle" })
            {
                ReceiverControllerState buttonState = baseline with
                {
                    TrianglePressed = button == "triangle",
                    SquarePressed = button == "square",
                    CrossPressed = button == "cross",
                    CirclePressed = button == "circle"
                };
                _dreamscapeControllerHost.PostState(buttonState);
                await Task.Delay(60);
                string? result = await _dreamscapeControllerHost.ExecuteScriptAsync(
                    $"document.querySelector('[data-button=\"{button}\"]').dataset.pressed");
                buttonChecks[button] = string.Equals(result, "\"true\"", StringComparison.Ordinal);
            }
            _dreamscapeControllerHost.PostState(baseline);

            await _dreamscapeControllerHost.ExecuteScriptAsync(
                "window.leftpadController.postCommand('showOverview'); true");
            await Task.Delay(350);
            bool controllerToOverview = _currentPage == ReceiverPage.Overview;
            string? overviewCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_CONTROLLER_OVERVIEW_CAPTURE");
            if (!string.IsNullOrWhiteSpace(overviewCapture) &&
                _dreamscapeOverviewHost is { IsInitialized: true })
            {
                _dreamscapeOverviewHost.PostState();
                await Task.Delay(150);
                await _dreamscapeOverviewHost.CapturePreviewAsync(overviewCapture);
            }

            bool overviewToController = false;
            if (_dreamscapeOverviewHost is { IsInitialized: true })
            {
                await _dreamscapeOverviewHost.ExecuteScriptAsync(
                    "window.leftpadSpike.postCommand('showGamepad'); true");
                await Task.Delay(350);
                overviewToController = _currentPage == ReceiverPage.Gamepad;
            }

            await _dreamscapeControllerHost.ExecuteScriptAsync(
                "window.leftpadController.postCommand('showSettings'); true");
            await Task.Delay(350);
            bool controllerToSettings = _currentPage == ReceiverPage.Settings;
            string? settingsCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_CONTROLLER_SETTINGS_CAPTURE");
            if (!string.IsNullOrWhiteSpace(settingsCapture) &&
                _dreamscapeSettingsHost is { IsInitialized: true })
            {
                _dreamscapeSettingsHost.PostState();
                await Task.Delay(150);
                await _dreamscapeSettingsHost.CapturePreviewAsync(settingsCapture);
            }

            bool controllerToNativeLogs = false;
            if (_dreamscapeSettingsHost is { IsInitialized: true })
            {
                await _dreamscapeSettingsHost.ExecuteScriptAsync(
                    "window.leftpadSettings.postCommand('showLogs'); true");
                await Task.Delay(350);
                controllerToNativeLogs = _currentPage == ReceiverPage.Log &&
                    _logSection.Visible &&
                    !_dreamscapeControllerHost.Visible;
            }

            NavigateTo(ReceiverPage.Gamepad);
            await Task.Delay(150);
            await _dreamscapeControllerHost.ExecuteScriptAsync(
                "window.leftpadController.postCommand('minimize'); true");
            await Task.Delay(250);
            bool minimizeRoundTrip = WindowState == FormWindowState.Minimized;
            WindowState = FormWindowState.Normal;
            await Task.Delay(150);

            await _dreamscapeControllerHost.ExecuteScriptAsync(
                "document.querySelector('.window-button.close').click(); true");
            await Task.Delay(250);
            bool closeToTrayRoundTrip = !Visible && _notifyIcon is { Visible: true };
            ShowMainForm();
            await Task.Delay(150);

            int visibleTopLevelWindows = Application.OpenForms.Cast<Form>()
                .Count(form => form.TopLevel && form.Visible);
            bool borderless = FormBorderStyle == FormBorderStyle.None;
            bool dragCommandAllowListed = ReceiverControllerCommandAllowList.AllowedNames
                .Contains("beginDrag");

            string? directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                runtimeVersion = _dreamscapeControllerHost.RuntimeVersion,
                initialization = "ready",
                controllerCapture,
                pressedCapture,
                overviewCapture,
                settingsCapture,
                baseline = DecodeScriptJson(baselineDiagnostics),
                active = DecodeScriptJson(activeDiagnostics),
                buttonChecks,
                navigation = new
                {
                    controllerToOverview,
                    overviewToController,
                    controllerToSettings,
                    controllerToNativeLogs
                },
                window = new
                {
                    minimizeRoundTrip,
                    closeToTrayRoundTrip,
                    borderless,
                    dragCommandAllowListed,
                    visibleTopLevelWindows
                }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Controller] Smoke report: {reportPath}");

            if (Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_CONTROLLER_SMOKE_AUTO_EXIT") == "1")
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
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Controller] Smoke failed: {exception.Message}");
        }
    }
}
