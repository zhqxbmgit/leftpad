namespace PcDs4Server;

public partial class MainForm
{
    private DreamscapeSettingsHost? _dreamscapeSettingsHost;
    private DreamscapeSettingsBasicSession? _dreamscapeSettingsSession;
    private bool _dreamscapeSettingsFailed;
    private bool _dreamscapeSettingsSmokeStarted;

    internal DreamscapeSettingsHost? DreamscapeSettingsHost => _dreamscapeSettingsHost;

    private void InitializeDreamscapeSettingsFeature()
    {
        if (!DreamscapeSettingsFeature.IsEnabled)
            return;

        _dreamscapeSettingsSession = new DreamscapeSettingsBasicSession(
            () => _radialMenu.ActiveSettings,
            () => _radialVisualPackCatalog.Discover(),
            settings => _radialMenu.PreviewAt(Cursor.Position, settings),
            settings => _radialMenu.UpdatePreview(settings),
            () => _radialMenu.ClosePreview(),
            ApplyAndSaveDreamscapeSettings,
            LogRadialMessage);
        _dreamscapeSettingsHost = new DreamscapeSettingsHost(
            () => _dreamscapeSettingsSession.CreateState(_cardPhone.Value),
            HandleDreamscapeSettingsMessage,
            AppendLog);
        _dreamscapeSettingsHost.InitializationFailed += message =>
        {
            _dreamscapeSettingsFailed = true;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {message}; using native Settings fallback.");
            BeginInvoke(() =>
            {
                _dreamscapeSettingsHost.Visible = false;
                if (_currentPage == ReceiverPage.Settings)
                    ShowNativeSettingsTab(0);
            });
        };
        _dreamscapeSettingsHost.FrontendReady += RunDreamscapeSettingsSmokeIfRequested;
        _dreamscapeSettingsHost.Dock = DockStyle.None;
        _dreamscapeSettingsHost.Bounds = ClientRectangle;
        _dreamscapeSettingsHost.Anchor =
            AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _dreamscapeSettingsHost.Visible = false;
        Controls.Add(_dreamscapeSettingsHost);
    }

    private void InitializeDreamscapeSettingsRuntime() =>
        _dreamscapeSettingsHost?.InitializeAsync();

    private bool SetDreamscapeSettingsVisibility(bool showSettings)
    {
        if (_dreamscapeSettingsHost == null ||
            _dreamscapeSettingsSession == null ||
            _dreamscapeSettingsFailed)
        {
            return false;
        }

        if (!showSettings)
        {
            _dreamscapeSettingsSession.Deactivate();
            _dreamscapeSettingsHost.Visible = false;
            return false;
        }

        if (!_dreamscapeSettingsSession.IsActive)
            _dreamscapeSettingsSession.Activate();
        _dreamscapeSettingsHost.Visible = true;
        _dreamscapeSettingsHost.BringToFront();
        _dreamscapeSettingsHost.PostState();
        return true;
    }

