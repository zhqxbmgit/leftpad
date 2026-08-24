using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PcDs4Server;

internal sealed class DreamscapeSettingsHost : UserControl
{
    private const string VirtualHost = "leftpad-settings.local";
    private readonly WebView2 _webView;
    private readonly Func<ReceiverSettingsBasicState> _stateProvider;
    private readonly Action<ReceiverSettingsMessage> _messageHandler;
    private readonly Action<string> _log;
    private readonly string _assetDirectory;
    private bool _initializationStarted;

    public DreamscapeSettingsHost(
        Func<ReceiverSettingsBasicState> stateProvider,
        Action<ReceiverSettingsMessage> messageHandler,
        Action<string> log,
        string? assetDirectory = null)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _assetDirectory = assetDirectory ?? DreamscapeSettingsFeature.AssetDirectory;
        Name = "dreamscapeSettingsHost";
        AccessibleName = "Dreamscape WebView2 Settings Basic";
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(35, 48, 105);

        _webView = new WebView2
        {
            Name = "dreamscapeSettingsWebView",
            AccessibleName = "Dreamscape Settings Basic Web Content",
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
        "static-art.png"
    ];

    public static bool ValidateAssets(string assetDirectory, out string error)
    {
        foreach (string file in RequiredFrontendFiles)
        {
            string path = Path.Combine(assetDirectory, file);
            if (!File.Exists(path))
            {
                error = $"Dreamscape Settings asset is missing: {path}";
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
            _log($"[WebView2 Settings] {assetError}");
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
            core.WebMessageReceived += HandleWebMessageReceived;
            core.NavigationCompleted += HandleNavigationCompleted;
            core.Navigate($"https://{VirtualHost}/index.html");
            _log($"[WebView2 Settings] Runtime {RuntimeVersion} initialized.");
        }
        catch (Exception exception)
        {
            string message = $"WebView2 Settings initialization failed: {exception.Message}";
            _log($"[WebView2 Settings] {message}");
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

    public async Task CapturePreviewAsync(string path)
    {
        if (_webView.CoreWebView2 == null)
            throw new InvalidOperationException("WebView2 Settings is not initialized.");
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await _webView.CoreWebView2.CapturePreviewAsync(
            CoreWebView2CapturePreviewImageFormat.Png,
            stream);
    }

    private void HandleNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
            string message = $"Settings frontend navigation failed: {eventArgs.WebErrorStatus}.";
            _log($"[WebView2 Settings] {message}");
            InitializationFailed?.Invoke(message);
            return;
        }
        PostState();
        FrontendReady?.Invoke();
        _log("[WebView2 Settings] Frontend ready; C# state posted.");
    }

    private void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!ReceiverSettingsCommandAllowList.TryParse(
            eventArgs.WebMessageAsJson,
            out ReceiverSettingsMessage message,
            out string rejectionReason))
        {
            _log($"[WebView2 Settings] Rejected command: {rejectionReason}");
            return;
        }
        _messageHandler(message);
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
