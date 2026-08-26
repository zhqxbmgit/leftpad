namespace PcDs4Server;

public partial class MainForm
{
    private bool _dreamscapeShellQuickSmokeStarted;

    private async void RunDreamscapeShellQuickSmokeIfRequested()
    {
        string? reportPath = Environment.GetEnvironmentVariable(
            "LEFTPAD_DREAMSCAPE_SHELL_QUICK_REPORT");
        if (string.IsNullOrWhiteSpace(reportPath) || _dreamscapeShellQuickSmokeStarted)
            return;

        _dreamscapeShellQuickSmokeStarted = true;
        RadialMenuSettings originalSettings = _radialMenu.ActiveSettings;
        int requestedScale = int.TryParse(
            Environment.GetEnvironmentVariable("LEFTPAD_DREAMSCAPE_SHELL_SCALE"),
            out int parsedScale) && ReceiverUiScaling.IsPreset(parsedScale)
                ? parsedScale
                : originalSettings.ReceiverUiScalePercent;

        try
        {
            if (requestedScale != originalSettings.ReceiverUiScalePercent)
            {
                ApplyRadialMenuSettings(originalSettings with
                {
                    ReceiverUiScalePercent = requestedScale
                });
            }

            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (_dreamscapeOverviewHost is { IsInitialized: true } &&
                    _dreamscapeControllerHost is { IsInitialized: true } &&
                    _dreamscapeSettingsHost is { IsInitialized: true } &&
                    _dreamscapeLogsHost is { IsInitialized: true })
                {
                    break;
                }
                await Task.Delay(100);
            }

            if (_dreamscapeOverviewHost is not { IsInitialized: true } ||
                _dreamscapeControllerHost is not { IsInitialized: true } ||
                _dreamscapeSettingsHost is not { IsInitialized: true } ||
                _dreamscapeLogsHost is not { IsInitialized: true })
            {
                throw new InvalidOperationException("Not all Dreamscape hosts initialized.");
            }

            NavigateTo(ReceiverPage.Overview);
            await Task.Delay(200);
            string? overview = await _dreamscapeOverviewHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadSpike.diagnostics().shell)");

            NavigateTo(ReceiverPage.Gamepad);
            await Task.Delay(200);
            string? controller = await _dreamscapeControllerHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadController.diagnostics().shell)");

            NavigateTo(ReceiverPage.Settings);
            await Task.Delay(200);
            string? settings = await _dreamscapeSettingsHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadSettings.diagnostics().shell)");

            NavigateTo(ReceiverPage.Log);
            await Task.Delay(200);
            string? logs = await _dreamscapeLogsHost.ExecuteScriptAsync(
                "JSON.stringify(window.leftpadLogs.diagnostics().shell)");

            string? directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                receiverUiScalePercent = requestedScale,
                clientSize = new { width = ClientSize.Width, height = ClientSize.Height },
                overview = DecodeScriptJson(overview),
                controller = DecodeScriptJson(controller),
                settings = DecodeScriptJson(settings),
                logs = DecodeScriptJson(logs)
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            if (Environment.GetEnvironmentVariable(
                "LEFTPAD_DREAMSCAPE_SHELL_QUICK_AUTO_EXIT") == "1")
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
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [Dreamscape Shell] Quick smoke failed: {exception.Message}");
        }
        finally
        {
            if (!IsDisposed && requestedScale != originalSettings.ReceiverUiScalePercent)
                ApplyRadialMenuSettings(originalSettings);
        }
    }
}
