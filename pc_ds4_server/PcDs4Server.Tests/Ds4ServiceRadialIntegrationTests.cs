using System.Reflection;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class Ds4ServiceRadialIntegrationTests
{
    [Fact]
    public void DirectDs4_ActionDoubleTap_PassesFirstTapAndConsumesSecondTap()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 100, 120);

        Assert.Equal(
            new[] { (DualShock4Button.Cross, true), (DualShock4Button.Cross, false) },
            fixture.DirectDs4.ButtonEvents);
        Assert.Equal(2, fixture.DirectDs4.SubmitReportCalls);
        Assert.Single(RadialLogs(fixture.Logs), log =>
            log.Contains("[RADIAL] Action double-tap trigger: cross", StringComparison.Ordinal));
    }

    [Fact]
    public void Keyboard_ActionDoubleTap_PassesFirstTapAndConsumesSecondTap()
    {
        var fixture = KeyboardFixture();
        using Ds4Service service = fixture.Service;
        ConfigureBinding(service, "cross", KeyboardKey.Space);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 100, 120);

        Assert.Equal(
            new[] { (KeyboardKey.Space, true), (KeyboardKey.Space, false) },
            fixture.Keyboard.Events);
        Assert.Single(RadialLogs(fixture.Logs), log =>
            log.Contains("[RADIAL] Action double-tap trigger: cross", StringComparison.Ordinal));
    }

    [Fact]
    public void DirectDs4_SingleActionTap_RemainsSynchronousPassThrough()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;

        fixture.Clock.SetMilliseconds(0);
        service.ProcessProtocolAction("cross", "down");
        Assert.Equal((DualShock4Button.Cross, true), Assert.Single(fixture.DirectDs4.ButtonEvents));
        Assert.Equal(1, fixture.DirectDs4.SubmitReportCalls);

        fixture.Clock.SetMilliseconds(20);
        service.ProcessProtocolAction("cross", "up");
        Assert.Equal((DualShock4Button.Cross, false), fixture.DirectDs4.ButtonEvents.Last());
        Assert.Equal(2, fixture.DirectDs4.SubmitReportCalls);
        Assert.Empty(RadialLogs(fixture.Logs));
    }

    [Fact]
    public void Keyboard_SingleActionTap_RemainsSynchronousPassThrough()
    {
        var fixture = KeyboardFixture();
        using Ds4Service service = fixture.Service;
        ConfigureBinding(service, "cross", KeyboardKey.Space);

        fixture.Clock.SetMilliseconds(0);
        service.ProcessProtocolAction("cross", "down");
        Assert.Equal((KeyboardKey.Space, true), Assert.Single(fixture.Keyboard.Events));

        fixture.Clock.SetMilliseconds(20);
        service.ProcessProtocolAction("cross", "up");
        Assert.Equal((KeyboardKey.Space, false), fixture.Keyboard.Events.Last());
        Assert.Empty(RadialLogs(fixture.Logs));
    }

    [Fact]
    public void ActionTapsOutsideWindow_BothPassThroughWithoutTrigger()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 171, 190);

        Assert.Equal(
            new[]
            {
                (DualShock4Button.Cross, true),
                (DualShock4Button.Cross, false),
                (DualShock4Button.Cross, true),
                (DualShock4Button.Cross, false)
            },
            fixture.DirectDs4.ButtonEvents);
        Assert.Equal(4, fixture.DirectDs4.SubmitReportCalls);
        Assert.Empty(RadialLogs(fixture.Logs));
    }

    [Fact]
    public void ActionSecondTapWithin149Milliseconds_UsesProductionWindowAndTriggers()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 169, 190);

        Assert.Equal(
            new[] { (DualShock4Button.Cross, true), (DualShock4Button.Cross, false) },
            fixture.DirectDs4.ButtonEvents);
        Assert.Single(RadialLogs(fixture.Logs), log =>
            log.Contains("[RADIAL] Action double-tap trigger: cross", StringComparison.Ordinal));
    }

    [Fact]
    public void KeyboardActionTapsOutsideProductionWindow_BothPassThroughWithoutTrigger()
    {
        var fixture = KeyboardFixture();
        using Ds4Service service = fixture.Service;
        ConfigureBinding(service, "cross", KeyboardKey.Space);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 171, 190);

        Assert.Equal(
            new[]
            {
                (KeyboardKey.Space, true),
                (KeyboardKey.Space, false),
                (KeyboardKey.Space, true),
                (KeyboardKey.Space, false)
            },
            fixture.Keyboard.Events);
        Assert.Empty(RadialLogs(fixture.Logs));
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
    public void EveryMappedGameAction_CanTrigger(string protocolAction)
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;

        SendTap(service, fixture.Clock, protocolAction, 0, 20);
        SendTap(service, fixture.Clock, protocolAction, 100, 120);

        Assert.Single(RadialLogs(fixture.Logs), log =>
            log.Contains($"[RADIAL] Action double-tap trigger: {protocolAction}", StringComparison.Ordinal));
    }

    [Fact]
    public void DifferentActions_DoNotFormADoubleTap()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "circle", 100, 120);

        Assert.Equal(4, fixture.DirectDs4.ButtonEvents.Count);
        Assert.Equal(4, fixture.DirectDs4.SubmitReportCalls);
        Assert.Empty(RadialLogs(fixture.Logs));
    }

    [Fact]
    public void ReleaseAllBetweenActionTaps_PreventsTrigger()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;

        SendTap(service, fixture.Clock, "cross", 0, 20);
        service.ReleaseAllControls(Ds4ControlResetReason.Disconnect);
        SendTap(service, fixture.Clock, "cross", 100, 120);

        Assert.Equal(4, fixture.DirectDs4.ButtonEvents.Count);
        Assert.Empty(RadialLogs(fixture.Logs));
    }

    [Fact]
    public void NeutralMoveDoubleTap_TriggersOnlyOnSecondCompleteUp()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendMoveTap(service, fixture.Clock, 0, 20);
        fixture.Clock.SetMilliseconds(100);
        service.ProcessProtocolAction("move", "down");
        Assert.Empty(RadialLogs(fixture.Logs));

        fixture.Clock.SetMilliseconds(120);
        service.ProcessProtocolAction("move", "up");

        Assert.Single(RadialLogs(fixture.Logs), log =>
            log.Contains("[RADIAL] MOVE double-tap trigger", StringComparison.Ordinal));
        Assert.False(fixture.LastSnapshot.MovementLocked);
        Assert.Equal((128, 128), fixture.DirectDs4.LeftStickEvents.Last());
    }

    [Fact]
    public void MoveSecondDownWithin149Milliseconds_TriggersOnUpAfterLongHold()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendMoveTap(service, fixture.Clock, 0, 20);
        SendMoveTap(service, fixture.Clock, 169, 500);

        Assert.Single(RadialLogs(fixture.Logs), log =>
            log.Contains("[RADIAL] MOVE double-tap trigger", StringComparison.Ordinal));
    }

    [Fact]
    public void MoveSecondDownAfterProductionWindow_DoesNotTrigger()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendMoveTap(service, fixture.Clock, 0, 20);
        SendMoveTap(service, fixture.Clock, 171, 190);

        Assert.Empty(RadialLogs(fixture.Logs));
        Assert.False(fixture.LastSnapshot.MovementLocked);
        Assert.Equal((128, 128), fixture.DirectDs4.LeftStickEvents.Last());
    }

    [Fact]
    public void LockedMove_FirstCenterTapStopsAndSecondCenterTapTriggers()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        fixture.Clock.SetMilliseconds(0);
        service.ProcessProtocolAction("move", "down");
        ConfiguredController(service).UpdateCursor(new ScreenPoint(503, 500));
        fixture.Clock.SetMilliseconds(20);
        service.ProcessProtocolAction("move", "up");
        Assert.True(fixture.LastSnapshot.MovementLocked);

        SendMoveTap(service, fixture.Clock, 100, 120);
        Assert.False(fixture.LastSnapshot.MovementLocked);
        Assert.Equal((128, 128), fixture.DirectDs4.LeftStickEvents.Last());
        Assert.Empty(RadialLogs(fixture.Logs));

        SendMoveTap(service, fixture.Clock, 180, 200);
        Assert.False(fixture.LastSnapshot.MovementLocked);
        Assert.Equal((128, 128), fixture.DirectDs4.LeftStickEvents.Last());
        Assert.Single(RadialLogs(fixture.Logs), log =>
            log.Contains("[RADIAL] MOVE double-tap trigger", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DirectionalMoveInEitherRound_PreventsTrigger(bool directionalFirst)
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendMoveRound(service, fixture.Clock, 0, 20, directionalFirst);
        SendMoveRound(service, fixture.Clock, 100, 120, !directionalFirst);

        Assert.Empty(RadialLogs(fixture.Logs));
    }

    [Fact]
    public void MoveStopBetweenTaps_PreventsTrigger()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendMoveTap(service, fixture.Clock, 0, 20);
        fixture.Clock.SetMilliseconds(50);
        service.ProcessProtocolAction("move", "stop");
        SendMoveTap(service, fixture.Clock, 100, 120);

        Assert.Empty(RadialLogs(fixture.Logs));
    }

    [Fact]
    public void RepeatedMoveDown_DoesNotReplaceAcceptedSecondTapDown()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendMoveTap(service, fixture.Clock, 0, 20);
        fixture.Clock.SetMilliseconds(100);
        service.ProcessProtocolAction("move", "down");
        fixture.Clock.SetMilliseconds(110);
        service.ProcessProtocolAction("move", "down");
        fixture.Clock.SetMilliseconds(120);
        service.ProcessProtocolAction("move", "up");

        Assert.Single(RadialLogs(fixture.Logs), log =>
            log.Contains("[RADIAL] MOVE double-tap trigger", StringComparison.Ordinal));
    }

    [Fact]
    public void VirtualJoystickResetBetweenMoveTaps_PreventsTrigger()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendMoveTap(service, fixture.Clock, 0, 20);
        service.ResetVirtualJoystick(JoystickResetReason.Disconnect);
        SendMoveTap(service, fixture.Clock, 100, 120);

        Assert.Empty(RadialLogs(fixture.Logs));
    }

    [Fact]
    public void CursorSamplingResetBetweenActionTaps_PreventsTrigger()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendTap(service, fixture.Clock, "cross", 0, 20);
        fixture.Clock.SetMilliseconds(50);
        service.ProcessProtocolAction("move", "down");
        fixture.Cursor.Success = false;
        ConfiguredController(service).SampleCursor();
        fixture.Cursor.Success = true;
        SendTap(service, fixture.Clock, "cross", 100, 120);

        Assert.Equal(4, fixture.DirectDs4.ButtonEvents.Count);
        Assert.Empty(RadialLogs(fixture.Logs));
    }

    private static ServiceFixture DirectFixture()
    {
        var clock = new ManualClock();
        var factory = new FakeDirectDs4Factory();
        var keyboard = new FakeKeyboardOutput();
        var service = new Ds4Service(
            factory,
            keyboard,
            new MemoryBindingStore(),
            inputTimestampProvider: clock.GetTimestamp);
        Assert.True(service.Initialize());
        var logs = new List<string>();
        service.OnLog += logs.Add;
        return new ServiceFixture(service, clock, factory.Session, keyboard, logs);
    }

    private static ServiceFixture KeyboardFixture()
    {
        var clock = new ManualClock();
        var factory = new FakeDirectDs4Factory();
        var keyboard = new FakeKeyboardOutput();
        var service = new Ds4Service(
            factory,
            keyboard,
            new MemoryBindingStore(),
            inputTimestampProvider: clock.GetTimestamp);
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        var logs = new List<string>();
        service.OnLog += logs.Add;
        return new ServiceFixture(service, clock, factory.Session, keyboard, logs);
    }

    private static MoveServiceFixture MoveFixture()
    {
        ServiceFixture fixture = DirectFixture();
        var cursor = new FakeCursor { Position = new ScreenPoint(500, 500) };
        fixture.Service.ConfigureVirtualJoystick(new FakeOverlay(), cursor);
        VirtualJoystickSnapshot lastSnapshot = ConfiguredController(fixture.Service).Snapshot;
        var moveFixture = new MoveServiceFixture(fixture, cursor, lastSnapshot);
        fixture.Service.OnJoystickStateChanged += snapshot => moveFixture.LastSnapshot = snapshot;
        return moveFixture;
    }

    private static void ConfigureBinding(Ds4Service service, string action, KeyboardKey key)
    {
        KeyboardBindings bindings = service.KeyboardBindings;
        bindings.Set(action, key);
        Assert.True(service.TryUpdateKeyboardBindings(bindings));
    }

    private static void SendTap(
        Ds4Service service,
        ManualClock clock,
        string action,
        int downMilliseconds,
        int upMilliseconds)
    {
        clock.SetMilliseconds(downMilliseconds);
        service.ProcessProtocolAction(action, "down");
        clock.SetMilliseconds(upMilliseconds);
        service.ProcessProtocolAction(action, "up");
    }

    private static void SendMoveTap(
        Ds4Service service,
        ManualClock clock,
        int downMilliseconds,
        int upMilliseconds) =>
        SendMoveRound(service, clock, downMilliseconds, upMilliseconds, directional: false);

    private static void SendMoveRound(
        Ds4Service service,
        ManualClock clock,
        int downMilliseconds,
        int upMilliseconds,
        bool directional)
    {
        clock.SetMilliseconds(downMilliseconds);
        service.ProcessProtocolAction("move", "down");
        if (directional)
            ConfiguredController(service).UpdateCursor(new ScreenPoint(503, 500));
        clock.SetMilliseconds(upMilliseconds);
        service.ProcessProtocolAction("move", "up");
    }

    private static VirtualJoystickController ConfiguredController(Ds4Service service)
    {
        FieldInfo field = typeof(Ds4Service).GetField("_joystick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<VirtualJoystickController>(field.GetValue(service));
    }

    private static IEnumerable<string> RadialLogs(IEnumerable<string> logs) =>
        logs.Where(log => log.Contains("[RADIAL]", StringComparison.Ordinal));

    private sealed class ManualClock
    {
        private TimeSpan _timestamp;
        public TimeSpan GetTimestamp() => _timestamp;
        public void SetMilliseconds(int milliseconds) =>
            _timestamp = TimeSpan.FromMilliseconds(milliseconds);
    }

    private sealed record ServiceFixture(
        Ds4Service Service,
        ManualClock Clock,
        FakeDirectDs4Session DirectDs4,
        FakeKeyboardOutput Keyboard,
        List<string> Logs);

    private sealed class MoveServiceFixture
    {
        private readonly ServiceFixture _fixture;

        public MoveServiceFixture(
            ServiceFixture fixture,
            FakeCursor cursor,
            VirtualJoystickSnapshot lastSnapshot)
        {
            _fixture = fixture;
            Cursor = cursor;
            LastSnapshot = lastSnapshot;
        }

        public Ds4Service Service => _fixture.Service;
        public ManualClock Clock => _fixture.Clock;
        public FakeDirectDs4Session DirectDs4 => _fixture.DirectDs4;
        public List<string> Logs => _fixture.Logs;
        public FakeCursor Cursor { get; }
        public VirtualJoystickSnapshot LastSnapshot { get; set; }
    }

    private sealed class FakeDirectDs4Factory : IDirectDs4Factory
    {
        public FakeDirectDs4Session Session { get; } = new();
        public IDirectDs4Session Create() => Session;
    }

    private sealed class FakeDirectDs4Session : IDirectDs4Session
    {
        public List<(DualShock4Button Button, bool Pressed)> ButtonEvents { get; } = new();
        public List<(DualShock4Slider Trigger, byte Value)> TriggerEvents { get; } = new();
        public List<(byte X, byte Y)> LeftStickEvents { get; } = new();
        public int SubmitReportCalls { get; private set; }

        public void SetButton(DualShock4Button button, bool pressed) => ButtonEvents.Add((button, pressed));
        public void SetTrigger(DualShock4Slider trigger, byte value) => TriggerEvents.Add((trigger, value));
        public void SetLeftStick(byte x, byte y) => LeftStickEvents.Add((x, y));
        public void SubmitReport() => SubmitReportCalls++;
        public void Dispose() { }
    }

    private sealed class FakeKeyboardOutput : IKeyboardOutput
    {
        public List<(KeyboardKey Key, bool Pressed)> Events { get; } = new();
        public void SetKeyState(KeyboardKey key, bool isPressed) => Events.Add((key, isPressed));
    }

    private sealed class MemoryBindingStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();
        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
    }

    private sealed class FakeCursor : ICursorPositionProvider
    {
        public ScreenPoint Position { get; set; }
        public bool Success { get; set; } = true;

        public bool TryGetPosition(out ScreenPoint position, out int win32Error)
        {
            position = Position;
            win32Error = Success ? 0 : 123;
            return Success;
        }
    }

    private sealed class FakeOverlay : IJoystickOverlay
    {
        public bool IsVisible { get; private set; }
        public void Show(ScreenPoint center) => IsVisible = true;
        public void UpdateKnob(double x, double y) { }
        public void Hide() => IsVisible = false;
    }
}
