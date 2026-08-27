using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class DreamscapeLogsTests
{
    [Fact]
    public void FormalAssets_ExistInCopiedRuntimeDirectory()
    {
        string directory = DreamscapeLogsFeature.AssetDirectory;

        Assert.True(DreamscapeLogsHost.ValidateAssets(directory, out string error), error);
        Assert.Equal(
            ["index.html", "styles.css", "app.js", "static-art.png"],
            DreamscapeLogsHost.RequiredFrontendFiles);
        Assert.All(
            DreamscapeLogsHost.RequiredFrontendFiles,
            file => Assert.True(File.Exists(Path.Combine(directory, file)), file));
    }

    [Fact]
    public void FormalAssets_HaveNoDesktopPrototypeReviewOrReferenceDependency()
    {
        string combined = ReadAsset("index.html") + ReadAsset("styles.css") +
            ReadAsset("app.js") + DreamscapeLogsFeature.AssetDirectory;

        Assert.DoesNotContain("Desktop", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("visual_prototypes", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("review", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("纪念碑谷4.png", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reference.png", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users", combined, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(expected, DreamscapeLogsFeature.IsEnabledValue(value));
        Assert.NotEqual(DreamscapeOverviewFeature.EnvironmentVariable, DreamscapeLogsFeature.EnvironmentVariable);
        Assert.NotEqual(DreamscapeSettingsFeature.EnvironmentVariable, DreamscapeLogsFeature.EnvironmentVariable);
        Assert.NotEqual(DreamscapeControllerFeature.EnvironmentVariable, DreamscapeLogsFeature.EnvironmentVariable);
    }

    [Fact]
    public void InitialSnapshot_IncludesAllExistingBufferedLinesInOrder()
    {
        var buffer = new ReceiverLogBuffer();
        buffer.Append("first line");
        buffer.Append("second line");

        ReceiverLogSnapshot snapshot = buffer.CreateSnapshot("等待连接");

        Assert.Equal(ReceiverLogSnapshot.MessageType, snapshot.Type);
        Assert.Equal(["first line", "second line"], snapshot.Entries.Select(entry => entry.RawText));
        Assert.Equal([1L, 2L], snapshot.Entries.Select(entry => entry.SequenceId));
        Assert.Equal("等待连接", snapshot.ConnectionState);
    }

    [Fact]
    public void NewLine_ProducesAOneEntryIncrementalAppendMessage()
    {
        var buffer = new ReceiverLogBuffer();
        ReceiverLogEntry entry = buffer.Append("[Info] one new line");
        var append = new ReceiverLogAppend(ReceiverLogAppend.MessageType, entry);

        using JsonDocument document = JsonDocument.Parse(append.ToJson());
        JsonElement root = document.RootElement;

        Assert.Equal("receiverLogAppend", root.GetProperty("type").GetString());
        Assert.Equal("[Info] one new line", root.GetProperty("entry").GetProperty("rawText").GetString());
        Assert.False(root.TryGetProperty("entries", out _));
    }

    [Fact]
    public void Buffer_PreservesThreeHundredLineCapAndEvictsOldestEntries()
    {
        var buffer = new ReceiverLogBuffer();
        for (int index = 1; index <= ReceiverLogBuffer.MaximumEntries + 5; index++)
            buffer.Append($"line {index}");

        ReceiverLogSnapshot snapshot = buffer.CreateSnapshot("等待连接");

        Assert.Equal(300, snapshot.Entries.Count);
        Assert.Equal("line 6", snapshot.Entries[0].RawText);
        Assert.Equal("line 305", snapshot.Entries[^1].RawText);
        Assert.Equal(6, snapshot.Entries[0].SequenceId);
        Assert.Equal(305, snapshot.Entries[^1].SequenceId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(150)]
    [InlineData(300)]
    public void Buffer_DoesNotEvictBeforeCapacity(int count)
    {
        var buffer = new ReceiverLogBuffer();
        for (int index = 0; index < count; index++)
            buffer.Append($"line {index}");

        Assert.Equal(count, buffer.CreateSnapshot("等待连接").Entries.Count);
    }

    [Fact]
    public void RawText_IsPreservedExactlyIncludingMarkupPathsAndWhitespace()
    {
        const string raw = "  <script>alert('x')</script>  C:\\temp\\a<b>.txt  ";
        var buffer = new ReceiverLogBuffer();

        ReceiverLogEntry entry = buffer.Append(raw);
        ReceiverLogSnapshot snapshot = buffer.CreateSnapshot("已连接");

        Assert.Equal(raw, entry.RawText);
        Assert.Equal(raw, Assert.Single(snapshot.Entries).RawText);
        using JsonDocument document = JsonDocument.Parse(snapshot.ToJson());
        Assert.Equal(
            raw,
            document.RootElement.GetProperty("entries")[0].GetProperty("rawText").GetString());
    }

    [Theory]
    [InlineData("[Warning] keep me", "warning")]
    [InlineData("[Controller] keep me", "controller")]
    [InlineData("[Network] keep me", "network")]
    [InlineData("[Info] keep me", "info")]
    [InlineData("[视觉主题] keep me", "dreamscape")]
    [InlineData("[环形菜单] keep me", "dreamscape")]
    [InlineData("plain keep me", "default")]
    public void CategoryClassification_DoesNotMutateRawText(string raw, string category)
    {
        ReceiverLogEntry entry = new ReceiverLogBuffer().Append(raw);

        Assert.Equal(raw, entry.RawText);
        Assert.Equal(category, entry.Category);
    }

    [Fact]
    public void Frontend_WritesRawLogTextUsingTextContentOnly()
    {
        string script = ReadAsset("app.js");

        Assert.Contains("row.textContent = entry.rawText", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insertAdjacentHTML", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("white-space: pre-wrap", ReadAsset("styles.css"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Frontend_ImplementsNearBottomAndUserScrolledAwaySemantics()
    {
        string script = ReadAsset("app.js");

        Assert.Contains("NEAR_BOTTOM_PX", script, StringComparison.Ordinal);
        Assert.Contains("const followLatest = isNearBottom() && !userAwayFromBottom", script, StringComparison.Ordinal);
        Assert.Contains("userAwayFromBottom = !isNearBottom()", script, StringComparison.Ordinal);
        Assert.Contains("if (followLatest) returnToBottom()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_LatestIndicatorReturnsToBottom()
    {
        string html = ReadAsset("index.html");
        string script = ReadAsset("app.js");

        Assert.Contains("id=\"latest-marker\"", html, StringComparison.Ordinal);
        Assert.Contains("latestMarker.addEventListener('click', returnToBottom)", script, StringComparison.Ordinal);
        Assert.Contains("latestMarker.dataset.pending", script, StringComparison.Ordinal);
        Assert.Contains("view.scrollTop = view.scrollHeight", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_EnforcesDomCapAndIncrementalAppendWithoutReload()
    {
        string script = ReadAsset("app.js");

        Assert.Contains("const MAX_ENTRIES = 300", script, StringComparison.Ordinal);
        Assert.Contains("view.append(createLogRow(entry))", script, StringComparison.Ordinal);
        Assert.Contains("view.firstElementChild.remove()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("location.reload", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("setInterval", script, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void HiddenPage_AvoidsUnnecessaryIncrementalDomPushes(
        bool ready,
        bool visible,
        bool expected)
    {
        Assert.Equal(expected, DreamscapeLogsHost.CanPushIncremental(ready, visible));
    }

    [Fact]
    public void ReenterLogs_HasSnapshotVisibilityAndNativeFallbackEntryPoints()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.NotNull(typeof(MainForm).GetMethod("SetDreamscapeLogsVisibility", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("CreateDreamscapeLogsSnapshot", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ShowNativeLogsPage", privateInstance));
    }

    [Theory]
    [InlineData("logsRequestSnapshot", "RequestSnapshot")]
    [InlineData("logsReturnToBottom", "ReturnToBottom")]
    [InlineData("showOverview", "ShowOverview")]
    [InlineData("showController", "ShowController")]
    [InlineData("showSettings", "ShowSettings")]
    [InlineData("beginDrag", "BeginDrag")]
    [InlineData("minimize", "Minimize")]
    [InlineData("closeWindow", "CloseWindow")]
    public void CommandAllowList_AcceptsLogsNavigationAndWindowCommands(string name, string expected)
    {
        bool accepted = ReceiverLogsCommandAllowList.TryParse(
            JsonSerializer.Serialize(new { command = name }),
            out ReceiverLogsCommand command,
            out string reason);

        Assert.True(accepted, reason);
        Assert.Equal(expected, command.ToString());
        Assert.Contains(name, ReceiverLogsCommandAllowList.AllowedNames);
    }

    [Theory]
    [InlineData("{\"command\":\"appendArbitraryHtml\"}")]
    [InlineData("{\"command\":\"clearBusinessLogs\"}")]
    [InlineData("{\"command\":42}")]
    [InlineData("not-json")]
    public void UnknownMutationOrMalformedCommand_IsRejected(string json)
    {
        Assert.False(ReceiverLogsCommandAllowList.TryParse(json, out _, out string reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void Navigation_ContainsOverviewControllerAndSettingsTargets()
    {
        string html = ReadAsset("index.html");
        string shell = File.ReadAllText(Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.js"));

        Assert.Contains("data-active-page=\"logs\"", html, StringComparison.Ordinal);
        Assert.Contains("logs: Object.freeze({ overview: 'showOverview'", shell, StringComparison.Ordinal);
        Assert.Contains("controller: 'showController'", shell, StringComparison.Ordinal);
        Assert.Contains("settings: 'showSettings'", shell, StringComparison.Ordinal);
        Assert.Contains("logs: 'logsRequestSnapshot'", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionPill_UsesLogsSpecificStaticPlateGeometryAndStaysInBounds()
    {
        string styles = ReadAsset("styles.css");
        Match rule = Regex.Match(
            styles,
            @"\.connection-pill\s*\{(?<body>[^}]*)\}",
            RegexOptions.CultureInvariant);

        Assert.True(rule.Success);
        string body = rule.Groups["body"].Value;
        int left = CssPixels(body, "left");
        int top = CssPixels(body, "top");
        int width = CssPixels(body, "width");
        int height = CssPixels(body, "height");
        var pill = new Rectangle(left, top, width, height);
        var logsTitleFrame = new Rectangle(377, 112, 1141, 703);
        var windowChrome = new Rectangle(1512, 0, 160, 82);
        var designCanvas = new Rectangle(0, 0, DreamscapeShellMetrics.DesignWidth, DreamscapeShellMetrics.DesignHeight);

        Assert.Equal(new Rectangle(1277, 163, 190, 57), pill);
        Assert.True(logsTitleFrame.Contains(pill));
        Assert.False(pill.IntersectsWith(windowChrome));
        Assert.True(designCanvas.Contains(pill));
    }

    [Fact]
    public void MainForm_ReusesAppendLogAndDoesNotOwnASecondLogger()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo? appendLog = typeof(MainForm).GetMethod("AppendLog", privateInstance);
        MethodInfo? buffer = typeof(MainForm).GetMethod("BufferDreamscapeLog", privateInstance);
        MethodInfo? publish = typeof(MainForm).GetMethod("PublishDreamscapeLogAppend", privateInstance);

        Assert.NotNull(appendLog);
        Assert.Equal(typeof(string), Assert.Single(appendLog!.GetParameters()).ParameterType);
        Assert.NotNull(buffer);
        Assert.Equal(typeof(string), Assert.Single(buffer!.GetParameters()).ParameterType);
        Assert.Equal(typeof(ReceiverLogMutation), buffer.ReturnType);
        Assert.NotNull(publish);
        Assert.DoesNotContain(
            typeof(DreamscapeLogsHost).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType.Name.Contains("Logger", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HostConstruction_IsSafeAndRemainsAChildControl()
    {
        RunInSta(() =>
        {
            using var host = new DreamscapeLogsHost(
                () => new ReceiverLogBuffer().CreateSnapshot("等待连接"),
                _ => { },
                _ => { });

            Assert.Equal(DockStyle.Fill, host.Dock);
            Assert.Equal("dreamscapeLogsHost", host.Name);
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
    public void StableInputAndMoveConstantsRemainUnchanged()
    {
        Assert.Equal(26.0, VirtualJoystickController.JoystickRadius);
        Assert.Equal(2.0, VirtualJoystickController.ActivationRadius);
        Assert.Equal(128, VirtualJoystickController.NeutralAxis);
        Assert.DoesNotContain(
            typeof(DreamscapeLogsHost).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType == typeof(Ds4Service) ||
                field.FieldType == typeof(VirtualJoystickController) ||
                field.FieldType == typeof(CursorJoystickSampler));
    }

    [Fact]
    public void CompletedDreamscapeAssetsRemainAvailableForRegression()
    {
        Assert.True(DreamscapeOverviewHost.ValidateAssets(
            DreamscapeOverviewFeature.AssetDirectory,
            out string overviewError), overviewError);
        Assert.True(DreamscapeSettingsHost.ValidateAssets(
            DreamscapeSettingsFeature.AssetDirectory,
            out string settingsError), settingsError);
        Assert.True(DreamscapeControllerHost.ValidateAssets(
            DreamscapeControllerFeature.AssetDirectory,
            out string controllerError), controllerError);
    }

    private static string ReadAsset(string file) =>
        File.ReadAllText(Path.Combine(DreamscapeLogsFeature.AssetDirectory, file));

    private static int CssPixels(string ruleBody, string property)
    {
        Match match = Regex.Match(
            ruleBody,
            $@"(?:^|\s){Regex.Escape(property)}:\s*(?<value>\d+)px\s*;",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing {property} in connection pill rule.");
        return int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
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
