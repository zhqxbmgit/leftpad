using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class DreamscapeOverviewSpikeTests
{
    [Fact]
    public void WebFrontendFiles_ExistInCopiedRuntimeAssets()
    {
        string directory = DreamscapeOverviewFeature.AssetDirectory;

        Assert.True(DreamscapeOverviewHost.ValidateAssets(directory, out string error), error);
        Assert.All(
            DreamscapeOverviewHost.RequiredFrontendFiles,
            file => Assert.True(File.Exists(Path.Combine(directory, file)), file));
        Assert.False(File.Exists(Path.Combine(directory, "reference-overview.png")));
        Assert.False(File.Exists(Path.Combine(directory, "dynamic-mask.png")));
        Assert.False(File.Exists(Path.Combine(directory, "coordinates.json")));
        Assert.Contains("plain", "plain HTML/CSS/JS", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateDto_SerializesOnlyExpectedCamelCaseBridgeShape()
    {
        var state = new ReceiverOverviewState(
            ReceiverOverviewState.MessageType,
            "就绪",
            "已连接",
            "等待连接",
            8888,
            "Direct DS4",
            "DirectDs4",
            ReceiverOverviewCatalog.OutputModeOptions,
            ReceiverOverviewCatalog.KeyboardKeyOptions,
            true,
            false,
            false,
            "停止",
            new Dictionary<string, string> { ["CR"] = "None" });

        using JsonDocument document = JsonDocument.Parse(state.ToJson());
        JsonElement root = document.RootElement;

        Assert.Equal("receiverOverviewState", root.GetProperty("type").GetString());
        Assert.Equal("就绪", root.GetProperty("vigemStatus").GetString());
        Assert.Equal("已连接", root.GetProperty("virtualDs4Status").GetString());
        Assert.Equal("等待连接", root.GetProperty("phoneStatus").GetString());
        Assert.Equal(8888, root.GetProperty("port").GetInt32());
        Assert.Equal("Direct DS4", root.GetProperty("outputMode").GetString());
        Assert.Equal("DirectDs4", root.GetProperty("outputModeValue").GetString());
        Assert.True(root.GetProperty("outputModeEditable").GetBoolean());
        Assert.False(root.GetProperty("keyboardMappingsEditable").GetBoolean());
        Assert.False(root.GetProperty("serviceRunning").GetBoolean());
        Assert.Equal("None", root.GetProperty("mappings").GetProperty("CR").GetString());
    }

    [Theory]
    [InlineData("startStop", "StartStop")]
    [InlineData("requestState", "RequestState")]
    [InlineData("beginDrag", "BeginDrag")]
    [InlineData("minimize", "Minimize")]
    [InlineData("closeWindow", "CloseWindow")]
    public void CommandAllowList_AcceptsExplicitCommandsOnly(
        string name,
        string expected)
    {
        bool accepted = ReceiverOverviewCommandAllowList.TryParse(
            JsonSerializer.Serialize(new { command = name }),
            out ReceiverOverviewCommandRequest request,
            out string reason);

        Assert.True(accepted, reason);
        Assert.Equal(expected, request.Command.ToString());
        Assert.Contains(name, ReceiverOverviewCommandAllowList.AllowedNames);
    }

    [Theory]
    [InlineData("{\"command\":\"deleteEverything\"}")]
    [InlineData("{\"command\":42}")]
    [InlineData("{\"notACommand\":\"startStop\"}")]
    [InlineData("not-json")]
    public void UnknownOrMalformedCommand_IsRejectedWithoutThrowing(string json)
    {
        bool accepted = ReceiverOverviewCommandAllowList.TryParse(
            json,
            out _,
            out string reason);

        Assert.False(accepted);
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData(1672, 941, 1.0)]
    [InlineData(836, 941, 0.5)]
    [InlineData(1672, 470.5, 0.5)]
    [InlineData(2508, 1411.5, 1.5)]
    public void ScaleCalculation_IsUniformAndReferenceBased(
        double viewportWidth,
        double viewportHeight,
        double expected)
    {
        Assert.Equal(expected, DreamscapeCanvasScale.Calculate(viewportWidth, viewportHeight), 6);
    }

    [Fact]
    public void ReferenceCoordinateMetadata_MatchesMeasuredTarget()
    {
        Assert.Equal(1672, DreamscapeReferenceMetadata.ReferenceWidth);
        Assert.Equal(941, DreamscapeReferenceMetadata.ReferenceHeight);
        Assert.Equal(64, DreamscapeReferenceMetadata.ReferenceSha256.Length);
        Assert.True(DreamscapeReferenceMetadata.Elements.Count >= 20);
        Assert.Contains(DreamscapeReferenceMetadata.Elements,
            element => element is { Name: "Navigation rail", X: 0, Y: 0, Width: 288, Height: 941 });
        Assert.Contains(DreamscapeReferenceMetadata.Elements,
            element => element is { Name: "Mapping panel", X: 864, Y: 518, Width: 720, Height: 375 });
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    public void FeatureFlag_DefaultsToDreamscapeAndAllowsExplicitOverride(string? value, bool expected)
    {
        Assert.Equal(expected, DreamscapeOverviewFeature.IsEnabledValue(value));
    }

    [Fact]
    public void MainFormLifecycleEntryPoints_RemainPresent()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.NotNull(typeof(MainForm).GetMethod("MainForm_FormClosing", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ExitProgram", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ShowMainForm", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("WndProc", privateInstance));
        Assert.True(DreamscapeOverviewFeature.IsEnabledValue(null));
    }

    [Fact]
    public void WebView2Host_ConstructionIsSafeBeforeRuntimeInitialization()
    {
        RunInSta(() =>
        {
            using var host = new DreamscapeOverviewHost(
                () => new ReceiverOverviewState(
                    ReceiverOverviewState.MessageType,
                    "未启动",
                    "未创建",
                    "等待连接",
                    8888,
                    "Direct DS4",
                    "DirectDs4",
                    ReceiverOverviewCatalog.OutputModeOptions,
                    ReceiverOverviewCatalog.KeyboardKeyOptions,
                    true,
                    false,
                    false,
                    "启动",
                    new Dictionary<string, string>()),
                _ => { },
                _ => { });

            Assert.Equal(DockStyle.Fill, host.Dock);
            Assert.Equal("dreamscapeOverviewHost", host.Name);
            Assert.False(host.IsInitialized);
            Assert.Single(host.Controls);
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
