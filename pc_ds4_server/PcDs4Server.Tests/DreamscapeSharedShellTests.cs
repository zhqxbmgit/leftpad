using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class DreamscapeSharedShellTests
{
    private static readonly (string Page, string Directory)[] Pages =
    [
        ("overview", DreamscapeOverviewFeature.AssetDirectory),
        ("controller", DreamscapeControllerFeature.AssetDirectory),
        ("settings", DreamscapeSettingsFeature.AssetDirectory),
        ("logs", DreamscapeLogsFeature.AssetDirectory)
    ];

    [Fact]
    public void SharedAssets_ExistInCopiedRuntimeDirectory()
    {
        Assert.True(DreamscapeShellMetrics.ValidateAssets(out string error), error);
        Assert.Equal(["shell.css", "shell.js", "official-prism.png"], DreamscapeShellMetrics.RequiredFrontendFiles);
        Assert.All(
            DreamscapeShellMetrics.RequiredFrontendFiles,
            file => Assert.True(File.Exists(Path.Combine(DreamscapeShellMetrics.AssetDirectory, file)), file));
    }

    [Fact]
    public void SharedBranding_UsesOfficialPrismAndDreamscapeNavigationIcons()
    {
        string script = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.js"));
        string css = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.css"));

        Assert.Contains("official-prism.png", script, StringComparison.Ordinal);
        Assert.DoesNotContain("shell-prism-a", script, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", css, StringComparison.Ordinal);
        Assert.Contains("dreamscape-nav-icon .icon-detail", css, StringComparison.Ordinal);
        Assert.Contains("dreamscape-window-surface", css, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPage_UsesTheSameSharedShellAndHasExactlyOneActiveIdentity()
    {
        foreach ((string page, string directory) in Pages)
        {
            string html = File.ReadAllText(Path.Combine(directory, "index.html"));

            Assert.Contains("https://leftpad-shared.local/shell.css", html, StringComparison.Ordinal);
            Assert.Contains("https://leftpad-shared.local/shell.js", html, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(html, "id=\"dreamscape-shell\"").Cast<Match>());
            Assert.Contains($"data-active-page=\"{page}\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"nav-command", html, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"window-drag-zone\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"window-button", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CanvasAndShellGeometry_HaveOneExactLogicalContract()
    {
        Assert.Equal(1672, DreamscapeShellMetrics.DesignWidth);
        Assert.Equal(941, DreamscapeShellMetrics.DesignHeight);
        Assert.Equal(new DreamscapeShellRect(0, 0, 307, 941), DreamscapeShellMetrics.Sidebar);
        Assert.Equal(new DreamscapeShellRect(63, 169, 214, 82), DreamscapeShellMetrics.Brand);
        Assert.Equal(new DreamscapeShellRect(15, 300, 276, 72), DreamscapeShellMetrics.Navigation["overview"]);
        Assert.Equal(new DreamscapeShellRect(15, 382, 276, 72), DreamscapeShellMetrics.Navigation["controller"]);
        Assert.Equal(new DreamscapeShellRect(15, 464, 276, 72), DreamscapeShellMetrics.Navigation["settings"]);
        Assert.Equal(new DreamscapeShellRect(15, 546, 276, 72), DreamscapeShellMetrics.Navigation["logs"]);
        Assert.Equal(new DreamscapeShellRect(1532, 16, 52, 52), DreamscapeShellMetrics.WindowButtons["minimize"]);
        Assert.Equal(new DreamscapeShellRect(1592, 16, 52, 52), DreamscapeShellMetrics.WindowButtons["close"]);

        string script = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.js"));
        Assert.Contains("designWidth: 1672", script, StringComparison.Ordinal);
        Assert.Contains("designHeight: 941", script, StringComparison.Ordinal);
        Assert.Contains("overview: Object.freeze({ x: 15, y: 300, width: 276, height: 72 })", script, StringComparison.Ordinal);
        Assert.Contains("close: Object.freeze({ x: 1592, y: 16, width: 52, height: 52 })", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChrome_HasOneGlyphPerCommandAndAnOpaquePerButtonUnderlay()
    {
        string script = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.js"));
        string css = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.css"));

        Assert.Single(Regex.Matches(script, @">—</button>").Cast<Match>());
        Assert.Single(Regex.Matches(script, @">×</button>").Cast<Match>());
        Assert.Single(Regex.Matches(script, @"data-shell-command=""minimize""").Cast<Match>());
        Assert.Single(Regex.Matches(script, @"data-shell-command=""closeWindow""").Cast<Match>());

        Match underlayRule = Regex.Match(
            css,
            @"\.dreamscape-window-button::before\s*\{(?<body>[^}]*)\}",
            RegexOptions.CultureInvariant);
        Assert.True(underlayRule.Success);
        string body = underlayRule.Groups["body"].Value;
        Assert.Contains("content: \"\"", body, StringComparison.Ordinal);
        Assert.Contains("inset: 0", body, StringComparison.Ordinal);
        Assert.Contains("border-radius: inherit", body, StringComparison.Ordinal);
        Assert.Contains("linear-gradient(145deg, #e1e5f3 0%, #c2d0ea 100%)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("width: 160px", body, StringComparison.Ordinal);
        Assert.DoesNotContain("height: 82px", body, StringComparison.Ordinal);

        Match logsMaskRule = Regex.Match(
            css,
            @"#dreamscape-shell\[data-active-page=""logs""\] \.dreamscape-window-button\.minimize::after\s*\{(?<body>[^}]*)\}",
            RegexOptions.CultureInvariant);
        Assert.True(logsMaskRule.Success);
        string logsMask = logsMaskRule.Groups["body"].Value;
        Assert.Contains("width: 6px", logsMask, StringComparison.Ordinal);
        Assert.Contains("height: 7px", logsMask, StringComparison.Ordinal);
        Assert.DoesNotContain("160px", logsMask, StringComparison.Ordinal);
        Assert.DoesNotContain("82px", logsMask, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedState_DoesNotChangeNavigationBounds()
    {
        string css = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.css"));
        Match selectedRule = Regex.Match(
            css,
            @"\.dreamscape-nav-command\[aria-current=""page""\]::before\s*\{(?<body>[^}]*)\}",
            RegexOptions.CultureInvariant);

        Assert.True(selectedRule.Success);
        string body = selectedRule.Groups["body"].Value;
        Assert.DoesNotContain("left:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("top:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("width:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("height:", body, StringComparison.Ordinal);
    }

    [Fact]
    public void PointerNavigation_ClearsStaleFocusWhileKeyboardFocusRemainsVisible()
    {
        string script = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.js"));
        string css = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.css"));

        Assert.Contains("element.addEventListener('click'", script, StringComparison.Ordinal);
        Assert.Contains("element.blur()", script, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('blur', clearNavigationFocus)", script, StringComparison.Ordinal);
        Assert.Contains(".dreamscape-nav-command:focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("outline: 2px solid var(--dreamscape-shell-focus)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPage_DelegatesScaleAndDiagnosticsToSharedShell()
    {
        foreach ((_, string directory) in Pages)
        {
            string script = File.ReadAllText(Path.Combine(directory, "app.js"));

            Assert.DoesNotContain("function fitCanvas", script, StringComparison.Ordinal);
            Assert.DoesNotContain("const REFERENCE_WIDTH", script, StringComparison.Ordinal);
            Assert.Contains("window.leftpadShell.calculateScale", script, StringComparison.Ordinal);
            Assert.Contains("window.leftpadShell.diagnostics()", script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HostAttachment_AlwaysUsesTheFullLiveClientRectangle()
    {
        RunInSta(() =>
        {
            using var parent = new Panel { ClientSize = new Size(938, 539) };
            using var host = new Panel();

            DreamscapeShellMetrics.AttachHost(parent, host, visible: true);

            Assert.Equal(DockStyle.None, host.Dock);
            Assert.Equal(parent.ClientRectangle, host.Bounds);
            parent.ClientSize = new Size(1312, 758);
            Assert.Equal(parent.ClientRectangle, host.Bounds);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