    private void HandleDreamscapeSettingsMessage(ReceiverSettingsMessage message)
    {
        if (_dreamscapeSettingsHost == null || _dreamscapeSettingsSession == null)
            return;

        switch (message.Command)
        {
            case ReceiverSettingsCommand.BasicChange:
                if (!_dreamscapeSettingsSession.TryApplyChange(message, out string rejectionReason))
                    AppendLog($"[WebView2 Settings] Rejected change: {rejectionReason}");
                _dreamscapeSettingsHost.PostState();
                break;
            case ReceiverSettingsCommand.Preview:
                _dreamscapeSettingsSession.Preview();
                _dreamscapeSettingsHost.PostState();
                break;
            case ReceiverSettingsCommand.HidePreview:
                _dreamscapeSettingsSession.HidePreview();
                _dreamscapeSettingsHost.PostState();
                break;
            case ReceiverSettingsCommand.ApplySave:
                if (!_dreamscapeSettingsSession.ApplyAndSave(out string saveError))
                    AppendLog($"[WebView2 Settings] Apply/save failed: {saveError}");
                _dreamscapeSettingsHost.PostState();
                break;
            case ReceiverSettingsCommand.RestoreDefault:
                _dreamscapeSettingsSession.RestoreDefault();
                _dreamscapeSettingsHost.PostState();
                break;
            case ReceiverSettingsCommand.RequestState:
                _dreamscapeSettingsHost.PostState();
                break;
            case ReceiverSettingsCommand.ShowOverview:
                NavigateTo(ReceiverPage.Overview);
                break;
            case ReceiverSettingsCommand.ShowGamepad:
                NavigateTo(ReceiverPage.Gamepad);
                break;
            case ReceiverSettingsCommand.ShowLogs:
                NavigateTo(ReceiverPage.Log);
                break;
            case ReceiverSettingsCommand.ShowAdvanced:
                ShowNativeSettingsTab(1);
                break;
            case ReceiverSettingsCommand.ShowMappings:
                ShowNativeSettingsTab(2);
                break;
            case ReceiverSettingsCommand.BeginDrag:
                BeginDreamscapeWindowDrag();
                break;
            case ReceiverSettingsCommand.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case ReceiverSettingsCommand.CloseWindow:
                Close();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(message), message.Command, null);
        }
    }

    private (bool Success, string Error) ApplyAndSaveDreamscapeSettings(
        RadialMenuSettings settings)
    {
        bool success = RadialMenuSettingsPersistence.TryApplyAndSave(
            _radialMenu,
            _radialSettingsStore,
            settings,
            ApplyRadialMenuSettings,
            out string error);
        if (success)
            LogRadialMessage("[环形菜单] Web Settings 设置已保存");
        return (success, error);
    }

    private void ShowNativeSettingsTab(int tabIndex)
    {
        _dreamscapeSettingsSession?.Deactivate();
        if (_dreamscapeSettingsHost != null)
            _dreamscapeSettingsHost.Visible = false;
        _settingsPage.Visible = true;
        if (_settingsPage.IsInitialized)
            _settingsPage.RefreshFromRuntime();
        TabControl? tabs = _settingsPage.Controls.Find("settingsTabs", true)
            .OfType<TabControl>()
            .FirstOrDefault();
        if (tabs != null && tabIndex >= 0 && tabIndex < tabs.TabCount)
            tabs.SelectedIndex = tabIndex;
    }

    private async void RunDreamscapeSettingsSmokeIfRequested()
    {
        const string reportVariable = "LEFTPAD_WEBVIEW2_SETTINGS_SMOKE_REPORT";
        string? reportPath = Environment.GetEnvironmentVariable(reportVariable);
        if (_dreamscapeSettingsSmokeStarted ||
            string.IsNullOrWhiteSpace(reportPath) ||
            _dreamscapeSettingsHost == null ||
            _dreamscapeSettingsSession == null)
        {
            return;
        }
        _dreamscapeSettingsSmokeStarted = true;

        RadialMenuSettings originalSettings = _radialMenu.ActiveSettings;
        bool originalFileExisted = File.Exists(_radialSettingsStore.Path);
        byte[]? originalFileBytes = originalFileExisted
            ? File.ReadAllBytes(_radialSettingsStore.Path)
            : null;
        bool settingsRestored = false;
        try
        {
            const int smokeReceiverUiScalePercent = 150;
            ApplyRadialMenuSettings(originalSettings with
            {
                ReceiverUiScalePercent = smokeReceiverUiScalePercent
            });
            NavigateTo(ReceiverPage.Settings);
            await Task.Delay(650);
            _dreamscapeSettingsHost.PostState();
            await Task.Delay(250);

            string? capturePath = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_SETTINGS_SMOKE_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capturePath))
                await _dreamscapeSettingsHost.CapturePreviewAsync(capturePath);
            string? before = await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadSettings.diagnostics())");

            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "document.getElementById('preview-button').click(); true");
            await Task.Delay(250);
            bool previewRoundTrip = _dreamscapeSettingsSession.IsPreviewActive &&
                _radialMenu.IsPreviewActive;
            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "document.getElementById('hide-preview-button').click(); true");
            await Task.Delay(200);
            bool hidePreviewRoundTrip = !_dreamscapeSettingsSession.IsPreviewActive &&
                !_radialMenu.IsPreviewActive;

            int testScale = originalSettings.ScalePercent < RadialMenuSettings.MaximumScalePercent
                ? originalSettings.ScalePercent + 1
                : originalSettings.ScalePercent - 1;
            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                $"window.leftpadSettings.postChange('overallSizePercent',{testScale}); true");
            await Task.Delay(250);
            bool draftChanged = _dreamscapeSettingsSession.Draft.ScalePercent == testScale &&
                _dreamscapeSettingsSession.IsDirty;

            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "document.querySelector('[data-command=\"showOverview\"]').click(); true");
            await Task.Delay(350);
            bool settingsToOverview = _currentPage == ReceiverPage.Overview;
            string? overviewCapture = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_SETTINGS_OVERVIEW_CAPTURE");
            if (!string.IsNullOrWhiteSpace(overviewCapture) &&
                _dreamscapeOverviewHost is { IsInitialized: true })
            {
                await _dreamscapeOverviewHost.CapturePreviewAsync(overviewCapture);
            }

            bool overviewToSettings = false;
            if (_dreamscapeOverviewHost is { IsInitialized: true })
            {
                await _dreamscapeOverviewHost.ExecuteScriptAsync(
                    "document.querySelector('[data-command=\"showSettings\"]').click(); true");
                await Task.Delay(350);
                overviewToSettings = _currentPage == ReceiverPage.Settings &&
                    _dreamscapeSettingsHost.Visible;
            }
            else
            {
                NavigateTo(ReceiverPage.Settings);
                overviewToSettings = _dreamscapeSettingsHost.Visible;
            }

            bool unsavedDiscarded =
                _dreamscapeSettingsSession.Draft.ScalePercent == originalSettings.ScalePercent &&
                _radialSettingsStore.Load().Settings.ScalePercent == originalSettings.ScalePercent;

            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                $"window.leftpadSettings.postChange('overallSizePercent',{testScale}); true");
            await Task.Delay(200);
            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "document.getElementById('apply-button').click(); true");
            await Task.Delay(400);
            bool applyRoundTrip = _radialMenu.ActiveSettings.ScalePercent == testScale;
            bool persistenceRoundTrip =
                _radialSettingsStore.Load().Settings.ScalePercent == testScale;

            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "document.getElementById('restore-button').click(); true");
            await Task.Delay(200);
            bool restoreDefaultDraft =
                _dreamscapeSettingsSession.Draft.ScalePercent == RadialMenuSettings.Default.ScalePercent;

            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "window.chrome.webview.postMessage({command:'settingsBasicChange',field:'notAllowed',value:1}); true");
            await Task.Delay(100);

            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "window.leftpadSettings.postCommand('showGamepad'); true");
            await Task.Delay(250);
            bool nativeGamepad = _currentPage == ReceiverPage.Gamepad && _gamepadMonitor.Visible;
            NavigateTo(ReceiverPage.Settings);
            await Task.Delay(200);
            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "window.leftpadSettings.postCommand('showLogs'); true");
            await Task.Delay(250);
            bool nativeLogs = _currentPage == ReceiverPage.Log && _logSection.Visible;

            NavigateTo(ReceiverPage.Settings);
            await Task.Delay(200);
            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "window.leftpadSettings.postCommand('showSettingsAdvanced'); true");
            await Task.Delay(200);
            bool nativeAdvanced = NativeSettingsTabIsSelected(1);
            NavigateTo(ReceiverPage.Settings);
            await Task.Delay(200);
            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "window.leftpadSettings.postCommand('showSettingsMappings'); true");
            await Task.Delay(200);
            bool nativeMappings = NativeSettingsTabIsSelected(2);

            NavigateTo(ReceiverPage.Settings);
            await Task.Delay(200);
            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "window.leftpadSettings.postCommand('minimize'); true");
            await Task.Delay(250);
            bool minimizeRoundTrip = WindowState == FormWindowState.Minimized;
            WindowState = FormWindowState.Normal;
            ShowMainForm();
            await Task.Delay(150);
            await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "document.querySelector('.window-button.close').click(); true");
            await Task.Delay(250);
            bool closeToTrayRoundTrip = !Visible && _notifyIcon is { Visible: true };
            ShowMainForm();
            await Task.Delay(150);

            string? after = await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadSettings.diagnostics())");
            int visibleTopLevelWindows = Application.OpenForms.Cast<Form>()
                .Count(form => form.TopLevel && form.Visible);

            ApplyRadialMenuSettings(originalSettings);
            if (originalFileExisted)
            {
                string? directory = Path.GetDirectoryName(_radialSettingsStore.Path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllBytes(_radialSettingsStore.Path, originalFileBytes!);
            }
            else if (File.Exists(_radialSettingsStore.Path))
            {
                File.Delete(_radialSettingsStore.Path);
            }
            settingsRestored = _radialMenu.ActiveSettings.Equals(originalSettings) &&
                File.Exists(_radialSettingsStore.Path) == originalFileExisted;

            string? reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                packageVersion = "1.0.4129.50",
                runtimeVersion = _dreamscapeSettingsHost.RuntimeVersion,
                receiverUiScalePercent = smokeReceiverUiScalePercent,
                designCanvasScale = "uniform min(viewport/reference), independent of ReceiverUiScale/DPI",
                capturePath,
                overviewCapture,
                before = DecodeScriptJson(before),
                after = DecodeScriptJson(after),
                previewRoundTrip,
                hidePreviewRoundTrip,
                draftChanged,
                unsavedDiscarded,
                applyRoundTrip,
                persistenceRoundTrip,
                restoreDefaultDraft,
                overviewToSettings,
                settingsToOverview,
                nativeGamepad,
                nativeLogs,
                nativeAdvanced,
                nativeMappings,
                minimizeRoundTrip,
                closeToTrayRoundTrip,
                visibleTopLevelWindows,
                settingsRestored,
                unknownFieldSent = true
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            if (Environment.GetEnvironmentVariable(
                    "LEFTPAD_WEBVIEW2_SETTINGS_SMOKE_AUTO_EXIT") == "1")
            {
                string? inputProbeReport = Environment.GetEnvironmentVariable(
                    "LEFTPAD_WEBVIEW2_SETTINGS_INPUT_PROBE_REPORT");
                int inputProbeDelayMs = int.TryParse(
                    Environment.GetEnvironmentVariable(
                        "LEFTPAD_WEBVIEW2_SETTINGS_INPUT_PROBE_DELAY_MS"),
                    out int parsedDelay)
                    ? Math.Clamp(parsedDelay, 0, 30000)
                    : 0;
                string? focusedRangeBounds = null;
                if (inputProbeDelayMs > 0)
                {
                    int focusDelay = Math.Min(1500, inputProbeDelayMs);
                    await Task.Delay(focusDelay);
                    focusedRangeBounds = await _dreamscapeSettingsHost.ExecuteScriptAsync(
                        "(() => { const e=document.getElementById('overall-size'); e.focus(); return JSON.stringify(e.getBoundingClientRect()); })()");
                    string? inputTargetReport = Environment.GetEnvironmentVariable(
                        "LEFTPAD_WEBVIEW2_SETTINGS_INPUT_TARGET_REPORT");
                    System.Text.Json.JsonElement? rangeBounds =
                        DecodeScriptJson(focusedRangeBounds);
                    if (!string.IsNullOrWhiteSpace(inputTargetReport) &&
                        rangeBounds is { ValueKind: System.Text.Json.JsonValueKind.Object } bounds)
                    {
                        double left = bounds.GetProperty("left").GetDouble();
                        double right = bounds.GetProperty("right").GetDouble();
                        double top = bounds.GetProperty("top").GetDouble();
                        double bottom = bounds.GetProperty("bottom").GetDouble();
                        Point rangeStart = PointToScreen(new Point(
                            (int)Math.Round(left + 45),
                            (int)Math.Round((top + bottom) / 2)));
                        Point rangeEnd = PointToScreen(new Point(
                            (int)Math.Round(right - 45),
                            (int)Math.Round((top + bottom) / 2)));
                        string? targetDirectory = Path.GetDirectoryName(inputTargetReport);
                        if (!string.IsNullOrEmpty(targetDirectory))
                            Directory.CreateDirectory(targetDirectory);
                        File.WriteAllText(inputTargetReport,
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                rangeStart = new { rangeStart.X, rangeStart.Y },
                                rangeEnd = new { rangeEnd.X, rangeEnd.Y }
                            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    await Task.Delay(inputProbeDelayMs - focusDelay);
                }
                if (!string.IsNullOrWhiteSpace(inputProbeReport))
                {
                    string? probeDiagnostics = await _dreamscapeSettingsHost.ExecuteScriptAsync(
                        "JSON.stringify(window.leftpadSettings.diagnostics())");
                    int externalInputDraftScalePercent =
                        _dreamscapeSettingsSession.Draft.ScalePercent;
                    bool externalInputDirty = _dreamscapeSettingsSession.IsDirty;
                    NavigateTo(ReceiverPage.Overview);
                    NavigateTo(ReceiverPage.Settings);
                    await Task.Delay(150);
                    string? probeDirectory = Path.GetDirectoryName(inputProbeReport);
                    if (!string.IsNullOrEmpty(probeDirectory))
                        Directory.CreateDirectory(probeDirectory);
                    File.WriteAllText(inputProbeReport,
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            diagnostics = DecodeScriptJson(probeDiagnostics),
                            focusedRangeBounds = DecodeScriptJson(focusedRangeBounds),
                            windowLocation = new { Location.X, Location.Y },
                            currentPage = _currentPage.ToString(),
                            externalInputDraftScalePercent,
                            externalInputDirty,
                            externalInputChangedDraft =
                                externalInputDraftScalePercent != originalSettings.ScalePercent,
                            unsavedExternalInputDiscarded =
                                _dreamscapeSettingsSession.Draft.ScalePercent ==
                                    originalSettings.ScalePercent &&
                                !_dreamscapeSettingsSession.IsDirty,
                            visibleTopLevelWindows = Application.OpenForms.Cast<Form>()
                                .Count(form => form.TopLevel && form.Visible)
                        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
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
            AppendLog($"[WebView2 Settings] Smoke failed: {exception.Message}");
        }
        finally
        {
            if (!settingsRestored)
            {
                ApplyRadialMenuSettings(originalSettings);
                if (originalFileExisted && originalFileBytes != null)
                    File.WriteAllBytes(_radialSettingsStore.Path, originalFileBytes);
                else if (!originalFileExisted && File.Exists(_radialSettingsStore.Path))
                    File.Delete(_radialSettingsStore.Path);
            }
        }
    }

    private bool NativeSettingsTabIsSelected(int expectedIndex)
    {
        TabControl? tabs = _settingsPage.Controls.Find("settingsTabs", true)
            .OfType<TabControl>()
            .FirstOrDefault();
        return _currentPage == ReceiverPage.Settings &&
            _settingsPage.Visible &&
            tabs?.SelectedIndex == expectedIndex;
    }
}
