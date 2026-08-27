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
            SynchronizeDreamscapePageActivation();
        };
        _dreamscapeOverviewHost.FrontendReady += RunDreamscapeSmokeReportIfRequested;
        DreamscapeShellMetrics.AttachHost(this, _dreamscapeOverviewHost, visible: true);
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

    internal ReceiverOverviewState CreateDreamscapeOverviewState()
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
            _service.OutputMode.ToString(),
            ReceiverOverviewCatalog.OutputModeOptions,
            ReceiverOverviewCatalog.KeyboardKeyOptions,
            stopped,
            ShouldEnableKeyboardMappings(_service.OutputMode, isRunning: !stopped),
            !stopped,
            stopped ? "启动" : "停止",
            mappings);
    }

    internal void HandleDreamscapeOverviewCommand(ReceiverOverviewCommandRequest request)
    {
        switch (request.Command)
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
            case ReceiverOverviewCommand.SetOutputMode:
                ApplyDreamscapeOutputMode(request);
                break;
            case ReceiverOverviewCommand.SetKeyboardMapping:
                ApplyDreamscapeKeyboardMapping(request);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Command, null);
        }
    }

    private void ApplyDreamscapeOutputMode(ReceiverOverviewCommandRequest request)
    {
        if (request.OutputMode is not OutputMode mode)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Spike] Rejected setOutputMode without a validated mode.");
            _dreamscapeOverviewHost?.PostState();
            return;
        }

        if (!_service.TrySetOutputMode(mode))
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Spike] Rejected output mode change while output is running.");
            UpdateOutputControls();
            _dreamscapeOverviewHost?.PostState();
            return;
        }

        _outputMode.SelectedItem = _service.OutputMode;
        UpdateOutputControls();
        _dreamscapeOverviewHost?.PostState();
    }

    private void ApplyDreamscapeKeyboardMapping(ReceiverOverviewCommandRequest request)
    {
        if (request.Action is not string action || request.Key is not KeyboardKey key)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Spike] Rejected setKeyboardMapping without validated payload.");
            _dreamscapeOverviewHost?.PostState();
            return;
        }

        if (_service.IsRunning || _service.OutputMode != OutputMode.Keyboard)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Spike] Rejected keyboard mapping change outside stopped Keyboard mode.");
            _dreamscapeOverviewHost?.PostState();
            return;
        }

        KeyboardBindings bindings = _service.KeyboardBindings;
        bindings.Set(action, key);
        if (!_service.TryUpdateKeyboardBindings(bindings, out string error))
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Overview] 键盘映射保存失败：{error}");
        }
        else if (_bindingEditors.TryGetValue(action, out ComboBox? editor))
        {
            _initializingBindingEditors = true;
            editor.SelectedItem = key;
            _initializingBindingEditors = false;
        }

        _dreamscapeOverviewHost?.PostState();
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
            string? editingDirectory = Environment.GetEnvironmentVariable(
                "LEFTPAD_WEBVIEW2_OVERVIEW_EDIT_SMOKE_DIR");
            if (!string.IsNullOrWhiteSpace(editingDirectory))
            {
                await RunDreamscapeOverviewEditingSmokeAsync(reportPath, editingDirectory);
                return;
            }

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
                "document.querySelector('.dreamscape-window-button.close').click(); true");
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

    private async Task RunDreamscapeOverviewEditingSmokeAsync(
        string reportPath,
        string captureDirectory)
    {
        if (_dreamscapeOverviewHost == null)
            return;

        Directory.CreateDirectory(captureDirectory);
        string outputDropdownCapture = Path.Combine(
            captureDirectory,
            "overview-output-mode-dropdown.png");
        string mappingDropdownCapture = Path.Combine(
            captureDirectory,
            "overview-keyboard-mapping-dropdown.png");
        string normalCapture = Path.Combine(
            captureDirectory,
            "overview-controls-normal.png");

        _service.Stop();
        OutputMode originalMode = _service.OutputMode;
        KeyboardBindings originalBindings = _service.KeyboardBindings;
        KeyboardKey temporaryKey = originalBindings.Cross == KeyboardKey.Enter
            ? KeyboardKey.Space
            : KeyboardKey.Enter;
        string? failure = null;
        System.Text.Json.JsonElement? directState = null;
        System.Text.Json.JsonElement? keyboardState = null;
        System.Text.Json.JsonElement? repeatedState = null;
        System.Text.Json.JsonElement? restoredState = null;
        bool outputModeRoundTrip = false;
        bool mappingRoundTrip = false;
        bool directMappingsDisabled = false;
        bool mappingPreserved = false;
        string? outputPickerResult = null;
        string? mappingPickerResult = null;

        try
        {
            _service.TrySetOutputMode(OutputMode.DirectDs4);
            _outputMode.SelectedItem = _service.OutputMode;
            UpdateOutputControls();
            _dreamscapeOverviewHost.PostState();
            await Task.Delay(300);

            directState = DecodeScriptJson(await CaptureDreamscapeControlStateAsync());
            ShowMainForm();
            TopMost = true;
            Activate();
            BringToFront();
            await Task.Delay(150);
            bool outputWebViewFocused = await _dreamscapeOverviewHost.FocusSelectAsync("#output-mode");
            SendKeys.SendWait("%{DOWN}");
            outputPickerResult = $"WebViewFocus={outputWebViewFocused}; input=Alt+Down";
            await Task.Delay(150);
            CaptureDreamscapeScreen(outputDropdownCapture);
            SendKeys.SendWait("{ESC}");
            TopMost = false;
            await Task.Delay(150);

            await DispatchDreamscapeSelectChangeAsync("#output-mode", "Keyboard");
            await Task.Delay(400);
            _dreamscapeOverviewHost.PostState();
            await Task.Delay(200);
            outputModeRoundTrip = _service.OutputMode == OutputMode.Keyboard;
            keyboardState = DecodeScriptJson(await CaptureDreamscapeControlStateAsync());

            await _dreamscapeOverviewHost.ExecuteScriptAsync(
                "document.querySelector('[data-mapping=\"CR\"]').focus(); true");
            _dreamscapeOverviewHost.PostState();
            _dreamscapeOverviewHost.PostState();
            await Task.Delay(200);
            repeatedState = DecodeScriptJson(await CaptureDreamscapeControlStateAsync());

            ShowMainForm();
            TopMost = true;
            Activate();
            BringToFront();
            await Task.Delay(150);
            bool mappingWebViewFocused = await _dreamscapeOverviewHost.FocusSelectAsync(
                "[data-mapping=\"CR\"]");
            SendKeys.SendWait("%{DOWN}");
            mappingPickerResult = $"WebViewFocus={mappingWebViewFocused}; input=Alt+Down";
            await Task.Delay(150);
            CaptureDreamscapeScreen(mappingDropdownCapture);
            SendKeys.SendWait("{ESC}");
            TopMost = false;
            await Task.Delay(150);

            await DispatchDreamscapeSelectChangeAsync(
                "[data-mapping=\"CR\"]",
                temporaryKey.ToString());
            await Task.Delay(350);
            _dreamscapeOverviewHost.PostState();
            await Task.Delay(150);
            mappingRoundTrip = _service.KeyboardBindings.Cross == temporaryKey;

            await DispatchDreamscapeSelectChangeAsync("#output-mode", "DirectDs4");
            await Task.Delay(350);
            directMappingsDisabled = _service.OutputMode == OutputMode.DirectDs4 &&
                DecodeScriptJson(await CaptureDreamscapeControlStateAsync()) is { } directAfterEdit &&
                directAfterEdit.GetProperty("mappingsDisabled").GetBoolean();

            await DispatchDreamscapeSelectChangeAsync("#output-mode", "Keyboard");
            await Task.Delay(350);
            mappingPreserved = _service.OutputMode == OutputMode.Keyboard &&
                _service.KeyboardBindings.Cross == temporaryKey;
        }
        catch (Exception exception)
        {
            failure = exception.ToString();
        }
        finally
        {
            if (_service.IsRunning)
                _service.Stop();
            TopMost = false;
            bool bindingsRestored = _service.TryUpdateKeyboardBindings(originalBindings);
            bool modeRestored = _service.TrySetOutputMode(originalMode);
            _outputMode.SelectedItem = _service.OutputMode;
            UpdateOutputControls();
            _dreamscapeOverviewHost.PostState();
            await Task.Delay(300);
            restoredState = DecodeScriptJson(await CaptureDreamscapeControlStateAsync());
            await _dreamscapeOverviewHost.CapturePreviewAsync(normalCapture);

            string? reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                package = "Microsoft.Web.WebView2",
                packageVersion = "1.0.4129.50",
                runtimeVersion = _dreamscapeOverviewHost.RuntimeVersion,
                initialization = "ready",
                failure,
                directState,
                keyboardState,
                repeatedState,
                outputModeRoundTrip,
                mappingRoundTrip,
                directMappingsDisabled,
                mappingPreserved,
                outputPickerResult,
                mappingPickerResult,
                temporaryKey = temporaryKey.ToString(),
                formalKeyboardKeyCatalogCount = Enum.GetValues<KeyboardKey>().Length,
                bindingsRestored,
                modeRestored,
                restoredState,
                captures = new
                {
                    outputDropdownCapture,
                    mappingDropdownCapture,
                    normalCapture
                }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [WebView2 Spike] Overview editing smoke report: {reportPath}");

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
    }

    private async Task DispatchDreamscapeSelectChangeAsync(string selector, string value)
    {
        if (_dreamscapeOverviewHost == null)
            return;
        string selectorJson = System.Text.Json.JsonSerializer.Serialize(selector);
        string valueJson = System.Text.Json.JsonSerializer.Serialize(value);
        await _dreamscapeOverviewHost.ExecuteScriptAsync(
            $"(() => {{ const select = document.querySelector({selectorJson}); " +
            $"select.value = {valueJson}; " +
            "select.dispatchEvent(new Event('change', { bubbles: true })); return true; })()");
    }

    private async Task<string?> CaptureDreamscapeControlStateAsync()
    {
        if (_dreamscapeOverviewHost == null)
            return null;
        return await _dreamscapeOverviewHost.ExecuteScriptAsync(
            "JSON.stringify((() => { " +
            "const output = document.getElementById('output-mode'); " +
            "const mappings = Array.from(document.querySelectorAll('[data-mapping]')); " +
            "const cross = document.querySelector('[data-mapping=\"CR\"]'); " +
            "return { " +
            "outputValue: output.value, outputDisabled: output.disabled, " +
            "outputOptions: Array.from(output.options).map(o => ({value:o.value,label:o.textContent})), " +
            "mappingCount: mappings.length, mappingOptionCount: cross.options.length, " +
            "mappingValue: cross.value, mappingsDisabled: mappings.every(s => s.disabled), " +
            "mappingsEnabled: mappings.every(s => !s.disabled), " +
            "focusedMapping: document.activeElement === cross, " +
            "mappingCatalogSignature: cross.dataset.catalogSignature " +
            "}; })())");
    }

    private void CaptureDreamscapeScreen(string path)
    {
        Rectangle bounds = Screen.FromControl(this).Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
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
