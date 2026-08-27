namespace PcDs4Server;

public partial class MainForm
{
    private bool _dreamscapePageActivationSmokeStarted;

    private void InitializeDreamscapePageActivation()
    {
        VisibleChanged += (_, _) => SynchronizeDreamscapePageActivation();
        if (_dreamscapeOverviewHost != null)
            _dreamscapeOverviewHost.FrontendReady += RunDreamscapePageActivationSmokeIfRequested;
        if (_dreamscapeSettingsHost != null)
            _dreamscapeSettingsHost.FrontendReady += RunDreamscapePageActivationSmokeIfRequested;
        if (_dreamscapeControllerHost != null)
            _dreamscapeControllerHost.FrontendReady += RunDreamscapePageActivationSmokeIfRequested;
        if (_dreamscapeLogsHost != null)
            _dreamscapeLogsHost.FrontendReady += RunDreamscapePageActivationSmokeIfRequested;
        SynchronizeDreamscapePageActivation();
    }

    private void SynchronizeDreamscapePageActivation()
    {
        if (IsDisposed || Disposing)
            return;

        bool receiverVisible = Visible;
        _dreamscapeOverviewHost?.SetPageActive(
            receiverVisible &&
            _currentPage == ReceiverPage.Overview &&
            _dreamscapeOverviewHost.Visible);
        _dreamscapeSettingsHost?.SetPageActive(
            receiverVisible &&
            _currentPage == ReceiverPage.Settings &&
            _dreamscapeSettingsHost.Visible);
    }

    internal DreamscapePageActivity GetDreamscapePageActivity() => new(
        ReceiverVisible: Visible,
        CurrentPage: _currentPage.ToString(),
        Overview: Visible && _currentPage == ReceiverPage.Overview &&
            _dreamscapeOverviewHost is { Visible: true },
        Controller: Visible && _currentPage == ReceiverPage.Gamepad &&
            _dreamscapeControllerHost is { Visible: true },
        Settings: Visible && _currentPage == ReceiverPage.Settings &&
            _dreamscapeSettingsHost is { Visible: true },
        Logs: Visible && _currentPage == ReceiverPage.Log &&
            _dreamscapeLogsHost is { Visible: true });

