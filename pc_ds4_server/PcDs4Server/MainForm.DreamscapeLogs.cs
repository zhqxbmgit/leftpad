namespace PcDs4Server;

public partial class MainForm
{
    private readonly ReceiverLogBuffer _receiverLogBuffer = new();
    private DreamscapeLogsHost? _dreamscapeLogsHost;
    private bool _dreamscapeLogsFailed;
    private bool _dreamscapeLogsSmokeStarted;

    internal DreamscapeLogsHost? DreamscapeLogsHost => _dreamscapeLogsHost;

    private void InitializeDreamscapeLogsFeature()
    {
        if (!DreamscapeLogsFeature.IsEnabled)
            return;

        _dreamscapeLogsHost = new DreamscapeLogsHost(
            CreateDreamscapeLogsSnapshot,
            HandleDreamscapeLogsCommand,
            AppendLog);
        _dreamscapeLogsHost.InitializationFailed += message =>
        {
            _dreamscapeLogsFailed = true;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {message}; using native Logs fallback.");
            BeginInvoke(() =>
            {
                _dreamscapeLogsHost.Visible = false;
                if (_currentPage == ReceiverPage.Log)
                    ShowNativeLogsPage();
            });
        };
        _dreamscapeLogsHost.FrontendReady += RunDreamscapeLogsSmokeIfRequested;
        _dreamscapeLogsHost.Dock = DockStyle.None;
        _dreamscapeLogsHost.Bounds = ClientRectangle;
        _dreamscapeLogsHost.Anchor =
            AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _dreamscapeLogsHost.Visible = false;
        Controls.Add(_dreamscapeLogsHost);
    }

    private void InitializeDreamscapeLogsRuntime() =>
        _dreamscapeLogsHost?.InitializeAsync();

    private bool SetDreamscapeLogsVisibility(bool showLogs)
    {
        if (_dreamscapeLogsHost == null || _dreamscapeLogsFailed)
            return false;

        if (!showLogs)
        {
            _dreamscapeLogsHost.Visible = false;
            return false;
        }

        _dreamscapeLogsHost.Visible = true;
        _dreamscapeLogsHost.BringToFront();
        _dreamscapeLogsHost.PostSnapshot();
        return true;
    }

    private void ShowNativeLogsPage() => _logSection.Visible = true;

    private ReceiverLogSnapshot CreateDreamscapeLogsSnapshot() =>
        _receiverLogBuffer.CreateSnapshot(_cardPhone.Value);

    private ReceiverLogEntry BufferDreamscapeLog(string message) =>
        _receiverLogBuffer.Append(message);

    private void PublishDreamscapeLogAppend(ReceiverLogEntry entry) =>
        _dreamscapeLogsHost?.PostAppend(entry);

    private void PublishDreamscapeLogsConnectionState() =>
        _dreamscapeLogsHost?.PostConnectionState(_cardPhone.Value);

    private void HandleDreamscapeLogsCommand(ReceiverLogsCommand command)
    {
        switch (command)
        {
            case ReceiverLogsCommand.RequestSnapshot:
                _dreamscapeLogsHost?.PostSnapshot();
                break;
            case ReceiverLogsCommand.ReturnToBottom:
                _ = _dreamscapeLogsHost?.ExecuteScriptAsync(
                    "window.leftpadLogs.returnToBottom(); true");
                break;
            case ReceiverLogsCommand.BeginDrag:
                BeginDreamscapeWindowDrag();
                break;
            case ReceiverLogsCommand.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case ReceiverLogsCommand.CloseWindow:
                Close();
                break;
            case ReceiverLogsCommand.ShowOverview:
                NavigateTo(ReceiverPage.Overview);
                break;
            case ReceiverLogsCommand.ShowController:
                NavigateTo(ReceiverPage.Gamepad);
                break;
            case ReceiverLogsCommand.ShowSettings:
                NavigateTo(ReceiverPage.Settings);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private async void RunDreamscapeLogsSmokeIfRequested()
    {
        const string reportVariable = "LEFTPAD_WEBVIEW2_LOGS_SMOKE_REPORT";
        string? reportPath = Environment.GetEnvironmentVariable(reportVariable);
        if (string.IsNullOrWhiteSpace(reportPath) ||
            _dreamscapeLogsHost == null ||
            _dreamscapeLogsSmokeStarted)
        {
            return;
        }

        _dreamscapeLogsSmokeStarted = true;
        try
        {
            await Task.Delay(750);
            NavigateTo(ReceiverPage.Log);
            await Task.Delay(350);
            _dreamscapeLogsHost.PostSnapshot();
            await Task.Delay(150);

            string? existingDiagnostics = await _dreamscapeLogsHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadLogs.diagnostics())");
            string? runtimeCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_LOGS_CAPTURE");
            if (!string.IsNullOrWhiteSpace(runtimeCapture))
                await _dreamscapeLogsHost.CapturePreviewAsync(runtimeCapture);

            for (int index = 1; index <= 12; index++)
                AppendLog($"[Info] Web Logs smoke line {index:00}.");
            AppendLog("[Warning] Web Logs smoke warning entry.");
            AppendLog("[Controller] Web Logs smoke controller entry.");
            AppendLog("[Network] Web Logs smoke network entry.");
            await Task.Delay(250);

            string? categoryDiagnostics = await _dreamscapeLogsHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadLogs.diagnostics())");
            string? categoryCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_LOGS_CATEGORY_CAPTURE");
            if (!string.IsNullOrWhiteSpace(categoryCapture))
                await _dreamscapeLogsHost.CapturePreviewAsync(categoryCapture);

            await _dreamscapeLogsHost.ExecuteScriptAsync(
                "window.leftpadLogs.scrollUpForSmoke(); true");
            await Task.Delay(150);
            AppendLog("[Info] Web Logs smoke append while reviewing history.");
            await Task.Delay(200);
            string? scrolledDiagnostics = await _dreamscapeLogsHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadLogs.diagnostics())");
            string? scrolledCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_LOGS_SCROLLED_CAPTURE");
            if (!string.IsNullOrWhiteSpace(scrolledCapture))
                await _dreamscapeLogsHost.CapturePreviewAsync(scrolledCapture);

            await _dreamscapeLogsHost.ExecuteScriptAsync(
                "window.leftpadLogs.returnToBottom(); true");
            await Task.Delay(150);
            string? returnedDiagnostics = await _dreamscapeLogsHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadLogs.diagnostics())");

