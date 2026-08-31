using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class DreamscapeControllerTests
{
    [Fact]
    public void FormalAssets_ExistInCopiedRuntimeDirectory()
    {
        string directory = DreamscapeControllerFeature.AssetDirectory;

        Assert.True(DreamscapeControllerHost.ValidateAssets(directory, out string error), error);
        Assert.Equal(
            ["index.html", "styles.css", "app.js", "static-art.png"],
            DreamscapeControllerHost.RequiredFrontendFiles);
        Assert.All(
            DreamscapeControllerHost.RequiredFrontendFiles,
            file => Assert.True(File.Exists(Path.Combine(directory, file)), file));
    }

    [Fact]
    public void FormalAssets_HaveNoDesktopPrototypeOrReferenceDependency()
    {
        string html = ReadAsset("index.html");
        string css = ReadAsset("styles.css");
        string script = ReadAsset("app.js");
        string combined = html + css + script + DreamscapeControllerFeature.AssetDirectory;

        Assert.DoesNotContain("Desktop", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("visual_prototypes", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("纪念碑谷3.png", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reference.png", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", html + css + script, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    [InlineData("1", false)]
    [InlineData("true", false)]
    [InlineData("yes", false)]
    [InlineData("on", false)]
    public void FeatureFlag_RemainsDisabledForEveryLegacyOverride(string? value, bool expected)
    {
        Assert.Equal(expected, DreamscapeControllerFeature.IsEnabledValue(value));
        Assert.NotEqual(
            DreamscapeOverviewFeature.EnvironmentVariable,
            DreamscapeControllerFeature.EnvironmentVariable);
        Assert.NotEqual(
            DreamscapeSettingsFeature.EnvironmentVariable,
            DreamscapeControllerFeature.EnvironmentVariable);
    }

    [Fact]
    public void StateDto_SerializesElevenDiagnosticsButtonsAndConnectionState()
    {
        ReceiverControllerState state = ReceiverControllerState.Create(
            CreateSnapshot(),
            new Dictionary<string, bool>
            {
                ["triangle"] = true,
                ["square"] = false,
                ["cross"] = true,
                ["circle"] = false
            },
            "已连接");

        using JsonDocument document = JsonDocument.Parse(state.ToJson());
        JsonElement root = document.RootElement;
        string[] diagnosticProperties =
        [
            "moveState", "mode", "cursorSampling", "directionCaptured", "moveLocked",
            "currentDirection", "lockedDirection", "joystick", "ds4", "center", "cursor"
        ];

        Assert.Equal(ReceiverControllerState.MessageType, root.GetProperty("type").GetString());
        Assert.All(diagnosticProperties, name => Assert.True(root.TryGetProperty(name, out _), name));
        Assert.True(root.GetProperty("trianglePressed").GetBoolean());
        Assert.False(root.GetProperty("squarePressed").GetBoolean());
        Assert.True(root.GetProperty("crossPressed").GetBoolean());
        Assert.False(root.GetProperty("circlePressed").GetBoolean());
        Assert.Equal("已连接", root.GetProperty("connectionState").GetString());
    }

    [Fact]
    public void StateFormatting_PreservesNativePrecisionAndMissingPointPresentation()
    {
        ReceiverControllerState active = ReceiverControllerState.Create(
            CreateSnapshot(),
            new Dictionary<string, bool>(),
            "等待连接");
        ReceiverControllerState missing = ReceiverControllerState.Create(
            snapshot: null,
            new Dictionary<string, bool>(),
            connectionState: "");

        Assert.Equal("0.125 / -0.500", active.CurrentDirection);
        Assert.Equal("-0.250 / 0.750", active.LockedDirection);
        Assert.Equal("0.500 / -0.250", active.Joystick);
        Assert.Equal("192 / 64", active.Ds4);
        Assert.Equal("960 / 540", active.Center);
        Assert.Equal("986 / 514", active.Cursor);
        Assert.Equal("0.000 / 0.000", missing.CurrentDirection);
        Assert.Equal("128 / 128", missing.Ds4);
        Assert.Equal("- / -", missing.Center);
        Assert.Equal("- / -", missing.Cursor);
        Assert.Equal("等待连接", missing.ConnectionState);
    }

    [Theory]
    [InlineData("triangle", true, false, false, false)]
    [InlineData("square", false, true, false, false)]
    [InlineData("cross", false, false, true, false)]
    [InlineData("circle", false, false, false, true)]
    public void ButtonState_ComesFromExistingNativeButtonDictionary(
        string pressedButton,
        bool triangle,
        bool square,
        bool cross,
        bool circle)
    {
        ReceiverControllerState state = ReceiverControllerState.Create(
            CreateSnapshot(),
            new Dictionary<string, bool> { [pressedButton] = true },
            "等待连接");

        Assert.Equal(triangle, state.TrianglePressed);
        Assert.Equal(square, state.SquarePressed);
        Assert.Equal(cross, state.CrossPressed);
        Assert.Equal(circle, state.CirclePressed);
    }

    [Fact]
    public void Frontend_ContainsAllElevenDiagnosticFieldsAsHtmlText()
    {
        string html = ReadAsset("index.html");
        string[] ids =
        [
            "move-state", "mode", "cursor-sampling", "direction-captured", "move-locked",
            "current-direction", "locked-direction", "joystick", "ds4", "center", "cursor"
        ];

        Assert.All(ids, id => Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal));
        Assert.Equal(11, CountOccurrences(html, "class=\"field\""));
    }

    [Fact]
    public void Frontend_PressedAndUnpressedStatesOnlyChangePresentation()
    {
        string html = ReadAsset("index.html");
        string css = ReadAsset("styles.css");
        string script = ReadAsset("app.js");

        Assert.Equal(4, CountOccurrences(html, "data-pressed=\"false\""));
        Assert.Contains("data-pressed=\"true\"", css, StringComparison.Ordinal);
        Assert.Contains("pressed ? '已按下' : '未按下'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("setLeftStick", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deadzone", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("directionX", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticSurface_IsFullyOpaqueAndCannotLeakBakedText()
    {
        string css = ReadAsset("styles.css");

        Assert.Contains("background: #fff8f4", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba(255,249,246", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isolation: isolate", css, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("controllerRequestState", "RequestState")]
    [InlineData("showOverview", "ShowOverview")]
    [InlineData("showSettings", "ShowSettings")]
    [InlineData("showLogs", "ShowLogs")]
    [InlineData("beginDrag", "BeginDrag")]
    [InlineData("minimize", "Minimize")]
    [InlineData("closeWindow", "CloseWindow")]
    public void CommandAllowList_AcceptsOnlyControllerViewAndNavigationCommands(
        string name,
        string expected)
    {
        bool accepted = ReceiverControllerCommandAllowList.TryParse(
            JsonSerializer.Serialize(new { command = name }),
            out ReceiverControllerCommand command,
            out string reason);

        Assert.True(accepted, reason);
        Assert.Equal(expected, command.ToString());
        Assert.Contains(name, ReceiverControllerCommandAllowList.AllowedNames);
    }

    [Theory]
    [InlineData("{\"command\":\"startStop\"}")]
    [InlineData("{\"command\":\"injectInput\"}")]
    [InlineData("{\"command\":42}")]
    [InlineData("not-json")]
    public void UnknownInputOrMalformedCommand_IsRejected(string json)
    {
        Assert.False(ReceiverControllerCommandAllowList.TryParse(
            json,
            out _,
            out string reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void MainForm_ReusesExistingJoystickSnapshotAndButtonStateSources()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo? joystick = typeof(MainForm).GetMethod(
            "PublishDreamscapeControllerJoystickState",
            privateInstance);
        MethodInfo? display = typeof(MainForm).GetMethod(
            "PublishDreamscapeControllerDisplayState",
            privateInstance);

        Assert.NotNull(joystick);
        Assert.Equal(typeof(VirtualJoystickSnapshot), Assert.Single(joystick!.GetParameters()).ParameterType);
        Assert.NotNull(display);
        Assert.DoesNotContain(
            typeof(DreamscapeControllerHost).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType == typeof(Ds4Service));
    }

    [Fact]
    public void ControllerIntegration_DoesNotOwnAnyInputDeviceOrSampler()
    {
        FieldInfo[] fields = typeof(DreamscapeControllerHost).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.DoesNotContain(fields, field => field.FieldType == typeof(Ds4Service));
        Assert.DoesNotContain(fields, field => field.FieldType == typeof(VirtualJoystickController));
        Assert.DoesNotContain(fields, field => field.FieldType == typeof(CursorJoystickSampler));
        Assert.DoesNotContain(fields, field => field.FieldType.Name.Contains("ViGEm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StableMoveConstants_RemainUnchanged()
    {
        Assert.Equal(26.0, VirtualJoystickController.JoystickRadius);
        Assert.Equal(2.0, VirtualJoystickController.ActivationRadius);
        Assert.Equal(128, VirtualJoystickController.NeutralAxis);
    }

    [Fact]
    public void Navigation_RoutesToWebControllerAndKeepsNativeFallbackEntryPoint()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.NotNull(typeof(MainForm).GetMethod("SetDreamscapeControllerVisibility", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ShowNativeControllerPage", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("HandleDreamscapeControllerCommand", privateInstance));
        string html = ReadAsset("index.html");
        string shell = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.js"));
        Assert.Contains("data-active-page=\"controller\"", html, StringComparison.Ordinal);
        Assert.Contains("controller: Object.freeze({ overview: 'showOverview'", shell, StringComparison.Ordinal);
        Assert.Contains("settings: 'showSettings'", shell, StringComparison.Ordinal);
        Assert.Contains("logs: 'showLogs'", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void HostConstruction_IsSafeAndRemainsAChildControl()
    {
        RunInSta(() =>
        {
            using var host = new DreamscapeControllerHost(
                () => ReceiverControllerState.Create(
                    null,
                    new Dictionary<string, bool>(),
                    "等待连接"),
                _ => { },
                _ => { });

            Assert.Equal(DockStyle.Fill, host.Dock);
            Assert.Equal("dreamscapeControllerHost", host.Name);
            Assert.False(host.IsInitialized);
            Assert.Single(host.Controls);
            Assert.False(host.TopLevelControl is Form);
        });
    }

    [Fact]
    public void WindowLifecycle_UsesExistingMainFormBehavior()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.NotNull(typeof(MainForm).GetMethod("BeginDreamscapeWindowDrag", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("MainForm_FormClosing", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ExitProgram", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ShowMainForm", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("WndProc", privateInstance));
    }

    [Fact]
    public void StateUpdates_DoNotRebuildDomOrStaticArt()
    {
        string html = ReadAsset("index.html");
        string script = ReadAsset("app.js");

        Assert.Equal(1, CountOccurrences(html, "id=\"static-art\""));
        Assert.Contains("textContent", script, StringComparison.Ordinal);
        Assert.Contains("if (element.textContent === text) return", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createElement('img')", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("setInterval", script, StringComparison.OrdinalIgnoreCase);
    }

    private static VirtualJoystickSnapshot CreateSnapshot() => new(
        MoveButtonPressed: true,
        JoystickActive: true,
        MovementLocked: false,
        DirectionCapturedDuringHold: true,
        Center: new ScreenPoint(960, 540),
        CurrentCursor: new ScreenPoint(986, 514),
        CursorDeltaX: 26,
        CursorDeltaY: -26,
        CursorDistance: Math.Sqrt(26 * 26 * 2),
        DirectionActive: true,
        CurrentDirectionX: 0.125,
        CurrentDirectionY: -0.5,
        LockedDirectionX: -0.25,
        LockedDirectionY: 0.75,
        LogicalKnobX: 13,
        LogicalKnobY: -6.5,
        StickX: 0.5,
        StickY: -0.25,
        Ds4X: 192,
        Ds4Y: 64,
        OverlayVisible: true,
        LastResetReason: null);

    private static string ReadAsset(string file) =>
        File.ReadAllText(Path.Combine(DreamscapeControllerFeature.AssetDirectory, file));

    private static int CountOccurrences(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) /
        value.Length;

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
