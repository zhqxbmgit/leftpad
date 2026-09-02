using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class RadialMouseDismissTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Tracker_DismissesExactlyOnceOnFalseToTrueEdge()
    {
        var tracker = new RadialMouseDismissTracker();
        tracker.Reset(initialLeftButtonDown: false);

        Assert.False(tracker.Observe(currentLeftButtonDown: false));
        Assert.True(tracker.Observe(currentLeftButtonDown: true));
        Assert.False(tracker.Observe(currentLeftButtonDown: true));
    }

    [Fact]
    public void Tracker_WhenOpenedWhileHeld_WaitsForReleaseAndNextPress()
    {
        var tracker = new RadialMouseDismissTracker();
        tracker.Reset(initialLeftButtonDown: true);

        Assert.False(tracker.Observe(currentLeftButtonDown: true));
        Assert.False(tracker.Observe(currentLeftButtonDown: true));
        Assert.False(tracker.Observe(currentLeftButtonDown: false));
        Assert.True(tracker.Observe(currentLeftButtonDown: true));
    }

    [Fact]
    public void SelectedSlot_LeftClickTickClosesWithoutCompletionOrMappingExecution()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            int confirmations = 0;
            fixture.Service.RadialMenuConfirmationRequested += _ => confirmations++;
            fixture.OpenActionMenu();
            Assert.True(fixture.Controller.UpdateSelectionForCursor(new Point(550, 550)));
            Assert.Equal(3, fixture.Controller.SelectedSlot);
            int keyboardEventsBeforeDismiss = fixture.Keyboard.Events.Count;

            fixture.Mouse.IsLeftDown = true;
            fixture.TickSelectionTimer();

            AssertClosedAndSynchronized(fixture);
            Assert.Equal(0, confirmations);
            Assert.Equal(keyboardEventsBeforeDismiss, fixture.Keyboard.Events.Count);
            Assert.Equal(0, fixture.DirectFactory.CreateCalls);
            Assert.DoesNotContain("[环形菜单] 已执行", fixture.LogText, StringComparison.Ordinal);
            Assert.DoesNotContain("[环形菜单] 已确认", fixture.LogText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Center_LeftClickClosesWithoutCompletion()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            int confirmations = 0;
            fixture.Service.RadialMenuConfirmationRequested += _ => confirmations++;
            fixture.OpenActionMenu();
            Assert.Equal(0, fixture.Controller.SelectedSlot);

            Assert.True(fixture.Form.ProcessRadialMouseDismiss(leftButtonDown: true));

            AssertClosedAndSynchronized(fixture);
            Assert.Equal(0, confirmations);
            Assert.Empty(fixture.Keyboard.Events);
        });
    }

    [Fact]
    public void LeftDismiss_AllowsActionDoubleTapToReopen()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.OpenActionMenu();
            Assert.True(fixture.Form.ProcessRadialMouseDismiss(leftButtonDown: true));
            AssertClosedAndSynchronized(fixture);
            fixture.Mouse.IsLeftDown = false;

            fixture.SendActionTap(200, 220);
            fixture.SendActionTap(280, 300);

            Assert.True(fixture.Controller.IsNormalMenuOpen);
            Assert.Equal(RadialTriggerSource.ForAction("cross"), fixture.Controller.OpenSource);
            Assert.True(fixture.Timer.Enabled);
            Assert.Equal(RadialTriggerSource.ForAction("cross"), fixture.ServiceOpenSource);
        });
    }

    [Fact]
    public void ExistingActionConfirm_StillExecutesSelectedSlotOnce()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.OpenActionMenu();
            Assert.True(fixture.Controller.UpdateSelectionForCursor(new Point(550, 550)));

            fixture.Clock.SetMilliseconds(200);
            fixture.Service.ProcessProtocolAction("cross", "down");
            fixture.Clock.SetMilliseconds(220);
            fixture.Service.ProcessProtocolAction("cross", "up");

            AssertClosedAndSynchronized(fixture);
            Assert.Equal(
                new[] { (KeyboardKey.F1, true), (KeyboardKey.F1, false) },
                fixture.Keyboard.Events);
            Assert.Equal(1, CountOccurrences(fixture.LogText, "[环形菜单] 已执行：Slot 3"));
        });
    }

    [Fact]
    public void ExistingMoveConfirm_StillExecutesSelectedSlotOnce()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.OpenMoveMenu();
            Assert.True(fixture.Controller.UpdateSelectionForCursor(new Point(550, 550)));

            fixture.Clock.SetMilliseconds(200);
            fixture.Service.ProcessProtocolAction("move", "down");
            fixture.Clock.SetMilliseconds(220);
            fixture.Service.ProcessProtocolAction("move", "up");

            AssertClosedAndSynchronized(fixture);
            Assert.Equal(
                new[] { (KeyboardKey.F1, true), (KeyboardKey.F1, false) },
                fixture.Keyboard.Events);
            Assert.Equal(1, CountOccurrences(fixture.LogText, "[环形菜单] 已执行：Slot 3"));
        });
    }

    [Fact]
    public void Preview_LeftClickDoesNotEnterGameplayDismissPath()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Controller.PreviewAt(new Point(500, 500), fixture.Settings);
            Assert.True(fixture.Controller.IsPreviewActive);
            Assert.False(fixture.Controller.IsNormalMenuOpen);

            Assert.False(fixture.Form.ProcessRadialMouseDismiss(leftButtonDown: true));

            Assert.True(fixture.Controller.IsPreviewActive);
            Assert.True(fixture.Controller.IsOpen);
            Assert.False(fixture.Timer.Enabled);
            Assert.Null(fixture.ServiceOpenSource);
        });
    }

    [Fact]
    public void ClosedMenu_LeftClicksDoNotMutateRadialStateOrLogs()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            string logsBefore = fixture.LogText;

            Assert.False(fixture.Form.ProcessRadialMouseDismiss(leftButtonDown: false));
            Assert.False(fixture.Form.ProcessRadialMouseDismiss(leftButtonDown: true));

            Assert.False(fixture.Controller.IsOpen);
            Assert.Equal(0, fixture.Controller.SelectedSlot);
            Assert.Null(fixture.Controller.OpenSource);
            Assert.Null(fixture.ServiceOpenSource);
            Assert.False(fixture.Timer.Enabled);
            Assert.Equal(logsBefore, fixture.LogText);
            Assert.Empty(fixture.Keyboard.Events);
        });
    }

    private static void AssertClosedAndSynchronized(Fixture fixture)
    {
        Assert.False(fixture.Controller.IsNormalMenuOpen);
        Assert.False(fixture.Controller.IsOpen);
        Assert.Equal(0, fixture.Controller.SelectedSlot);
        Assert.Null(fixture.Controller.OpenSource);
        Assert.Null(fixture.ServiceOpenSource);
        Assert.False(fixture.Timer.Enabled);
    }

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

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

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "LeftPad.RadialMouseDismissTests",
            Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            Settings = RadialMenuSettings.SafeFallback with
            {
                SlotMappings = RadialMenuSettings.SafeFallback.SlotMappings.WithSlot(
                    3,
                    new RadialSlotMapping
                    {
                        Kind = RadialActionKind.KeyboardKey,
                        Key = KeyboardKey.F1
                    })
            };
            Store = new RadialMenuSettingsStore(Path.Combine(_directory, "radial-menu-settings.json"));
            Assert.True(Store.TrySave(Settings, out string error), error);
            Service = new Ds4Service(
                DirectFactory,
                Keyboard,
                new MemoryKeyboardStore(),
                inputTimestampProvider: Clock.GetTimestamp,
                radialDs4Delay: _ => { });
            Assert.True(Service.TrySetOutputMode(OutputMode.Keyboard));
            Form = new MainForm(Service, Store, () => Mouse.IsLeftDown);
        }

        public RadialMenuSettings Settings { get; }
        public RadialMenuSettingsStore Store { get; }
        public ManualClock Clock { get; } = new();
        public MouseState Mouse { get; } = new();
        public CountingKeyboardOutput Keyboard { get; } = new();
        public CountingDirectDs4Factory DirectFactory { get; } = new();
        public Ds4Service Service { get; }
        public MainForm Form { get; }
        public RadialMenuController Controller => Field<RadialMenuController>(Form, "_radialMenu");
        public System.Windows.Forms.Timer Timer =>
            Field<System.Windows.Forms.Timer>(Form, "_radialSelectionTimer");
        public RadialTriggerSource? ServiceOpenSource =>
            Field<RadialTriggerSource?>(Service, "_radialOpenSource");
        public string LogText => Field<RichTextBox>(Form, "_logBox").Text;

        public void OpenActionMenu()
        {
            SendActionTap(0, 20);
            SendActionTap(100, 120);
            Assert.True(Controller.IsNormalMenuOpen);
        }

        public void OpenMoveMenu()
        {
            typeof(Ds4Service).GetMethod("OpenRadialMenu", PrivateInstance)!
                .Invoke(Service, [RadialTriggerSource.Move]);
            Assert.True(Controller.IsNormalMenuOpen);
        }

        public void SendActionTap(int downMilliseconds, int upMilliseconds)
        {
            Clock.SetMilliseconds(downMilliseconds);
            Service.ProcessProtocolAction("cross", "down");
            Clock.SetMilliseconds(upMilliseconds);
            Service.ProcessProtocolAction("cross", "up");
        }

        public void TickSelectionTimer() =>
            typeof(MainForm).GetMethod("RadialSelectionTimer_Tick", PrivateInstance)!
                .Invoke(Form, [null, EventArgs.Empty]);

        public void Dispose()
        {
            typeof(MainForm).GetField("_isReallyClosing", PrivateInstance)!.SetValue(Form, true);
            Form.Close();
            if (!Form.IsDisposed) Form.Dispose();
            Field<NotifyIcon>(Form, "_notifyIcon").Dispose();
            Service.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private sealed class MouseState
    {
        public bool IsLeftDown { get; set; }
    }

    private sealed class ManualClock
    {
        private TimeSpan _timestamp;
        public TimeSpan GetTimestamp() => _timestamp;
        public void SetMilliseconds(int milliseconds) =>
            _timestamp = TimeSpan.FromMilliseconds(milliseconds);
    }

    private sealed class CountingKeyboardOutput : IKeyboardOutput
    {
        public List<(KeyboardKey Key, bool Pressed)> Events { get; } = new();
        public void SetKeyState(KeyboardKey key, bool isPressed) => Events.Add((key, isPressed));
    }

    private sealed class MemoryKeyboardStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();
        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
    }

    private sealed class CountingDirectDs4Factory : IDirectDs4Factory
    {
        public int CreateCalls { get; private set; }
        public IDirectDs4Session Create()
        {
            CreateCalls++;
            throw new InvalidOperationException("Direct DS4 must not be created by mouse-dismiss tests.");
        }
    }
}
