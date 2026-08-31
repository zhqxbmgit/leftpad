using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class RuntimeUiStateCorrectionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly ITestOutputHelper _output;

    public RuntimeUiStateCorrectionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TriangleDown_Stop_ReleasesOutputAndResetsNativePresentation()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(initializeDirectDs4: true);
            fixture.Service.ProcessProtocolAction("triangle", "down");
            Assert.True(ButtonStates(fixture.Form)["triangle"]);

            fixture.Service.Stop();

            AssertControllerButtonsNeutral(fixture.Form);
            Assert.Equal(
                [(DualShock4Button.Triangle, true), (DualShock4Button.Triangle, false)],
                fixture.Factory.Session.ButtonEvents);
        });
    }

    [Theory]
    [InlineData(Ds4ControlResetReason.Disconnect)]
    [InlineData(Ds4ControlResetReason.SessionReplacement)]
    [InlineData(Ds4ControlResetReason.OutputFailure)]
    public void InputAuthorityLoss_ResetsAllPressedButtons(Ds4ControlResetReason reason)
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();
            fixture.Service.ProcessProtocolAction("triangle", "down");
            fixture.Service.ProcessProtocolAction("cross", "down");
            fixture.Service.ProcessProtocolAction("circle", "down");
            Assert.True(ButtonStates(fixture.Form).Values.Count(value => value) >= 3);

            fixture.Service.ReleaseAllControls(reason);

            AssertControllerButtonsNeutral(fixture.Form);
        });
    }

    [Fact]
    public void Disconnect_ClearsCross_AndNewSessionInputStillDisplaysNormally()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();
            fixture.Service.ProcessProtocolAction("cross", "down");
            Assert.True(ButtonStates(fixture.Form)["cross"]);

            fixture.Service.ReleaseAllControls(Ds4ControlResetReason.Disconnect);
            Assert.False(ButtonStates(fixture.Form)["cross"]);

            fixture.Service.ProcessProtocolAction("square", "down");
            Assert.True(ButtonStates(fixture.Form)["square"]);
        });
    }

    [Fact]
    public void StopThenRestart_DoesNotInheritStaleState()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(initializeDirectDs4: true);
            fixture.Service.Start();
            fixture.Service.ProcessProtocolAction("circle", "down");

            fixture.Service.Stop();
            AssertControllerButtonsNeutral(fixture.Form);
            Assert.True(fixture.Service.Initialize());
            fixture.Service.Start();
            AssertControllerButtonsNeutral(fixture.Form);

            fixture.Service.ProcessProtocolAction("triangle", "down");
            Assert.True(ButtonStates(fixture.Form)["triangle"]);
            Assert.False(ButtonStates(fixture.Form)["circle"]);
        });
    }

    [Fact]
    public void NormalButtonDownAndUpBehavior_IsUnchanged()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();

            fixture.Service.ProcessProtocolAction("triangle", "down");
            Assert.True(ButtonStates(fixture.Form)["triangle"]);

            fixture.Service.ProcessProtocolAction("triangle", "up");
            Assert.False(ButtonStates(fixture.Form)["triangle"]);
        });
    }

    [Fact]
    public void Stop_NeutralizesRealtimeMoveProjectionWithoutChangingConnectionState()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();
            var changed = (Action<VirtualJoystickSnapshot>)typeof(Ds4Service)
                .GetField("OnJoystickStateChanged", PrivateInstance)!.GetValue(fixture.Service)!;
            changed(ActiveJoystickSnapshot());
            Label debug = Field<Label>(fixture.Form, "_joystickDebug");
            string connection = Field<ModernStatusCard>(fixture.Form, "_cardPhone").Value;
            Assert.Contains("MOVE：按住", debug.Text);
            Assert.Contains("模式：实时", debug.Text);
            Assert.Contains("本次按住已捕获方向：是", debug.Text);

            fixture.Service.Stop();

            foreach (string text in new[] { "MOVE：已释放", "模式：已停止", "光标采样：未启用",
                "本次按住已捕获方向：否", "移动锁定：否", "当前方向：0.000 / 0.000",
                "锁定方向：0.000 / 0.000", "摇杆：0.000 / 0.000", "DS4：128 / 128",
                "中心：- / -", "当前光标：- / -" })
                Assert.Contains(text, debug.Text);
            Assert.Equal(connection, Field<ModernStatusCard>(fixture.Form, "_cardPhone").Value);
        });
    }

    [Fact]
    public void PresentationReset_DoesNotEmitAdditionalDirectDs4Output()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(initializeDirectDs4: true);
            fixture.Service.ProcessProtocolAction("triangle", "down");
            int outputEventsBeforePresentationReset = fixture.Factory.Session.TotalOutputEvents;

            Invoke(fixture.Form, "ResetControllerPresentationState");

            Assert.Equal(outputEventsBeforePresentationReset, fixture.Factory.Session.TotalOutputEvents);
            AssertControllerButtonsNeutral(fixture.Form);
        });
    }

    [Fact]
    public void OutputFailure_StillInvalidatesPresentationWhenPhysicalReleaseThrows()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(initializeDirectDs4: true);
            fixture.Service.ProcessProtocolAction("triangle", "down");
            fixture.Factory.Session.ThrowOnButtonRelease = true;

            Assert.Throws<InvalidOperationException>(() =>
                fixture.Service.ReleaseAllControls(Ds4ControlResetReason.OutputFailure));

            AssertControllerButtonsNeutral(fixture.Form);
        });
    }

    [Fact]
    public void KeyboardRelease_EmitsOnlyActualUpAndResetsPresentation()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();
            Assert.True(fixture.Service.TrySetOutputMode(OutputMode.Keyboard));
            KeyboardBindings bindings = fixture.Service.KeyboardBindings;
            bindings.Cross = KeyboardKey.Space;
            Assert.True(fixture.Service.TryUpdateKeyboardBindings(bindings));

            fixture.Service.ProcessProtocolAction("cross", "down");
            fixture.Service.ReleaseAllControls(Ds4ControlResetReason.Disconnect);

            Assert.Equal(
                [(KeyboardKey.Space, true), (KeyboardKey.Space, false)],
                fixture.Keyboard.Events);
            AssertControllerButtonsNeutral(fixture.Form);
        });
    }

    [Fact]
    public void ResetNotification_FromWorkerThread_IsMarshaledToUiThread()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();
            _ = fixture.Form.Handle;
            fixture.Service.ProcessProtocolAction("cross", "down");
            Assert.True(ButtonStates(fixture.Form)["cross"]);

            Task.Run(() => fixture.Service.ReleaseAllControls(Ds4ControlResetReason.Disconnect))
                .GetAwaiter()
                .GetResult();
            var timeout = Stopwatch.StartNew();
            while (ButtonStates(fixture.Form)["cross"] && timeout.Elapsed < TimeSpan.FromSeconds(5))
                Application.DoEvents();

            AssertControllerButtonsNeutral(fixture.Form);
        });
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(300, 300, 1, 300)]
    [InlineData(301, 300, 2, 301)]
    [InlineData(1000, 300, 701, 1000)]
    [InlineData(5000, 300, 4701, 5000)]
    public void BoundedLogProjection_RetainsExactLastThreeHundred(
        int appended,
        int expectedCount,
        long expectedFirst,
        long expectedLast)
    {
        var buffer = new ReceiverLogBuffer();
        var stopwatch = Stopwatch.StartNew();
        for (int sequence = 1; sequence <= appended; sequence++)
            buffer.AppendWithEviction($"line {sequence}");

        IReadOnlyList<ReceiverLogEntry> entries = buffer.GetEntries();
        string nativeText = NativeLogProjection.CreateText(entries);
        stopwatch.Stop();

        Assert.Equal(expectedCount, entries.Count);
        if (expectedCount == 0)
        {
            Assert.Empty(nativeText);
        }
        else
        {
            Assert.Equal(expectedFirst, entries[0].SequenceId);
            Assert.Equal(expectedLast, entries[^1].SequenceId);
            Assert.Equal($"line {expectedFirst}", entries[0].RawText);
            Assert.Equal($"line {expectedLast}", entries[^1].RawText);
            Assert.Equal(
                string.Concat(entries.Select(entry => entry.RawText + Environment.NewLine)),
                nativeText);
        }

        _output.WriteLine(
            $"append={appended}; count={entries.Count}; first={expectedFirst}; " +
            $"last={expectedLast}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}");
    }

    [Fact]
    public void LogProjection_PreservesDuplicatesUnicodePathsLongBlankWhitespaceAndMarkup()
    {
        string longLine = new('长', 16_384);
        string[] values =
        [
            "duplicate",
            "duplicate",
            "中文与 emoji 🎮✨",
            @"C:\Users\zhq\LeftPad\logs\receiver.log",
            longLine,
            string.Empty,
            "   \t",
            "<script>alert(1)</script>"
        ];
        var buffer = new ReceiverLogBuffer();
        foreach (string value in values)
            buffer.AppendWithEviction(value);

        IReadOnlyList<ReceiverLogEntry> entries = buffer.GetEntries();

        Assert.Equal(values, entries.Select(entry => entry.RawText));
        Assert.Equal(2, entries.Count(entry => entry.RawText == "duplicate"));
        Assert.Equal(
            string.Concat(values.Select(value => value + Environment.NewLine)),
            NativeLogProjection.CreateText(entries));
    }

    [Fact]
    public void NativeRichTextProjection_PreservesSpecialContentAsPlainText()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();
            string[] specialValues =
            [
                "duplicate",
                "duplicate",
                "中文与 emoji 🎮✨",
                @"C:\Users\zhq\LeftPad\logs\receiver.log",
                new string('L', 16_384),
                string.Empty,
                "   \t",
                "<script>alert(1)</script>"
            ];
            for (int sequence = 1; sequence <= 310; sequence++)
                Invoke(fixture.Form, "AppendLog", $"filler {sequence}");
            foreach (string value in specialValues)
                Invoke(fixture.Form, "AppendLog", value);

            IReadOnlyList<ReceiverLogEntry> entries = LogEntries(fixture.Form);
            RichTextBox logBox = Field<RichTextBox>(fixture.Form, "_logBox");
            Assert.Equal(specialValues, entries.TakeLast(specialValues.Length)
                .Select(entry => entry.RawText));
            Assert.Equal(
                NativeLogProjection.CreateText(entries).Replace("\r\n", "\n", StringComparison.Ordinal),
                logBox.Text);
        });
    }

    [Fact]
    public void NativeRuntime_UsesBoundedBufferForOneThousandAndFiveThousandLogs()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();
            fixture.Form.Show();
            Assert.True(fixture.Form.IsHandleCreated);
            Assert.True(fixture.Form.Enabled);
            Assert.Equal(1, Application.OpenForms.Cast<Form>().Count(form => form.TopLevel && form.Visible));

            var stopwatch = Stopwatch.StartNew();
            for (int sequence = 1; sequence <= 1000; sequence++)
                Invoke(fixture.Form, "AppendLog", $"runtime line {sequence}");
            TimeSpan thousandElapsed = stopwatch.Elapsed;
            AssertNativeLogWindow(fixture.Form, 701, 1000);

            for (int sequence = 1001; sequence <= 5000; sequence++)
                Invoke(fixture.Form, "AppendLog", $"runtime line {sequence}");
            stopwatch.Stop();
            AssertNativeLogWindow(fixture.Form, 4701, 5000);

            fixture.Service.ProcessProtocolAction("triangle", "down");
            fixture.Service.Stop();
            AssertControllerButtonsNeutral(fixture.Form);

            _output.WriteLine(
                $"native-1000 elapsedMs={thousandElapsed.TotalMilliseconds:F3}; " +
                $"native-5000 cumulativeElapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}");
        });
    }

    [Fact]
    public void NativeLogs_BeforeHandleCreationUseTheSameBoundedBuffer()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture();

            for (int sequence = 1; sequence <= 1000; sequence++)
                Invoke(fixture.Form, "AppendLog", $"native line {sequence}");
            IReadOnlyList<ReceiverLogEntry> entries = LogEntries(fixture.Form);
            RichTextBox logBox = Field<RichTextBox>(fixture.Form, "_logBox");
            Assert.Equal(300, entries.Count);
            Assert.Equal("native line 701", entries[0].RawText);
            Assert.Equal("native line 1000", entries[^1].RawText);
            Assert.Equal(
                NativeLogProjection.CreateText(entries).Replace("\r\n", "\n", StringComparison.Ordinal),
                logBox.Text);

            fixture.Service.ProcessProtocolAction("cross", "down");
            Assert.True(ButtonStates(fixture.Form)["cross"]);
            fixture.Service.ReleaseAllControls(Ds4ControlResetReason.Disconnect);
            AssertControllerButtonsNeutral(fixture.Form);
        });
    }

    private static void AssertNativeLogWindow(MainForm form, int expectedFirst, int expectedLast)
    {
        IReadOnlyList<ReceiverLogEntry> entries = LogEntries(form);
        RichTextBox logBox = Field<RichTextBox>(form, "_logBox");

        Assert.Equal(ReceiverLogBuffer.MaximumEntries, entries.Count);
        Assert.Equal($"runtime line {expectedFirst}", entries[0].RawText);
        Assert.Equal($"runtime line {expectedLast}", entries[^1].RawText);
        Assert.Equal(
            NativeLogProjection.CreateText(entries).Replace("\r\n", "\n", StringComparison.Ordinal),
            logBox.Text);
    }

    private static void AssertControllerButtonsNeutral(MainForm form)
    {
        Assert.All(ButtonStates(form), pair => Assert.False(pair.Value, pair.Key));
    }

    private static Dictionary<string, bool> ButtonStates(MainForm form) =>
        Field<Dictionary<string, bool>>(form, "_btnStates");

    private static IReadOnlyList<ReceiverLogEntry> LogEntries(MainForm form) =>
        Field<ReceiverLogBuffer>(form, "_receiverLogBuffer").GetEntries();

    private static T Field<T>(MainForm form, string name) where T : class =>
        Assert.IsType<T>(typeof(MainForm).GetField(name, PrivateInstance)?.GetValue(form));

    private static object? Invoke(MainForm form, string name, params object?[] arguments)
    {
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(
            typeof(MainForm).GetMethod(name, PrivateInstance));
        return method.Invoke(form, arguments);
    }

    private static VirtualJoystickSnapshot ActiveJoystickSnapshot() => new(
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
        CurrentDirectionX: 0.707,
        CurrentDirectionY: -0.707,
        LockedDirectionX: 0.5,
        LockedDirectionY: -0.5,
        LogicalKnobX: 18.4,
        LogicalKnobY: -18.4,
        StickX: 0.707,
        StickY: -0.707,
        Ds4X: 218,
        Ds4Y: 38,
        OverlayVisible: true,
        LastResetReason: null);

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
        public MainFormFixture(bool initializeDirectDs4 = false)
        {
            Factory = new FakeDirectDs4Factory();
            Keyboard = new FakeKeyboardOutput();
            Service = new Ds4Service(Factory, Keyboard, new MemoryKeyboardBindingStore());
            string settingsPath = Path.Combine(
                Path.GetTempPath(),
                "leftpad-runtime-ui-state-tests",
                Guid.NewGuid().ToString("N"),
                "radial-menu-settings.json");
            Form = new MainForm(Service, new RadialMenuSettingsStore(settingsPath));
            if (initializeDirectDs4)
                Assert.True(Service.Initialize());
        }

        public FakeDirectDs4Factory Factory { get; }
        public FakeKeyboardOutput Keyboard { get; }
        public Ds4Service Service { get; }
        public MainForm Form { get; }

        public void Dispose()
        {
            Form.Dispose();
            Service.Dispose();
        }
    }

    private sealed class FakeDirectDs4Factory : IDirectDs4Factory
    {
        public FakeDirectDs4Session Session { get; private set; } = new();

        public IDirectDs4Session Create()
        {
            Session = new FakeDirectDs4Session();
            return Session;
        }
    }

    private sealed class FakeDirectDs4Session : IDirectDs4Session
    {
        public List<(DualShock4Button Button, bool Pressed)> ButtonEvents { get; } = [];
        public bool ThrowOnButtonRelease { get; set; }
        public int DPadEvents { get; private set; }
        public int TriggerEvents { get; private set; }
        public int StickEvents { get; private set; }
        public int SubmitEvents { get; private set; }
        public int TotalOutputEvents =>
            ButtonEvents.Count + DPadEvents + TriggerEvents + StickEvents + SubmitEvents;

        public void SetButton(DualShock4Button button, bool pressed)
        {
            ButtonEvents.Add((button, pressed));
            if (!pressed && ThrowOnButtonRelease)
            {
                ThrowOnButtonRelease = false;
                throw new InvalidOperationException("injected release failure");
            }
        }
        public void SetDPadDirection(DualShock4DPadDirection direction) => DPadEvents++;
        public void SetTrigger(DualShock4Slider trigger, byte value) => TriggerEvents++;
        public void SetLeftStick(byte x, byte y) => StickEvents++;
        public void SubmitReport() => SubmitEvents++;
        public void Dispose() { }
    }

    private sealed class FakeKeyboardOutput : IKeyboardOutput
    {
        public List<(KeyboardKey Key, bool Pressed)> Events { get; } = [];

        public void SetKeyState(KeyboardKey key, bool isPressed) => Events.Add((key, isPressed));
    }

    private sealed class MemoryKeyboardBindingStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();

        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
    }
}