            await _dreamscapeLogsHost.ExecuteScriptAsync(
                "window.leftpadLogs.postCommand('showOverview'); true");
            await Task.Delay(350);
            bool logsToOverview = _currentPage == ReceiverPage.Overview;
            string? overviewCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_LOGS_OVERVIEW_CAPTURE");
            if (!string.IsNullOrWhiteSpace(overviewCapture) &&
                _dreamscapeOverviewHost is { IsInitialized: true })
            {
                _dreamscapeOverviewHost.PostState();
                await Task.Delay(150);
                await _dreamscapeOverviewHost.CapturePreviewAsync(overviewCapture);
            }

            bool overviewToLogs = false;
            if (_dreamscapeOverviewHost is { IsInitialized: true })
            {
                await _dreamscapeOverviewHost.ExecuteScriptAsync(
                    "window.leftpadSpike.postCommand('showLogs'); true");
                await Task.Delay(350);
                overviewToLogs = _currentPage == ReceiverPage.Log;
            }

            await _dreamscapeLogsHost.ExecuteScriptAsync(
                "window.leftpadLogs.postCommand('showController'); true");
            await Task.Delay(350);
            bool logsToController = _currentPage == ReceiverPage.Gamepad;
            string? controllerCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_LOGS_CONTROLLER_CAPTURE");
            if (!string.IsNullOrWhiteSpace(controllerCapture) &&
                _dreamscapeControllerHost is { IsInitialized: true })
            {
                _dreamscapeControllerHost.PostState();
                await Task.Delay(150);
                await _dreamscapeControllerHost.CapturePreviewAsync(controllerCapture);
            }

            bool controllerToLogs = false;
            if (_dreamscapeControllerHost is { IsInitialized: true })
            {
                await _dreamscapeControllerHost.ExecuteScriptAsync(
                    "window.leftpadController.postCommand('showLogs'); true");
                await Task.Delay(350);
                controllerToLogs = _currentPage == ReceiverPage.Log;
            }

            await _dreamscapeLogsHost.ExecuteScriptAsync(
                "window.leftpadLogs.postCommand('showSettings'); true");
            await Task.Delay(350);
            bool logsToSettings = _currentPage == ReceiverPage.Settings;
            string? settingsCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_LOGS_SETTINGS_CAPTURE");
            if (!string.IsNullOrWhiteSpace(settingsCapture) &&
                _dreamscapeSettingsHost is { IsInitialized: true })
            {
                _dreamscapeSettingsHost.PostState();
                await Task.Delay(150);
                await _dreamscapeSettingsHost.CapturePreviewAsync(settingsCapture);
            }

            bool settingsToLogs = false;
            if (_dreamscapeSettingsHost is { IsInitialized: true })
            {
                await _dreamscapeSettingsHost.ExecuteScriptAsync(
                    "window.leftpadSettings.postCommand('showLogs'); true");
                await Task.Delay(350);
                settingsToLogs = _currentPage == ReceiverPage.Log;
            }

            await _dreamscapeLogsHost.ExecuteScriptAsync(
                "window.leftpadLogs.postCommand('minimize'); true");
            await Task.Delay(250);
            bool minimizeRoundTrip = WindowState == FormWindowState.Minimized;
            WindowState = FormWindowState.Normal;
            await Task.Delay(150);

            await _dreamscapeLogsHost.ExecuteScriptAsync(
                "document.querySelector('.window-button.close').click(); true");
            await Task.Delay(250);
            bool closeToTrayRoundTrip = !Visible && _notifyIcon is { Visible: true };
            ShowMainForm();
            await Task.Delay(150);

            int visibleTopLevelWindows = Application.OpenForms.Cast<Form>()
                .Count(form => form.TopLevel && form.Visible);
            bool borderless = FormBorderStyle == FormBorderStyle.None;
            bool dragCommandAllowListed = ReceiverLogsCommandAllowList.AllowedNames
                .Contains("beginDrag");

            string? directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                runtimeVersion = _dreamscapeLogsHost.RuntimeVersion,
                initialization = "ready",
                runtimeCapture,
                categoryCapture,
                scrolledCapture,
                overviewCapture,
                controllerCapture,
                settingsCapture,
                existing = DecodeScriptJson(existingDiagnostics),
                categories = DecodeScriptJson(categoryDiagnostics),
                scrolled = DecodeScriptJson(scrolledDiagnostics),
                returned = DecodeScriptJson(returnedDiagnostics),
                navigation = new
                {
                    logsToOverview,
                    overviewToLogs,
                    logsToController,
                    controllerToLogs,
                    logsToSettings,
                    settingsToLogs
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

            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Logs] Smoke report: {reportPath}");

            if (Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_LOGS_SMOKE_AUTO_EXIT") == "1")
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
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Logs] Smoke failed: {exception.Message}");
        }
    }
}
