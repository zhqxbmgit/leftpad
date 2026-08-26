using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class DreamscapeOverviewEditingTests
{
    [Fact]
    public void OutputModeCatalog_ContainsExactlyFormalValuesAndLabels()
    {
        Assert.Equal(
            ["DirectDs4", "Keyboard"],
            ReceiverOverviewCatalog.OutputModeOptions.Select(option => option.Value));
        Assert.Equal(
            ["Direct DS4", "键盘"],
            ReceiverOverviewCatalog.OutputModeOptions.Select(option => option.Label));
    }

    [Fact]
    public void KeyboardKeyCatalog_ComesFromEveryEnumValue()
    {
        string[] expected = Enum.GetValues<KeyboardKey>().Select(key => key.ToString()).ToArray();
        ReceiverOverviewOption[] actual = ReceiverOverviewCatalog.KeyboardKeyOptions.ToArray();

        Assert.Equal(expected, actual.Select(option => option.Value));
        Assert.Equal(expected, actual.Select(option => option.Label));
        Assert.Contains(actual, option => option.Value == "None");
        Assert.True(actual.Length > 1);
    }

    [Fact]
    public void State_SelectsDirectDs4AndDisablesMappingsWhileStopped()
    {
        RunInSta(() =>
        {
            using Ds4Service service = CreateService(out _);
            using MainForm form = CreateForm(service);

            ReceiverOverviewState state = form.CreateDreamscapeOverviewState();

            Assert.Equal("Direct DS4", state.OutputMode);
            Assert.Equal("DirectDs4", state.OutputModeValue);
            Assert.True(state.OutputModeEditable);
            Assert.False(state.KeyboardMappingsEditable);
            Assert.False(state.ServiceRunning);
            Assert.Equal("None", state.Mappings["CR"]);
            Assert.True(state.KeyboardKeyOptions.Count > 1);
        });
    }

    [Fact]
    public void State_SelectsKeyboardAndEnablesMappingsWhileStopped()
    {
        RunInSta(() =>
        {
            using Ds4Service service = CreateService(out _);
            Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
            using MainForm form = CreateForm(service);

            ReceiverOverviewState state = form.CreateDreamscapeOverviewState();

            Assert.Equal("键盘", state.OutputMode);
            Assert.Equal("Keyboard", state.OutputModeValue);
            Assert.True(state.OutputModeEditable);
            Assert.True(state.KeyboardMappingsEditable);
            Assert.False(state.ServiceRunning);
        });
    }

    [Theory]
    [InlineData(OutputMode.DirectDs4)]
    [InlineData(OutputMode.Keyboard)]
    public void State_DisablesOutputModeAndMappingsWhileRunning(OutputMode mode)
    {
        RunInSta(() =>
        {
            using Ds4Service service = CreateService(out _);
            Assert.True(service.TrySetOutputMode(mode));
            SetRunningForTest(service, true);
            using MainForm form = CreateForm(service);

            ReceiverOverviewState state = form.CreateDreamscapeOverviewState();

            Assert.False(state.OutputModeEditable);
            Assert.False(state.KeyboardMappingsEditable);
            Assert.True(state.ServiceRunning);
            SetRunningForTest(service, false);
        });
    }

    [Theory]
    [InlineData("DirectDs4", OutputMode.DirectDs4)]
    [InlineData("Keyboard", OutputMode.Keyboard)]
    public void SetOutputModeParser_AcceptsOnlyFormalEnumNames(string value, OutputMode expected)
    {
        bool accepted = ReceiverOverviewCommandAllowList.TryParse(
            JsonSerializer.Serialize(new { command = "setOutputMode", value }),
            out ReceiverOverviewCommandRequest request,
            out string reason);

        Assert.True(accepted, reason);
        Assert.Equal(ReceiverOverviewCommand.SetOutputMode, request.Command);
        Assert.Equal(expected, request.OutputMode);
    }

    [Theory]
    [InlineData("Direct DS4")]
    [InlineData("键盘")]
    [InlineData("directds4")]
    [InlineData("Unknown")]
    public void SetOutputModeParser_RejectsDisplayTextCasingAndUnknownValues(string value)
    {
        AssertRejected(JsonSerializer.Serialize(new { command = "setOutputMode", value }));
    }

    [Theory]
    [InlineData("cross")]
    [InlineData("circle")]
    [InlineData("square")]
    [InlineData("triangle")]
    [InlineData("l1")]
    [InlineData("r1")]
    [InlineData("l2")]
    [InlineData("r2")]
    [InlineData("l3")]
    [InlineData("r3")]
    public void SetKeyboardMappingParser_AcceptsAllFormalProtocolActions(string action)
    {
        bool accepted = ReceiverOverviewCommandAllowList.TryParse(
            JsonSerializer.Serialize(new { command = "setKeyboardMapping", action, key = "Enter" }),
            out ReceiverOverviewCommandRequest request,
            out string reason);

        Assert.True(accepted, reason);
        Assert.Equal(ReceiverOverviewCommand.SetKeyboardMapping, request.Command);
        Assert.Equal(action, request.Action);
        Assert.Equal(KeyboardKey.Enter, request.Key);
    }

    [Theory]
    [InlineData("CR", "Enter")]
    [InlineData("Cross", "Enter")]
    [InlineData("move", "Enter")]
    [InlineData("cross", "enter")]
    [InlineData("cross", "NotAKey")]
    public void SetKeyboardMappingParser_RejectsInvalidActionOrKey(string action, string key)
    {
        AssertRejected(JsonSerializer.Serialize(new { command = "setKeyboardMapping", action, key }));
    }

    [Theory]
    [InlineData("{\"command\":\"setOutputMode\",\"value\":42}")]
    [InlineData("{\"command\":\"setOutputMode\",\"value\":\"Keyboard\",\"extra\":true}")]
    [InlineData("{\"command\":\"setKeyboardMapping\",\"action\":\"cross\",\"key\":42}")]
    [InlineData("{\"command\":\"setKeyboardMapping\",\"action\":\"cross\"}")]
    [InlineData("{\"command\":\"requestState\",\"value\":\"anything\"}")]
    public void Parser_RejectsWrongPayloadTypesMissingFieldsAndUnexpectedProperties(string json)
    {
        AssertRejected(json);
    }

    [Fact]
    public void ValidModeAndMappingCommands_UseServiceStoreAndPreserveMappingAcrossModes()
    {
        RunInSta(() =>
        {
            using Ds4Service service = CreateService(out RecordingBindingStore store);
            using MainForm form = CreateForm(service);

            form.HandleDreamscapeOverviewCommand(new(
                ReceiverOverviewCommand.SetOutputMode,
                OutputMode: OutputMode.Keyboard));
            form.HandleDreamscapeOverviewCommand(new(
                ReceiverOverviewCommand.SetKeyboardMapping,
                Action: "cross",
                Key: KeyboardKey.Enter));

            Assert.Equal(OutputMode.Keyboard, service.OutputMode);
            Assert.Equal(KeyboardKey.Enter, service.KeyboardBindings.Cross);
            Assert.Equal(KeyboardKey.Enter, store.Saved.Cross);
            Assert.Equal(1, store.SaveCount);

            form.HandleDreamscapeOverviewCommand(new(
                ReceiverOverviewCommand.SetOutputMode,
                OutputMode: OutputMode.DirectDs4));
            form.HandleDreamscapeOverviewCommand(new(
                ReceiverOverviewCommand.SetOutputMode,
                OutputMode: OutputMode.Keyboard));

            Assert.Equal(KeyboardKey.Enter, service.KeyboardBindings.Cross);
            Assert.Equal("Enter", form.CreateDreamscapeOverviewState().Mappings["CR"]);
            Assert.Equal(1, store.SaveCount);
        });
    }

    [Fact]
    public void MappingCommand_IsRejectedInDirectDs4WithoutPersistence()
    {
        RunInSta(() =>
        {
            using Ds4Service service = CreateService(out RecordingBindingStore store);
            using MainForm form = CreateForm(service);

            form.HandleDreamscapeOverviewCommand(new(
                ReceiverOverviewCommand.SetKeyboardMapping,
                Action: "cross",
                Key: KeyboardKey.Enter));

            Assert.Equal(KeyboardKey.None, service.KeyboardBindings.Cross);
            Assert.Equal(0, store.SaveCount);
        });
    }

    [Fact]
    public void ModeAndMappingCommands_AreRejectedWhileRunning()
    {
        RunInSta(() =>
        {
            using Ds4Service service = CreateService(out RecordingBindingStore store);
            Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
            using MainForm form = CreateForm(service);
            SetRunningForTest(service, true);

            form.HandleDreamscapeOverviewCommand(new(
                ReceiverOverviewCommand.SetOutputMode,
                OutputMode: OutputMode.DirectDs4));
            form.HandleDreamscapeOverviewCommand(new(
                ReceiverOverviewCommand.SetKeyboardMapping,
                Action: "cross",
                Key: KeyboardKey.Enter));

            Assert.Equal(OutputMode.Keyboard, service.OutputMode);
            Assert.Equal(KeyboardKey.None, service.KeyboardBindings.Cross);
            Assert.Equal(0, store.SaveCount);
            SetRunningForTest(service, false);
        });
    }

    [Fact]
    public void FrontendSelectSync_IsCatalogDrivenIdempotentAndDoesNotInferBusinessFromLabels()
    {
        string script = File.ReadAllText(Path.Combine(
            DreamscapeOverviewFeature.AssetDirectory,
            "app.js"));

        Assert.Contains("function syncSelectOptions(select, options, selectedValue, editable)", script);
        Assert.Contains("select.dataset.catalogSignature !== signature", script);
        Assert.Contains("select.replaceChildren(fragment)", script);
        Assert.Contains("select.value !== selectedValue", script);
        Assert.DoesNotContain("function setSelectValue", script);
        Assert.DoesNotContain("state.outputMode)", script);
        Assert.DoesNotContain("state.startStopLabel ===", script);
        Assert.Contains("state.outputModeOptions", script);
        Assert.Contains("state.keyboardKeyOptions", script);
        Assert.Contains("state.serviceRunning ? 'running' : 'stopped'", script);
    }

    private static void AssertRejected(string json)
    {
        bool accepted = ReceiverOverviewCommandAllowList.TryParse(
            json,
            out _,
            out string reason);

        Assert.False(accepted);
        Assert.NotEmpty(reason);
    }

    private static MainForm CreateForm(Ds4Service service)
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            "leftpad-overview-editing-tests",
            Guid.NewGuid().ToString("N"),
            "radial-menu-settings.json");
        return new MainForm(service, new RadialMenuSettingsStore(settingsPath));
    }

    private static Ds4Service CreateService(out RecordingBindingStore store)
    {
        store = new RecordingBindingStore();
        return new Ds4Service(
            new UnusedDirectDs4Factory(),
            new NoOpKeyboardOutput(),
            store);
    }

    private static void SetRunningForTest(Ds4Service service, bool value)
    {
        PropertyInfo property = typeof(Ds4Service).GetProperty(nameof(Ds4Service.IsRunning))!;
        property.SetValue(service, value);
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

    private sealed class UnusedDirectDs4Factory : IDirectDs4Factory
    {
        public IDirectDs4Session Create() =>
            throw new InvalidOperationException("Direct DS4 must not initialize in Overview editing tests.");
    }

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void SetKeyState(KeyboardKey key, bool isPressed) { }
    }

    private sealed class RecordingBindingStore : IKeyboardBindingStore
    {
        public KeyboardBindings Saved { get; private set; } = new();
        public int SaveCount { get; private set; }

        public KeyboardBindings Load() => Saved.Clone();

        public void Save(KeyboardBindings bindings)
        {
            Saved = bindings.Clone();
            SaveCount++;
        }
    }
}
