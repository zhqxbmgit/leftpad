using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class DreamscapePageActivationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void State_DefaultsInactiveAndWaitsForFrontend()
    {
        var state = new DreamscapePageActivationState();

        Assert.False(state.PageActive);
        Assert.False(state.FrontendReady);
        Assert.False(state.TryTakePending(out bool active));
        Assert.False(active);
    }

    [Fact]
    public void NavigationBeforeInitialization_PreservesFinalActiveState()
    {
        var state = new DreamscapePageActivationState();
        state.SetPageActive(true);

        Assert.False(state.TryTakePending(out _));
        state.NavigationCompleted();

        Assert.True(state.TryTakePending(out bool active));
        Assert.True(active);
    }

    [Fact]
    public void InitializationBeforeActivation_PublishesInactiveThenActive()
    {
        var state = new DreamscapePageActivationState();
        state.NavigationCompleted();

        Assert.True(state.TryTakePending(out bool initiallyActive));
        Assert.False(initiallyActive);

        state.SetPageActive(true);
        Assert.True(state.TryTakePending(out bool activated));
        Assert.True(activated);
    }

    [Fact]
    public void RepeatedActive_IsIdempotent()
    {
        DreamscapePageActivationState state = ReadyState(active: true);
        Assert.True(state.TryTakePending(out _));

        for (int index = 0; index < 50; index++)
        {
            state.SetPageActive(true);
            Assert.False(state.TryTakePending(out _));
        }
    }

    [Fact]
    public void RepeatedInactive_IsIdempotent()
    {
        DreamscapePageActivationState state = ReadyState(active: false);
        Assert.True(state.TryTakePending(out _));

        for (int index = 0; index < 50; index++)
        {
            state.SetPageActive(false);
            Assert.False(state.TryTakePending(out _));
        }
    }

    [Fact]
    public void ActiveInactiveActive_PublishesOneMessagePerTransition()
    {
        DreamscapePageActivationState state = ReadyState(active: false);
        Assert.True(state.TryTakePending(out bool inactive));
        Assert.False(inactive);

        state.SetPageActive(true);
        Assert.True(state.TryTakePending(out bool firstActive));
        Assert.True(firstActive);
        state.SetPageActive(false);
        Assert.True(state.TryTakePending(out bool secondInactive));
        Assert.False(secondInactive);
        state.SetPageActive(true);
        Assert.True(state.TryTakePending(out bool secondActive));
        Assert.True(secondActive);
        Assert.False(state.TryTakePending(out _));
    }

    [Fact]
    public void Reload_ReplaysCurrentActivationAfterNavigationCompletes()
    {
        DreamscapePageActivationState state = ReadyState(active: true);
        Assert.True(state.TryTakePending(out _));

        state.NavigationStarting();
        Assert.False(state.FrontendReady);
        Assert.False(state.TryTakePending(out _));
        state.NavigationCompleted();

        Assert.True(state.TryTakePending(out bool replayed));
        Assert.True(replayed);
    }

    [Fact]
    public void FiftyCycles_DoNotPublishForDuplicateSignals()
    {
        DreamscapePageActivationState state = ReadyState(active: false);
        Assert.True(state.TryTakePending(out _));
        int publications = 0;

        for (int cycle = 0; cycle < 50; cycle++)
        {
            state.SetPageActive(true);
            publications += state.TryTakePending(out _) ? 1 : 0;
            state.SetPageActive(true);
            publications += state.TryTakePending(out _) ? 1 : 0;
            state.SetPageActive(false);
            publications += state.TryTakePending(out _) ? 1 : 0;
            state.SetPageActive(false);
            publications += state.TryTakePending(out _) ? 1 : 0;
        }

        Assert.Equal(100, publications);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivationMessage_HasStrictTypeAndBooleanPayload(bool active)
    {
        using JsonDocument document = JsonDocument.Parse(
            new DreamscapePageActivationMessage(active).ToJson());
        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(2, root.EnumerateObject().Count());
        Assert.Equal(DreamscapePageActivationMessage.MessageType,
            root.GetProperty("type").GetString());
        Assert.Equal(
            active ? JsonValueKind.True : JsonValueKind.False,
            root.GetProperty("active").ValueKind);
        Assert.Equal(active, root.GetProperty("active").GetBoolean());
    }

    [Fact]
    public void OverviewHost_SetActiveBeforeRuntimeRetainsState()
    {
        RunInSta(() =>
        {
            using DreamscapeOverviewHost host = CreateOverviewHost();

            host.SetPageActive(true);

            Assert.True(host.IsPageActive);
            Assert.False(host.IsInitialized);
        });
    }

    [Fact]
    public void SettingsHost_SetActiveBeforeRuntimeRetainsState()
    {
        RunInSta(() =>
        {
            using DreamscapeSettingsHost host = CreateSettingsHost();

            host.SetPageActive(true);

            Assert.True(host.IsPageActive);
            Assert.False(host.IsInitialized);
        });
    }

    [Fact]
    public void OverviewHost_ActivationAfterDisposeIsHarmless()
    {
        RunInSta(() =>
        {
            DreamscapeOverviewHost host = CreateOverviewHost();
            host.Dispose();

            host.SetPageActive(true);

            Assert.True(host.IsDisposed);
            Assert.False(host.IsPageActive);
        });
    }

    [Fact]
    public void SettingsHost_ActivationAfterDisposeIsHarmless()
    {
        RunInSta(() =>
        {
            DreamscapeSettingsHost host = CreateSettingsHost();
            host.Dispose();

            host.SetPageActive(true);

            Assert.True(host.IsDisposed);
            Assert.False(host.IsPageActive);
        });
    }

    [Fact]
    public void OverviewFrontend_PollingIsOwnedByActiveContract()
    {
        string script = ReadAsset(DreamscapeOverviewFeature.AssetDirectory, "app.js");

        Assert.Contains("function startPolling()", script);
        Assert.Contains("function stopPolling()", script);
        Assert.Contains("message.type !== 'pageActivation'", script);
        Assert.Contains("typeof message.active !== 'boolean'", script);
        Assert.DoesNotContain("setInterval(() => postCommand('requestState')", script);
    }

    [Fact]
    public void SettingsFrontend_PollingIsOwnedByActiveContract()
    {
        string script = ReadAsset(DreamscapeSettingsFeature.AssetDirectory, "app.js");

        Assert.Contains("function startPolling()", script);
        Assert.Contains("function stopPolling()", script);
        Assert.Contains("message.type !== 'pageActivation'", script);
        Assert.Contains("typeof message.active !== 'boolean'", script);
        Assert.DoesNotContain("setInterval(() => postCommand('settingsRequestState')", script);
    }

    [Fact]
    public void ControllerFrontend_HasNoPeriodicPolling()
    {
        string script = ReadAsset(DreamscapeControllerFeature.AssetDirectory, "app.js");

        Assert.DoesNotContain("setInterval", script);
        Assert.Contains("postCommand('controllerRequestState')", script);
    }

    [Fact]
    public void LogsFrontend_HasNoPeriodicPolling()
    {
        string script = ReadAsset(DreamscapeLogsFeature.AssetDirectory, "app.js");

        Assert.DoesNotContain("setInterval", script);
        Assert.Contains("postCommand('logsRequestSnapshot')", script);
    }

    [Fact]
    public void ProductionForm_BeforeShowKeepsDreamscapeDormant()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: false);

            DreamscapePageActivity activity = fixture.Form.GetDreamscapePageActivity();

            Assert.False(activity.ReceiverVisible);
            AssertNoActivePage(activity);
            Assert.Null(fixture.Form.DreamscapeOverviewHost);
            Assert.Null(fixture.Form.DreamscapeSettingsHost);
        });
    }

    [Fact]
    public void NativeForm_CreatesNoDreamscapeActivationBridge()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: true);

            Assert.Null(fixture.Form.DreamscapeOverviewHost);
            Assert.Null(fixture.Form.DreamscapeSettingsHost);
            AssertNoActivePage(fixture.Form.GetDreamscapePageActivity());
        });
    }

    [Theory]
    [InlineData("Overview", false, false, false, false)]
    [InlineData("Gamepad", false, false, false, false)]
    [InlineData("Settings", false, false, false, false)]
    [InlineData("Log", false, false, false, false)]
    public void ProductionNavigation_KeepsEveryDreamscapePageInactive(
        string page,
        bool overview,
        bool controller,
        bool settings,
        bool logs)
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: false);
            Navigate(fixture.Form, page);
            fixture.Form.Show();

            DreamscapePageActivity activity = fixture.Form.GetDreamscapePageActivity();

            Assert.True(activity.ReceiverVisible);
            Assert.Equal(overview, activity.Overview);
            Assert.Equal(controller, activity.Controller);
            Assert.Equal(settings, activity.Settings);
            Assert.Equal(logs, activity.Logs);
            Assert.Equal(page, activity.CurrentPage);
            Assert.Equal(0, ActivePageCount(activity));
        });
    }

    [Fact]
    public void TrayHideKeepsDreamscapeDormantAndPreservesNativeSettings()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: false);
            Navigate(fixture.Form, "Settings");
            fixture.Form.Show();
            Assert.Null(SettingsSession(fixture.Form));
            Assert.True(fixture.Form.SettingsControl.TryReadSettingsForTesting(out RadialMenuSettings draftBefore));

            fixture.Form.Hide();

            AssertNoActivePage(fixture.Form.GetDreamscapePageActivity());
            Assert.Null(fixture.Form.DreamscapeSettingsHost);
            Assert.Null(SettingsSession(fixture.Form));
            Assert.True(fixture.Form.SettingsControl.TryReadSettingsForTesting(out RadialMenuSettings draftAfter));
            Assert.Equal(draftBefore, draftAfter);
        });
    }

    [Fact]
    public void SingleInstanceRestoreShowsNativeSettingsAndPreservesCurrentPage()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: false);
            Navigate(fixture.Form, "Settings");
            fixture.Form.Show();
            fixture.Form.Hide();

            Assert.True(fixture.Form.ActivateExistingInstance());

            DreamscapePageActivity activity = fixture.Form.GetDreamscapePageActivity();
            Assert.Equal("Settings", activity.CurrentPage);
            Assert.True(fixture.Form.SettingsControl.Visible);
            Assert.False(activity.Settings);
            Assert.Equal(0, ActivePageCount(activity));
            Assert.Null(fixture.Form.DreamscapeSettingsHost);
            Assert.Null(fixture.Form.DreamscapeOverviewHost);
        });
    }

    [Fact]
    public void FiftyProductionNavigationCycles_KeepDreamscapeDormant()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: false);
            fixture.Form.Show();

            for (int cycle = 0; cycle < 50; cycle++)
            {
                foreach (string page in new[] { "Overview", "Gamepad", "Settings", "Log", "Overview" })
                {
                    Navigate(fixture.Form, page);
                    DreamscapePageActivity activity = fixture.Form.GetDreamscapePageActivity();
                    Assert.Equal(page, activity.CurrentPage);
                    Assert.Equal(0, ActivePageCount(activity));
                }
            }

            Assert.Null(fixture.Form.DreamscapeOverviewHost);
            Assert.Null(fixture.Form.DreamscapeSettingsHost);
        });
    }

    private static DreamscapePageActivationState ReadyState(bool active)
    {
        var state = new DreamscapePageActivationState();
        state.SetPageActive(active);
        state.NavigationCompleted();
        return state;
    }

    private static DreamscapeOverviewHost CreateOverviewHost() =>
        new(() => null!, _ => { }, _ => { });

    private static DreamscapeSettingsHost CreateSettingsHost() =>
        new(() => null!, _ => { }, _ => { });

    private static string ReadAsset(string directory, string file) =>
        File.ReadAllText(Path.Combine(directory, file));

    private static void Navigate(MainForm form, string pageName)
    {
        FieldInfo pageField = Assert.IsAssignableFrom<FieldInfo>(
            typeof(MainForm).GetField("_currentPage", PrivateInstance));
        object requestedPage = Enum.Parse(pageField.FieldType, pageName);
        MethodInfo navigate = Assert.IsAssignableFrom<MethodInfo>(
            typeof(MainForm).GetMethod("NavigateTo", PrivateInstance));
        navigate.Invoke(form, [requestedPage]);
    }

    private static DreamscapeSettingsBasicSession? SettingsSession(MainForm form) =>
        (DreamscapeSettingsBasicSession?)typeof(MainForm)
            .GetField("_dreamscapeSettingsSession", PrivateInstance)!.GetValue(form);

    private static int ActivePageCount(DreamscapePageActivity activity) =>
        new[] { activity.Overview, activity.Controller, activity.Settings, activity.Logs }
            .Count(active => active);

    private static void AssertNoActivePage(DreamscapePageActivity activity) =>
        Assert.Equal(0, ActivePageCount(activity));

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
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class MainFormFixture : IDisposable
    {
        private readonly string? _originalNativeUi;

        public MainFormFixture(bool nativeUi)
        {
            _originalNativeUi = Environment.GetEnvironmentVariable(
                ReceiverFrontendPolicy.NativeUiEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                ReceiverFrontendPolicy.NativeUiEnvironmentVariable,
                nativeUi ? "1" : null);
            Service = new Ds4Service(
                new FakeDirectDs4Factory(),
                new NoOpKeyboardOutput(),
                new MemoryKeyboardBindingStore());
            string settingsPath = Path.Combine(
                Path.GetTempPath(),
                "leftpad-page-activation-tests",
                Guid.NewGuid().ToString("N"),
                "radial-menu-settings.json");
            Form = new MainForm(Service, new RadialMenuSettingsStore(settingsPath));
        }

        public Ds4Service Service { get; }
        public MainForm Form { get; }

        public void Dispose()
        {
            Form.Dispose();
            Service.Dispose();
            Environment.SetEnvironmentVariable(
                ReceiverFrontendPolicy.NativeUiEnvironmentVariable,
                _originalNativeUi);
        }
    }

    private sealed class FakeDirectDs4Factory : IDirectDs4Factory
    {
        public IDirectDs4Session Create() => new NoOpDirectDs4Session();
    }

    private sealed class NoOpDirectDs4Session : IDirectDs4Session
    {
        public void SetButton(DualShock4Button button, bool pressed) { }
        public void SetDPadDirection(DualShock4DPadDirection direction) { }
        public void SetTrigger(DualShock4Slider trigger, byte value) { }
        public void SetLeftStick(byte x, byte y) { }
        public void SubmitReport() { }
        public void Dispose() { }
    }

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void SetKeyState(KeyboardKey key, bool isPressed) { }
    }

    private sealed class MemoryKeyboardBindingStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();
        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
    }
}
