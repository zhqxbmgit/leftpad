using System.Reflection;
using System.Text.Json;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class DreamscapeRetirementTests
{
    [Fact]
    public void ProductionAssembly_HasNoWebViewReferenceOrRetiredFrontendAuthority()
    {
        Assembly production = typeof(MainForm).Assembly;
        Assert.DoesNotContain(production.GetReferencedAssemblies(), reference =>
            reference.Name?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(production.GetTypes(), type =>
            type.Name.Contains("Dreamscape", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("WebView2", StringComparison.OrdinalIgnoreCase) ||
            type.Name is "ReceiverFrontendPolicy" or "ReceiverOverviewState" or
                "ReceiverControllerState" or "ReceiverSettingsBasicState" or
                "ReceiverLogSnapshot" or "ReceiverLogsCommandAllowList");

        var constants = production.GetTypes().SelectMany(type => type.GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => field.GetRawConstantValue() as string);
        Assert.DoesNotContain(constants, value => value is not null &&
            (value.StartsWith("LEFTPAD_WEBVIEW2", StringComparison.Ordinal) ||
             value == "LEFTPAD_NATIVE_UI"));
    }

    [Fact]
    public void RestoredProductionAndTestGraphs_HaveNoWebViewPackage()
    {
        foreach (string project in new[] { "PcDs4Server", "PcDs4Server.Tests" })
        {
            string assets = Path.Combine(ServerRoot, project, "obj", "project.assets.json");
            Assert.True(File.Exists(assets), $"Restore must generate {assets} before this gate.");
            using JsonDocument graph = JsonDocument.Parse(File.ReadAllText(assets));
            Assert.DoesNotContain(graph.RootElement.GetProperty("libraries").EnumerateObject(),
                library => library.Name.Contains("WebView2", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("Microsoft.Web.WebView2", graph.RootElement.GetRawText(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProductionAssetsAndProject_DoNotCarryTheRetiredFrontend()
    {
        string production = Path.Combine(ServerRoot, "PcDs4Server");
        string project = File.ReadAllText(Path.Combine(production, "PcDs4Server.csproj"));
        Assert.DoesNotContain("WebView2", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dreamscape", project, StringComparison.OrdinalIgnoreCase);
        foreach (string name in new[] { "Overview", "Controller", "Settings", "Logs", "Shared" })
            Assert.False(Directory.Exists(Path.Combine(production, "Assets", "Dreamscape" + name)));
        Assert.True(Directory.Exists(Path.Combine(production, "Assets", "UIVisualPacks")));
        Assert.True(Directory.Exists(Path.Combine(production, "Assets", "UIThemes")));
    }

    [Fact]
    public void ProductionMainForm_HasOnlyNativePageFieldsAndNoFrontendBridge()
    {
        FieldInfo[] fields = typeof(MainForm).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (string name in new[] { "_overviewCards", "_overviewOutput", "_gamepadMonitor",
            "_joystickDebugSection", "_settingsPage", "_logSection" })
            Assert.Contains(fields, field => field.Name == name &&
                typeof(System.Windows.Forms.Control).IsAssignableFrom(field.FieldType));
        Assert.DoesNotContain(fields, field =>
            field.Name.Contains("Dreamscape", StringComparison.OrdinalIgnoreCase) ||
            field.FieldType.FullName?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string ServerRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
                 directory is not null; directory = directory.Parent)
            {
                string project = Path.Combine(directory.FullName, "PcDs4Server", "PcDs4Server.csproj");
                if (File.Exists(project)) return directory.FullName;
            }
            throw new DirectoryNotFoundException("Cannot locate the restored PcDs4Server projects.");
        }
    }
}