    private async void RunDreamscapePageActivationSmokeIfRequested()
    {
        string? reportPath = Environment.GetEnvironmentVariable(
            "LEFTPAD_DREAMSCAPE_PAGE_ACTIVATION_REPORT");
        if (string.IsNullOrWhiteSpace(reportPath) ||
            _dreamscapePageActivationSmokeStarted ||
            _dreamscapeOverviewHost is not { IsInitialized: true } ||
            _dreamscapeSettingsHost is not { IsInitialized: true } ||
            _dreamscapeControllerHost is not { IsInitialized: true } ||
            _dreamscapeLogsHost is not { IsInitialized: true })
        {
            return;
        }

        _dreamscapePageActivationSmokeStarted = true;
        try
        {
            await Task.Delay(500);

            NavigateTo(ReceiverPage.Log);
            await Task.Delay(200);
            PagePollingDiagnostics logsStart = await ReadPagePollingDiagnosticsAsync();
            await Task.Delay(3100);
            PagePollingDiagnostics logsEnd = await ReadPagePollingDiagnosticsAsync();
            DreamscapePageActivity logsActivity = GetDreamscapePageActivity();

            PagePollingDiagnostics overviewBefore = logsEnd;
            DateTime overviewActivatedAt = DateTime.UtcNow;
            NavigateTo(ReceiverPage.Overview);
            int overviewImmediateMilliseconds = await WaitForRequestIncreaseAsync(
                overview: true,
                overviewBefore.OverviewRequests,
                TimeSpan.FromMilliseconds(900));
            await Task.Delay(2600);
            PagePollingDiagnostics overviewActiveEnd = await ReadPagePollingDiagnosticsAsync();

            NavigateTo(ReceiverPage.Log);
            await Task.Delay(200);
            PagePollingDiagnostics overviewHiddenStart = await ReadPagePollingDiagnosticsAsync();
            await Task.Delay(3100);
            PagePollingDiagnostics overviewHiddenEnd = await ReadPagePollingDiagnosticsAsync();

            PagePollingDiagnostics settingsBefore = overviewHiddenEnd;
            NavigateTo(ReceiverPage.Settings);
            int settingsImmediateMilliseconds = await WaitForRequestIncreaseAsync(
                overview: false,
                settingsBefore.SettingsRequests,
                TimeSpan.FromMilliseconds(900));
            await Task.Delay(2600);
            PagePollingDiagnostics settingsActiveEnd = await ReadPagePollingDiagnosticsAsync();

            NavigateTo(ReceiverPage.Log);
            await Task.Delay(200);
            PagePollingDiagnostics settingsHiddenStart = await ReadPagePollingDiagnosticsAsync();
            await Task.Delay(3100);
            PagePollingDiagnostics settingsHiddenEnd = await ReadPagePollingDiagnosticsAsync();

            NavigateTo(ReceiverPage.Settings);
            await Task.Delay(200);
            PagePollingDiagnostics trayStart = await ReadPagePollingDiagnosticsAsync();
            Hide();
            await Task.Delay(3100);
            PagePollingDiagnostics trayEnd = await ReadPagePollingDiagnosticsAsync();
            string trayCurrentPage = _currentPage.ToString();
            DateTime restoreStartedAt = DateTime.UtcNow;
            string? externalRestoreMarker = Environment.GetEnvironmentVariable(
                "LEFTPAD_DREAMSCAPE_PAGE_ACTIVATION_TRAY_READY");
            string restoreMode;
            if (!string.IsNullOrWhiteSpace(externalRestoreMarker))
            {
                string? markerDirectory = Path.GetDirectoryName(externalRestoreMarker);
                if (!string.IsNullOrEmpty(markerDirectory))
                    Directory.CreateDirectory(markerDirectory);
                File.WriteAllText(externalRestoreMarker, "ready");
                restoreMode = "secondaryProcess";
                DateTime externalRestoreDeadline = DateTime.UtcNow.AddSeconds(8);
                while (!Visible && DateTime.UtcNow < externalRestoreDeadline)
                    await Task.Delay(20);
                if (!Visible)
                    throw new TimeoutException("Secondary process did not restore the tray-hidden receiver.");
            }
            else
            {
                restoreMode = "directActivation";
                ActivateExistingInstance();
            }
            int restoreImmediateMilliseconds = await WaitForRequestIncreaseAsync(
                overview: false,
                trayEnd.SettingsRequests,
                TimeSpan.FromMilliseconds(900));
            await Task.Delay(1100);
            PagePollingDiagnostics restoreEnd = await ReadPagePollingDiagnosticsAsync();
            DreamscapePageActivity restoreActivity = GetDreamscapePageActivity();

            for (int cycle = 0; cycle < 50; cycle++)
            {
                NavigateTo(ReceiverPage.Overview);
                NavigateTo(ReceiverPage.Gamepad);
                NavigateTo(ReceiverPage.Settings);
                NavigateTo(ReceiverPage.Log);
                NavigateTo(ReceiverPage.Overview);
            }
            await Task.Delay(150);
            PagePollingDiagnostics overviewStressStart = await ReadPagePollingDiagnosticsAsync();
            await Task.Delay(2500);
            PagePollingDiagnostics overviewStressEnd = await ReadPagePollingDiagnosticsAsync();

            NavigateTo(ReceiverPage.Settings);
            await Task.Delay(150);
            PagePollingDiagnostics settingsStressStart = await ReadPagePollingDiagnosticsAsync();
            await Task.Delay(2500);
            PagePollingDiagnostics settingsStressEnd = await ReadPagePollingDiagnosticsAsync();

            string? reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                runtimeVersion = _dreamscapeOverviewHost.RuntimeVersion,
                logsVisible = new
                {
                    durationMilliseconds = 3100,
                    overviewDelta = logsEnd.OverviewRequests - logsStart.OverviewRequests,
                    settingsDelta = logsEnd.SettingsRequests - logsStart.SettingsRequests,
                    activity = logsActivity
                },
                overviewVisible = new
                {
                    activationUtc = overviewActivatedAt,
                    immediateMilliseconds = overviewImmediateMilliseconds,
                    overviewDelta = overviewActiveEnd.OverviewRequests - overviewBefore.OverviewRequests,
                    settingsDelta = overviewActiveEnd.SettingsRequests - overviewBefore.SettingsRequests,
                    pageActive = overviewActiveEnd.OverviewActive,
                    polling = overviewActiveEnd.OverviewPolling
                },
                overviewHidden = new
                {
                    durationMilliseconds = 3100,
                    overviewDelta = overviewHiddenEnd.OverviewRequests - overviewHiddenStart.OverviewRequests
                },
                settingsVisible = new
                {
                    immediateMilliseconds = settingsImmediateMilliseconds,
                    overviewDelta = settingsActiveEnd.OverviewRequests - settingsBefore.OverviewRequests,
                    settingsDelta = settingsActiveEnd.SettingsRequests - settingsBefore.SettingsRequests,
                    pageActive = settingsActiveEnd.SettingsActive,
                    polling = settingsActiveEnd.SettingsPolling
                },
                settingsHidden = new
                {
                    durationMilliseconds = 3100,
                    settingsDelta = settingsHiddenEnd.SettingsRequests - settingsHiddenStart.SettingsRequests
                },
                trayHidden = new
                {
                    durationMilliseconds = 3100,
                    currentPage = trayCurrentPage,
                    overviewDelta = trayEnd.OverviewRequests - trayStart.OverviewRequests,
                    settingsDelta = trayEnd.SettingsRequests - trayStart.SettingsRequests,
                    overviewActive = trayEnd.OverviewActive,
                    settingsActive = trayEnd.SettingsActive
                },
                restore = new
                {
                    startedUtc = restoreStartedAt,
                    mode = restoreMode,
                    immediateMilliseconds = restoreImmediateMilliseconds,
                    currentPage = _currentPage.ToString(),
                    settingsDelta = restoreEnd.SettingsRequests - trayEnd.SettingsRequests,
                    activity = restoreActivity
                },
                fiftyCycles = new
                {
                    cycles = 50,
                    overviewRequestsOver2500ms =
                        overviewStressEnd.OverviewRequests - overviewStressStart.OverviewRequests,
                    settingsRequestsOver2500ms =
                        settingsStressEnd.SettingsRequests - settingsStressStart.SettingsRequests,
                    overviewTimerStarts = overviewStressEnd.OverviewTimerStarts,
                    overviewTimerStops = overviewStressEnd.OverviewTimerStops,
                    settingsTimerStarts = settingsStressEnd.SettingsTimerStarts,
                    settingsTimerStops = settingsStressEnd.SettingsTimerStops
                }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        }
        catch (Exception exception)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [Page Activation] Smoke failed: {exception.Message}");
            try
            {
                string? reportDirectory = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(reportDirectory))
                    Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
                {
                    failure = exception.ToString()
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Preserve the original runtime failure when the report path is unavailable.
            }
        }
        finally
        {
            if (Environment.GetEnvironmentVariable(
                "LEFTPAD_DREAMSCAPE_PAGE_ACTIVATION_AUTO_EXIT") == "1")
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
    }

