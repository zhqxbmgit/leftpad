using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class NativeReceiverProductionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("invalid")]
    public void ProductionMainForm_UsesOnlyNativeReceiverPages(string? legacyOverride)
    {
        RunInSta(() =>
        {
            using var environment = new LegacyFrontendEnvironment(legacyOverride);
            using var fixture = new NativeFormFixture();
            MainForm form = fixture.Form;
            AssertNativeControlTree(form);
            // Exercise the real HWND / OnShown path with isolated stores and no real output device.
            form.Show();
            Assert.True(form.IsHandleCreated);
            foreach (string page in new[] { "Overview", "Gamepad", "Settings", "Log", "Overview" })
            {
                Navigate(form, page);
                AssertNativePage(form, page);
            }
            Assert.Empty(form.OwnedForms);
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Store.Path));
            // Existing binding-editor initialization saves defaults through the injected memory store.
            Assert.All(KeyboardBindings.ProtocolActions,
                action => Assert.Equal(KeyboardKey.None, fixture.Keyboard.Load().Get(action)));
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(175)]
    [InlineData(200)]
    public void NativePages_RemainAccessibleAtEveryReceiverScaleWithoutChangingRadialScale(int scale)
    {
        RunInSta(() =>
        {
            using var fixture = new NativeFormFixture();
            fixture.Form.Show();
            RadialMenuController controller = Field<RadialMenuController>(fixture.Form, "_radialMenu");
            RadialMenuSettings original = controller.ConfiguredSettings;
            ReceiverUiScaling scaling = Field<ReceiverUiScaling>(fixture.Form, "_receiverUiScaling");
            scaling.Apply(scale);
            Assert.Equal(scale, scaling.CurrentScalePercent);
            foreach (string page in new[] { "Overview", "Gamepad", "Settings", "Log", "Overview" })
            {
                Navigate(fixture.Form, page);
                AssertNativePage(fixture.Form, page);
            }
            Assert.Equal(original, controller.ConfiguredSettings);
            Assert.Equal(original.ScalePercent, controller.ActiveSettings.ScalePercent);
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Store.Path));
        });
    }

    [Fact]
    public void NativeStatusButtonsMoveAndLogs_ReceiveServiceUpdatesWithoutWebHosts()
    {
        RunInSta(() =>
        {
            using var fixture = new NativeFormFixture();
            MainForm form = fixture.Form;
            form.Show();
            RaiseServiceEvent(fixture.Service, "OnStatusChanged", "ViGEmBus：已连接 / 虚拟 DS4：就绪");
            RaiseServiceEvent(fixture.Service, "OnConnectionChanged", "已连接：isolated-test");
            Assert.Equal("就绪", Field<ModernStatusCard>(form, "_cardVigem").Value);
            Assert.Equal("已连接", Field<ModernStatusCard>(form, "_cardPhone").Value);

            Navigate(form, "Gamepad");
            fixture.Service.ProcessProtocolAction("triangle", "down");
            Assert.True(Field<Dictionary<string, bool>>(form, "_btnStates")["triangle"]);
            fixture.Service.ProcessProtocolAction("triangle", "up");
            Assert.False(Field<Dictionary<string, bool>>(form, "_btnStates")["triangle"]);
            VirtualJoystickSnapshot snapshot = Field<VirtualJoystickController>(fixture.Service, "_joystick").Snapshot with
            {
                MoveButtonPressed = true,
                JoystickActive = true,
                DirectionCapturedDuringHold = true,
                StickX = 0.5,
                StickY = -0.5,
                Ds4X = 192,
                Ds4Y = 64
            };
            RaiseServiceEvent(fixture.Service, "OnJoystickStateChanged", snapshot);
            string debug = Field<Label>(form, "_joystickDebug").Text;
            Assert.Contains("MOVE：按住", debug);
            Assert.Contains("模式：实时", debug);
            Assert.Contains("DS4：192 / 64", debug);

            Navigate(form, "Log");
            Invoke(form, "AppendLog", "native append sentinel");
            RaiseServiceEvent(fixture.Service, "OnLog", "native service sentinel");
            Invoke(form, "LogRadialMessage", "native radial sentinel");
            Invoke(form, "LogRadialMessage", "native persistence error sentinel");
            string logs = Field<RichTextBox>(form, "_logBox").Text;
            foreach (string sentinel in new[] { "append", "service", "radial", "persistence error" })
                Assert.Contains($"native {sentinel} sentinel", logs);
            AssertNativePage(form, "Log");
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Store.Path));
        });
    }

    [Fact]
    public void NativeCrossProfileApplySave_CommitsWhilePublishedLayoutIsStillRadial6()
    {
        RunInSta(() =>
        {
            using var directory = new IsolatedSettingsDirectory();
            var store = new RadialMenuSettingsStore(directory.SettingsPath);
            Assert.True(store.TrySave(CrossProfileSettingsScenario.Original, out string initialError), initialError);
            var overlay = CrossProfileSettingsScenario.PendingOverlay();
            using var controller = new RadialMenuController(overlay, CrossProfileSettingsScenario.Original);
            var logs = new List<string>();
            using var control = new RadialMenuSettingsControl(controller, store, controller.ApplySettings, logs.Add);
            ComboBox theme = Assert.IsType<ComboBox>(Assert.Single(control.Controls.Find("visualPack", true)));
            theme.SelectedItem = theme.Items.Cast<RadialVisualPackCatalogEntry>()
                .Single(pack => pack.Id == "dark-fantasy-radial8-v1");
            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings candidate));
            Assert.Equal("radial-8", candidate.MappingProfileId);
            Assert.Equal(8, control.MappingRowCount);

            Assert.True(control.TryApplyAndSave(showDialog: false), string.Join(Environment.NewLine, logs));

            Assert.Equal(candidate.NormalizeMappings(), controller.ConfiguredSettings.NormalizeMappings());
            Assert.Equal("radial-6", controller.ActiveSettings.MappingProfileId);
            Assert.Equal("radial-6", overlay.ActiveLayoutDefinition!.ProfileId);
            RadialMenuSettingsLoadResult reloaded = store.Load();
            Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, reloaded.Status);
            Assert.Equal(candidate.NormalizeMappings(), reloaded.Settings.NormalizeMappings());
            Assert.Equal("dark-fantasy-radial8-v1", reloaded.Settings.VisualPackId);
            Assert.Equal("radial-8", reloaded.Settings.MappingProfileId);
            Assert.DoesNotContain(logs, log => log.Contains("Runtime settings did not match", StringComparison.Ordinal));
            Assert.Equal(new[] { store.Path }, Directory.GetFiles(directory.Path));
            control.RefreshFromRuntime();
            Assert.Equal("dark-fantasy-radial8-v1", control.SelectedVisualPackId);
            Assert.Equal("radial-8", control.ActiveMappingProfileId);
            Assert.Equal(8, control.MappingRowCount);
            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings refreshed));
            Assert.Equal(candidate.NormalizeMappings(), refreshed.NormalizeMappings());
        });
    }

    private static void AssertNativePage(MainForm form, string page)
    {
        Assert.Equal(page == "Overview", Field<Control>(form, "_overviewCards").Visible);
        Assert.Equal(page == "Overview", Field<Control>(form, "_overviewOutput").Visible);
        Assert.Equal(page == "Gamepad", Field<Control>(form, "_gamepadMonitor").Visible);
        Assert.Equal(page == "Gamepad", Field<Control>(form, "_joystickDebugSection").Visible);
        Assert.Equal(page == "Settings", form.SettingsControl.Visible);
        Assert.Equal(page == "Log", Field<Control>(form, "_logSection").Visible);
        AssertNativeControlTree(form);
        if (page == "Settings")
        {
            TabControl tabs = Assert.IsType<TabControl>(Assert.Single(form.SettingsControl.Controls.Find("settingsTabs", true)));
            Assert.Equal(new[] { "基础", "动作映射" }, tabs.TabPages.Cast<TabPage>().Select(tab => tab.Text));
            Assert.Empty(form.SettingsControl.Controls.Find("advancedSettingsPage", true));
        }
    }

    private static void AssertNativeControlTree(MainForm form)
    {
        Assert.DoesNotContain(Descendants(form), control =>
            control.GetType().Namespace?.StartsWith("Microsoft.Web.WebView2", StringComparison.Ordinal) == true);
    }

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private static void Invoke(MainForm form, string name, params object[] args) =>
        typeof(MainForm).GetMethod(name, PrivateInstance)!.Invoke(form, args);

    private static void Navigate(MainForm form, string page) => Invoke(form, "NavigateTo",
        Enum.Parse(typeof(MainForm).GetField("_currentPage", PrivateInstance)!.FieldType, page));

    private static void RaiseServiceEvent<T>(Ds4Service service, string name, T value) =>
        Field<Action<T>>(service, name)(value);

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
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class NativeFormFixture : IDisposable
    {
        private readonly IsolatedSettingsDirectory _directory = new();
        public NativeFormFixture()
        {
            Store = new RadialMenuSettingsStore(_directory.SettingsPath);
            Assert.True(Store.TrySave(CrossProfileSettingsScenario.Original, out string error), error);
            OriginalBytes = File.ReadAllBytes(Store.Path);
            Service = new Ds4Service(new UnusedDirectDs4Factory(), new NoOpKeyboardOutput(), Keyboard);
            Form = new MainForm(Service, Store)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
        }
        public MainForm Form { get; }
        public Ds4Service Service { get; }
        public CountingKeyboardStore Keyboard { get; } = new();
        public RadialMenuSettingsStore Store { get; }
        public byte[] OriginalBytes { get; }
        public void Dispose()
        {
            typeof(MainForm).GetField("_isReallyClosing", PrivateInstance)!.SetValue(Form, true);
            Form.Close();
            Field<NotifyIcon>(Form, "_notifyIcon").Dispose();
            Form.Dispose();
            Service.Dispose();
            _directory.Dispose();
        }
    }

    private sealed class IsolatedSettingsDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "LeftPad.NativeReceiverProductionTests", Guid.NewGuid().ToString("N"));
        public IsolatedSettingsDirectory() => Directory.CreateDirectory(Path);
        public string SettingsPath => System.IO.Path.Combine(Path, "radial-menu-settings.json");
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class LegacyFrontendEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _original = new();
        public LegacyFrontendEnvironment(string? value)
        {
            foreach (string variable in new[] { "LEFTPAD_NATIVE_UI",
                "LEFTPAD_WEBVIEW2_OVERVIEW", "LEFTPAD_WEBVIEW2_CONTROLLER",
                "LEFTPAD_WEBVIEW2_SETTINGS", "LEFTPAD_WEBVIEW2_LOGS" })
            {
                _original[variable] = Environment.GetEnvironmentVariable(variable);
                Environment.SetEnvironmentVariable(variable,
                    variable == "LEFTPAD_NATIVE_UI" || value == null ? value : "1");
            }
        }
        public void Dispose()
        {
            foreach ((string variable, string? value) in _original)
                Environment.SetEnvironmentVariable(variable, value);
        }
    }

    private sealed class UnusedDirectDs4Factory : IDirectDs4Factory
    {
        public IDirectDs4Session Create() => throw new InvalidOperationException("No real output in Native UI tests.");
    }

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void SetKeyState(KeyboardKey key, bool isPressed) { }
    }

    private sealed class CountingKeyboardStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();
        public int SaveCalls { get; private set; }
        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings)
        {
            SaveCalls++;
            _bindings = bindings.Clone();
        }
    }
}

internal static class NativeReceiverTestThread
{
    internal static void Click(Control root, string name)
    {
        Button button = Assert.IsType<Button>(Assert.Single(root.Controls.Find(name, true)));
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, [EventArgs.Empty]);
    }

    internal static void Run(Action action)
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
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
