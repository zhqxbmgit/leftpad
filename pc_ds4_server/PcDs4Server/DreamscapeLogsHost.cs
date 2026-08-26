using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PcDs4Server;

internal sealed class DreamscapeLogsHost : UserControl, IDreamscapeShellHost
{
    private const string VirtualHost = "leftpad-logs.local";
    private readonly WebView2 _webView;
    private readonly Func<ReceiverLogSnapshot> _snapshotProvider;
    private readonly Action<ReceiverLogsCommand> _commandHandler;
    private readonly Action<string> _log;
    private readonly string _assetDirectory;
    private bool _initializationStarted;
    private bool _frontendReady;

    public DreamscapeLogsHost(
        Func<ReceiverLogSnapshot> snapshotProvider,
        Action<ReceiverLogsCommand> commandHandler,
        Action<string> log,
        string? assetDirectory = null)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _assetDirectory = assetDirectory ?? DreamscapeLogsFeature.AssetDirectory;
        Name = "dreamscapeLogsHost";
        AccessibleName = "Dreamscape WebView2 Logs";
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(39, 58, 120);

        _webView = new WebView2
        {
            Name = "dreamscapeLogsWebView",
            AccessibleName = "Dreamscape Logs Web Content",
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(39, 58, 120)
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
        "static-art.png"
    ];

    public static bool ValidateAssets(string assetDirectory, out string error)
    {
        foreach (string file in RequiredFrontendFiles)
        {
            string path = Path.Combine(assetDirectory, file);
            if (!File.Exists(path))
            {
                error = $"Dreamscape Logs asset is missing: {path}";
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
            _log($"[WebView2 Logs] {assetError}");
            InitializationFailed?.Invoke(assetError);
            return;
        }
        if (!DreamscapeShellMetrics.ValidateAssets(out assetError))
        {
            _log($"[WebView2 Logs] {assetError}");
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
            _log($"[WebView2 Logs] Runtime {RuntimeVersion} initialized.");
        }
        catch (Exception exception)
        {
            string message = $"WebView2 Logs initialization failed: {exception.Message}";
            _log($"[WebView2 Logs] {message}");
            InitializationFailed?.Invoke(message);
        }
    }

    public event Action<string>? InitializationFailed;
    public event Action? FrontendReady;

    public void PostSnapshot()
    {
        if (!_frontendReady || _webView.CoreWebView2 == null)
            return;
        _webView.CoreWebView2.PostWebMessageAsJson(_snapshotProvider().ToJson());
    }

    public void PostAppend(ReceiverLogEntry entry)
    {
        if (!CanPushIncremental(_frontendReady, Visible) || _webView.CoreWebView2 == null)
            return;
        _webView.CoreWebView2.PostWebMessageAsJson(
            new ReceiverLogAppend(ReceiverLogAppend.MessageType, entry).ToJson());
    }

    public void PostConnectionState(string connectionState)
    {
        if (!CanPushIncremental(_frontendReady, Visible) || _webView.CoreWebView2 == null)
            return;
        _webView.CoreWebView2.PostWebMessageAsJson(
            new ReceiverLogConnectionState(
                ReceiverLogConnectionState.MessageType,
                connectionState).ToJson());
    }

    internal static bool CanPushIncremental(bool frontendReady, bool visible) =>
        frontendReady && visible;

    public async Task<string?> ExecuteScriptAsync(string script)
    {
        if (_webView.CoreWebView2 == null)
            return null;
        return await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    public async Task CapturePreviewAsync(string path)
    {
        if (_webView.CoreWebView2 == null)
            throw new InvalidOperationException("WebView2 Logs is not initialized.");
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
            string message = $"Logs frontend navigation failed: {eventArgs.WebErrorStatus}.";
            _log($"[WebView2 Logs] {message}");
            InitializationFailed?.Invoke(message);
            return;
        }

        await DreamscapeShellMetrics.PublishHostMetricsAsync(_webView);
        _frontendReady = true;
        PostSnapshot();
        FrontendReady?.Invoke();
        _log("[WebView2 Logs] Frontend ready; initial log snapshot posted.");
    }

    private void HandleWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!ReceiverLogsCommandAllowList.TryParse(
            eventArgs.WebMessageAsJson,
            out ReceiverLogsCommand command,
            out string rejectionReason))
        {
            _log($"[WebView2 Logs] Rejected command: {rejectionReason}");
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
