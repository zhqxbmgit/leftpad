using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PcDs4Server;

internal sealed class DreamscapeOverviewHost : UserControl, IDreamscapeShellHost
{
    private const string VirtualHost = "leftpad-overview.local";
    private readonly WebView2 _webView;
    private readonly Func<ReceiverOverviewState> _stateProvider;
    private readonly Action<ReceiverOverviewCommandRequest> _commandHandler;
    private readonly Action<string> _log;
    private readonly string _assetDirectory;
    private bool _initializationStarted;

    public DreamscapeOverviewHost(
        Func<ReceiverOverviewState> stateProvider,
        Action<ReceiverOverviewCommandRequest> commandHandler,
        Action<string> log,
        string? assetDirectory = null)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _assetDirectory = assetDirectory ?? DreamscapeOverviewFeature.AssetDirectory;
        Name = "dreamscapeOverviewHost";
        AccessibleName = "Dreamscape WebView2 Overview";
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(35, 48, 105);

        _webView = new WebView2
        {
            Name = "dreamscapeOverviewWebView",
            AccessibleName = "Dreamscape Overview Web Content",
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(35, 48, 105)
        };
        Controls.Add(_webView);
    }

    public bool IsInitialized => _webView.CoreWebView2 != null;
    public string RuntimeVersion { get; private set; } = "not initialized";

    public static IReadOnlyList<string> RequiredFrontendFiles { get; } =
    [
        "index.html",
        "styles.css",
        "app.js",
        "static-art.png",
        "button-stop.svg"
    ];

    public static bool ValidateAssets(string assetDirectory, out string error)
    {
        foreach (string file in RequiredFrontendFiles)
        {
            string path = Path.Combine(assetDirectory, file);
            if (!File.Exists(path))
            {
                error = $"Dreamscape frontend asset is missing: {path}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public async void InitializeAsync()
    {
        if (_initializationStarted || IsDisposed)
            return;
        _initializationStarted = true;

        if (!ValidateAssets(_assetDirectory, out string assetError))
        {
            _log($"[WebView2 Spike] {assetError}");
            InitializationFailed?.Invoke(assetError);
            return;
        }
        if (!DreamscapeShellMetrics.ValidateAssets(out assetError))
        {
            _log($"[WebView2 Spike] {assetError}");
            InitializationFailed?.Invoke(assetError);
            return;
        }

        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeftPad",
                "WebView2Spike");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);
            RuntimeVersion = environment.BrowserVersionString;
            await _webView.EnsureCoreWebView2Async(environment);
            CoreWebView2 core = _webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                _assetDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            DreamscapeShellMetrics.MapAssets(core);
            core.WebMessageReceived += HandleWebMessageReceived;
            core.NavigationCompleted += HandleNavigationCompleted;
            core.Navigate($"https://{VirtualHost}/index.html");
            _log($"[WebView2 Spike] Runtime {RuntimeVersion} initialized.");
        }
        catch (Exception exception)
        {
            string message = $"WebView2 initialization failed: {exception.Message}";
            _log($"[WebView2 Spike] {message}");
            InitializationFailed?.Invoke(message);
        }
    }

    public event Action<string>? InitializationFailed;
    public event Action? FrontendReady;

    public void PostState()
    {
        if (_webView.CoreWebView2 == null)
            return;

        _webView.CoreWebView2.PostWebMessageAsJson(_stateProvider().ToJson());
    }

    public async Task<string?> ExecuteScriptAsync(string script)
    {
        if (_webView.CoreWebView2 == null)
            return null;
        return await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    public async Task<bool> FocusSelectAsync(string selector)
    {
        if (_webView.CoreWebView2 == null)
            return false;
        SetFocus(_webView.Handle);
        bool webViewFocused = GetFocus() != IntPtr.Zero;
        string selectorJson = System.Text.Json.JsonSerializer.Serialize(selector);
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"document.querySelector({selectorJson}).focus(); true");
        return webViewFocused;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr windowHandle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    public async Task CapturePreviewAsync(string path)
    {
        if (_webView.CoreWebView2 == null)
            throw new InvalidOperationException("WebView2 is not initialized.");
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await _webView.CoreWebView2.CapturePreviewAsync(
            CoreWebView2CapturePreviewImageFormat.Png,
            stream);
    }

    private async void HandleNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
            string message = $"Frontend navigation failed: {eventArgs.WebErrorStatus}.";
            _log($"[WebView2 Spike] {message}");
            InitializationFailed?.Invoke(message);
            return;
        }

        await DreamscapeShellMetrics.PublishHostMetricsAsync(_webView);
        PostState();
        FrontendReady?.Invoke();
        _log("[WebView2 Spike] Frontend ready; initial C# state posted.");
    }

    private void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        string json = eventArgs.WebMessageAsJson;
        if (!ReceiverOverviewCommandAllowList.TryParse(
            json,
            out ReceiverOverviewCommandRequest command,
            out string rejectionReason))
        {
            _log($"[WebView2 Spike] Rejected command: {rejectionReason}");
            return;
        }

        _commandHandler(command);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.WebMessageReceived -= HandleWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted -= HandleNavigationCompleted;
        }
        base.Dispose(disposing);
    }
}
