using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PcDs4Server;

internal sealed record DreamscapeShellRect(int X, int Y, int Width, int Height);

internal interface IDreamscapeShellHost
{
}

internal static class DreamscapeShellMetrics
{
    public const int DesignWidth = 1672;
    public const int DesignHeight = 941;
    public const string VirtualHost = "leftpad-shared.local";

    public static Size DesignSize => new(DesignWidth, DesignHeight);
    public static DreamscapeShellRect Sidebar { get; } = new(0, 0, 307, 941);
    public static DreamscapeShellRect Brand { get; } = new(63, 169, 214, 82);
    public static IReadOnlyDictionary<string, DreamscapeShellRect> Navigation { get; } =
        new Dictionary<string, DreamscapeShellRect>(StringComparer.Ordinal)
        {
            ["overview"] = new(15, 300, 276, 72),
            ["controller"] = new(15, 382, 276, 72),
            ["settings"] = new(15, 464, 276, 72),
            ["logs"] = new(15, 546, 276, 72)
        };
    public static IReadOnlyDictionary<string, DreamscapeShellRect> WindowButtons { get; } =
        new Dictionary<string, DreamscapeShellRect>(StringComparer.Ordinal)
        {
            ["minimize"] = new(1532, 16, 52, 52),
            ["close"] = new(1592, 16, 52, 52)
        };

    public static string AssetDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "DreamscapeShared");

    public static IReadOnlyList<string> RequiredFrontendFiles { get; } =
    [
        "shell.css",
        "shell.js",
        "official-prism.png"
    ];

    public static bool ValidateAssets(out string error)
    {
        foreach (string file in RequiredFrontendFiles)
        {
            string path = Path.Combine(AssetDirectory, file);
            if (!File.Exists(path))
            {
                error = $"Dreamscape shared shell asset is missing: {path}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static void MapAssets(CoreWebView2 core) =>
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            AssetDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

    public static void AttachHost(Control parent, Control host, bool visible)
    {
        host.Dock = DockStyle.None;
        host.Anchor = AnchorStyles.None;
        host.Visible = visible;
        parent.Controls.Add(host);

        void SynchronizeBounds(object? sender, EventArgs eventArgs)
        {
            if (!host.IsDisposed)
                host.Bounds = parent.ClientRectangle;
        }

        parent.ClientSizeChanged += SynchronizeBounds;
        parent.Layout += SynchronizeBounds;
        SynchronizeBounds(parent, EventArgs.Empty);
        host.Disposed += (_, _) =>
        {
            parent.ClientSizeChanged -= SynchronizeBounds;
            parent.Layout -= SynchronizeBounds;
        };
    }

    public static async Task PublishHostMetricsAsync(WebView2 webView)
    {
        string zoomFactor = webView.ZoomFactor.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        await webView.ExecuteScriptAsync(
            $"window.leftpadShell?.setHostMetrics({{zoomFactor:{zoomFactor}}}); true");
    }
}
