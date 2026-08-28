param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$SmokeReport,
    [Parameter(Mandatory = $true)][string]$ApplicationProbeReport,
    [Parameter(Mandatory = $true)][string]$InputTargetReport,
    [Parameter(Mandatory = $true)][string]$ExternalProbeReport
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LeftPadSettingsProbeNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
'@

# Match the Receiver's DPI-aware screen coordinate space before reading its
# window rect or injecting physical pointer positions.
[LeftPadSettingsProbeNative]::SetProcessDPIAware() | Out-Null

function Get-ReceiverRect([IntPtr]$Handle) {
    $rect = New-Object LeftPadSettingsProbeNative+RECT
    if (-not [LeftPadSettingsProbeNative]::GetWindowRect($Handle, [ref]$rect)) {
        throw 'GetWindowRect failed.'
    }
    return $rect
}

function Get-DesignPoint(
    $Rect,
    [double]$DesignX,
    [double]$DesignY,
    [double]$ViewportWidth,
    [double]$ViewportHeight
) {
    $scale = [Math]::Min($ViewportWidth / 1672.0, $ViewportHeight / 941.0)
    $originX = ($ViewportWidth - 1672.0 * $scale) / 2.0
    $originY = ($ViewportHeight - 941.0 * $scale) / 2.0
    return @(
        [int][Math]::Round($Rect.Left + $originX + $DesignX * $scale),
        [int][Math]::Round($Rect.Top + $originY + $DesignY * $scale)
    )
}

function Invoke-MouseClick([int]$X, [int]$Y) {
    [LeftPadSettingsProbeNative]::SetCursorPos($X, $Y) | Out-Null
    Start-Sleep -Milliseconds 80
    [LeftPadSettingsProbeNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [LeftPadSettingsProbeNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Invoke-Key([byte]$VirtualKey) {
    [LeftPadSettingsProbeNative]::keybd_event($VirtualKey, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [LeftPadSettingsProbeNative]::keybd_event($VirtualKey, 0, 0x0002, [UIntPtr]::Zero)
}

$settingsPath = Join-Path $env:LOCALAPPDATA 'LeftPad\radial-menu-settings.json'
$settingsExistedBefore = Test-Path -LiteralPath $settingsPath
$settingsShaBefore = if ($settingsExistedBefore) {
    (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
} else { $null }

$env:LEFTPAD_WEBVIEW2_OVERVIEW = '1'
$env:LEFTPAD_WEBVIEW2_SETTINGS = '1'
$env:LEFTPAD_WEBVIEW2_SETTINGS_SMOKE_REPORT = $SmokeReport
$env:LEFTPAD_WEBVIEW2_SETTINGS_INPUT_PROBE_REPORT = $ApplicationProbeReport
$env:LEFTPAD_WEBVIEW2_SETTINGS_INPUT_TARGET_REPORT = $InputTargetReport
$env:LEFTPAD_WEBVIEW2_SETTINGS_INPUT_PROBE_DELAY_MS = '12000'
$env:LEFTPAD_WEBVIEW2_SETTINGS_SMOKE_AUTO_EXIT = '1'

$process = Start-Process -FilePath $Executable -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(20)
while (-not (Test-Path -LiteralPath $SmokeReport)) {
    if ($process.HasExited) { throw "Receiver exited before the smoke report (exit $($process.ExitCode))." }
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for the smoke report.' }
    Start-Sleep -Milliseconds 150
}
$smoke = Get-Content -LiteralPath $SmokeReport -Raw | ConvertFrom-Json
$viewportWidth = [double]$smoke.before.viewport.width
$viewportHeight = [double]$smoke.before.viewport.height

$process.Refresh()
while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 100
    $process.Refresh()
}
if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Receiver main window was not found.' }

$handle = $process.MainWindowHandle
[LeftPadSettingsProbeNative]::SetForegroundWindow($handle) | Out-Null
$before = Get-ReceiverRect $handle
$dragStart = Get-DesignPoint $before 1000 35 $viewportWidth $viewportHeight
[LeftPadSettingsProbeNative]::SetCursorPos($dragStart[0], $dragStart[1]) | Out-Null
[LeftPadSettingsProbeNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
for ($step = 1; $step -le 10; $step++) {
    $x = $dragStart[0] + [int](12 * $step)
    $y = $dragStart[1] + [int](7 * $step)
    [LeftPadSettingsProbeNative]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 35
}
[LeftPadSettingsProbeNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 350
$after = Get-ReceiverRect $handle

# The app focuses the real range and publishes screen coordinates calculated
# by MainForm.PointToScreen, avoiding cross-process DPI virtualization.
while (-not (Test-Path -LiteralPath $InputTargetReport)) {
    if ($process.HasExited) { throw 'Receiver exited before publishing range coordinates.' }
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for range coordinates.' }
    Start-Sleep -Milliseconds 100
}
$inputTarget = Get-Content -LiteralPath $InputTargetReport -Raw | ConvertFrom-Json
Invoke-Key 0x23
Invoke-Key 0x24
Invoke-Key 0x27
Invoke-Key 0x25
[LeftPadSettingsProbeNative]::SetCursorPos(
    [int]$inputTarget.rangeStart.X,
    [int]$inputTarget.rangeStart.Y) | Out-Null
[LeftPadSettingsProbeNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
for ($step = 1; $step -le 10; $step++) {
    $x = [int]$inputTarget.rangeStart.X + [int](
        ([int]$inputTarget.rangeEnd.X - [int]$inputTarget.rangeStart.X) * $step / 10)
    $y = [int]$inputTarget.rangeStart.Y + [int](
        ([int]$inputTarget.rangeEnd.Y - [int]$inputTarget.rangeStart.Y) * $step / 10)
    [LeftPadSettingsProbeNative]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 35
}
[LeftPadSettingsProbeNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 350

$process.WaitForExit(25000) | Out-Null
if (-not $process.HasExited) { throw 'Receiver did not complete its requested clean exit.' }

$settingsExistedAfter = Test-Path -LiteralPath $settingsPath
$settingsShaAfter = if ($settingsExistedAfter) {
    (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
} else { $null }
$report = [ordered]@{
    before = [ordered]@{ left = $before.Left; top = $before.Top; right = $before.Right; bottom = $before.Bottom }
    after = [ordered]@{ left = $after.Left; top = $after.Top; right = $after.Right; bottom = $after.Bottom }
    delta = [ordered]@{ x = $after.Left - $before.Left; y = $after.Top - $before.Top }
    dragMoved = ($after.Left -ne $before.Left -or $after.Top -ne $before.Top)
    rangeMouseDragSent = $true
    rangeStart = $inputTarget.rangeStart
    rangeEnd = $inputTarget.rangeEnd
    rangeKeyboardKeys = @('End', 'Home', 'Right', 'Left')
    settingsFileExistedBefore = $settingsExistedBefore
    settingsFileExistedAfter = $settingsExistedAfter
    settingsShaBefore = $settingsShaBefore
    settingsShaAfter = $settingsShaAfter
    settingsFileRestored = ($settingsExistedBefore -eq $settingsExistedAfter -and $settingsShaBefore -eq $settingsShaAfter)
    exitCode = $process.ExitCode
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ExternalProbeReport -Encoding utf8
$report | ConvertTo-Json -Depth 5
