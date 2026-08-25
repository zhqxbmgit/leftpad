using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PcDs4Server;

internal sealed class DreamscapeControllerHost : UserControl
{
    private const string VirtualHost = "leftpad-controller.local";
    private readonly WebView2 _webView;
    private readonly Func<ReceiverControllerState> _stateProvider;
    private readonly Action<ReceiverControllerCommand> _commandHandler;
    private readonly Action<string> _log;
    private readonly string _assetDirectory;
    private bool _initializationStarted;
    private bool _frontendReady;
    private string? _lastPostedJson;

    public DreamscapeControllerHost(
        Func<ReceiverControllerState> stateProvider,
        Action<ReceiverControllerCommand> commandHandler,
        Action<string> log,
        string? assetDirectory = null)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _assetDirectory = assetDirectory ?? DreamscapeControllerFeature.AssetDirectory;
        Name = "dreamscapeControllerHost";
        AccessibleName = "Dreamscape WebView2 Controller Status";
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(39, 58, 120);

        _webView = new WebView2
        {
            Name = "dreamscapeControllerWebView",
            AccessibleName = "Dreamscape Controller Status Web Content",
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
                error = $"Dreamscape Controller asset is missing: {path}";
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
            _log($"[WebView2 Controller] {assetError}");
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
            _log($"[WebView2 Controller] Runtime {RuntimeVersion} initialized.");
        }
        catch (Exception exception)
        {
            string message = $"WebView2 Controller initialization failed: {exception.Message}";
            _log($"[WebView2 Controller] {message}");
            InitializationFailed?.Invoke(message);
        }
    }

    public event Action<string>? InitializationFailed;
    public event Action? FrontendReady;

    public void PostState() => PostState(_stateProvider(), force: true);

    public void PostStateIfChanged()
    {
        if (!Visible)
            return;
        PostState(_stateProvider(), force: false);
    }

    internal void PostState(ReceiverControllerState state) => PostState(state, force: true);

    public async Task<string?> ExecuteScriptAsync(string script)
    {
        if (_webView.CoreWebView2 == null)
            return null;
        return await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    public async Task CapturePreviewAsync(string path)
    {
        if (_webView.CoreWebView2 == null)
            throw new InvalidOperationException("WebView2 Controller is not initialized.");
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await _webView.CoreWebView2.CapturePreviewAsync(
            CoreWebView2CapturePreviewImageFormat.Png,
            stream);
    }

    private void PostState(ReceiverControllerState state, bool force)
    {
        if (!_frontendReady || _webView.CoreWebView2 == null)
            return;
        string json = state.ToJson();
        if (!force && string.Equals(json, _lastPostedJson, StringComparison.Ordinal))
            return;
        _lastPostedJson = json;
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void HandleNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
            string message = $"Controller frontend navigation failed: {eventArgs.WebErrorStatus}.";
            _log($"[WebView2 Controller] {message}");
            InitializationFailed?.Invoke(message);
            return;
        }

        _frontendReady = true;
        PostState();
        FrontendReady?.Invoke();
        _log("[WebView2 Controller] Frontend ready; C# state posted.");
    }

    private void HandleWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!ReceiverControllerCommandAllowList.TryParse(
            eventArgs.WebMessageAsJson,
            out ReceiverControllerCommand command,
            out string rejectionReason))
        {
            _log($"[WebView2 Controller] Rejected command: {rejectionReason}");
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