    private async Task<PagePollingDiagnostics> ReadPagePollingDiagnosticsAsync()
    {
        System.Text.Json.JsonElement overview = DecodeScriptJson(
            await _dreamscapeOverviewHost!.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadSpike.diagnostics())")) ??
            throw new InvalidOperationException("Overview diagnostics unavailable.");
        System.Text.Json.JsonElement settings = DecodeScriptJson(
            await _dreamscapeSettingsHost!.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadSettings.diagnostics())")) ??
            throw new InvalidOperationException("Settings diagnostics unavailable.");
        return new PagePollingDiagnostics(
            overview.GetProperty("stateRequestsSent").GetInt32(),
            settings.GetProperty("stateRequestsSent").GetInt32(),
            overview.GetProperty("pageActive").GetBoolean(),
            settings.GetProperty("pageActive").GetBoolean(),
            overview.GetProperty("polling").GetBoolean(),
            settings.GetProperty("polling").GetBoolean(),
            overview.GetProperty("pollingTimerStarts").GetInt32(),
            overview.GetProperty("pollingTimerStops").GetInt32(),
            settings.GetProperty("pollingTimerStarts").GetInt32(),
            settings.GetProperty("pollingTimerStops").GetInt32());
    }

    private async Task<int> WaitForRequestIncreaseAsync(
        bool overview,
        int initialCount,
        TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            PagePollingDiagnostics diagnostics = await ReadPagePollingDiagnosticsAsync();
            int current = overview
                ? diagnostics.OverviewRequests
                : diagnostics.SettingsRequests;
            if (current > initialCount)
                return (int)stopwatch.ElapsedMilliseconds;
            await Task.Delay(20);
        }
        return -1;
    }

    private sealed record PagePollingDiagnostics(
        int OverviewRequests,
        int SettingsRequests,
        bool OverviewActive,
        bool SettingsActive,
        bool OverviewPolling,
        bool SettingsPolling,
        int OverviewTimerStarts,
        int OverviewTimerStops,
        int SettingsTimerStarts,
        int SettingsTimerStops);
}

internal sealed record DreamscapePageActivity(
    bool ReceiverVisible,
    string CurrentPage,
    bool Overview,
    bool Controller,
    bool Settings,
    bool Logs);
