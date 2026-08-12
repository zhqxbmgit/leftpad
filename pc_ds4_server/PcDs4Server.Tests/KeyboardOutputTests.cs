using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class KeyboardOutputTests
{
    [Fact]
    public void DefaultOutputMode_IsDirectDs4()
    {
        using var service = Service(out _, out _);
        Assert.Equal(OutputMode.DirectDs4, service.OutputMode);
    }

    [Fact]
    public void KeyboardInitialize_DoesNotCreateVigem()
    {
        using var service = Service(out FakeDirectDs4Factory factory, out _);
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        Assert.True(service.Initialize());
        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public void DirectDs4Initialize_CreatesDs4()
    {
        using var service = Service(out FakeDirectDs4Factory factory, out _);
        Assert.True(service.Initialize());
        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public void RunningServer_CannotSwitchOutputMode()
    {
        using var service = Service(out _, out _);
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        Assert.True(service.Initialize());
        service.Start();
        Assert.False(service.TrySetOutputMode(OutputMode.DirectDs4));
        Assert.Equal(OutputMode.Keyboard, service.OutputMode);
        service.Stop();
        Assert.True(service.TrySetOutputMode(OutputMode.DirectDs4));
    }

    [Fact]
    public void RunningServer_CannotChangeKeyboardBindings()
    {
        using var service = Service(out _, out _);
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        Assert.True(service.Initialize());
        service.Start();
        var changed = service.KeyboardBindings;
        changed.Cross = KeyboardKey.Enter;
        Assert.False(service.TryUpdateKeyboardBindings(changed));
        Assert.Equal(KeyboardKey.None, service.KeyboardBindings.Cross);
    }

    [Theory]
    [InlineData(OutputMode.DirectDs4, false, false)]
    [InlineData(OutputMode.Keyboard, false, true)]
    [InlineData(OutputMode.Keyboard, true, false)]
    public void KeyboardMappingUi_IsEnabledOnlyForStoppedKeyboardMode(
        OutputMode mode,
        bool running,
        bool expectedEnabled)
    {
        Assert.Equal(expectedEnabled, MainForm.ShouldEnableKeyboardMappings(mode, running));
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void MoveQuantization_MapsEightDirections(double x, double y, KeyboardKey[] expected)
    {
        Assert.Equal(expected.Order(), KeyboardMoveOutput.Quantize(x, y).Order());
    }

    public static TheoryData<double, double, KeyboardKey[]> Directions => new()
    {
        { 0, -1, new[] { KeyboardKey.W } },
        { 0, 1, new[] { KeyboardKey.S } },
        { -1, 0, new[] { KeyboardKey.A } },
        { 1, 0, new[] { KeyboardKey.D } },
        { -1, -1, new[] { KeyboardKey.W, KeyboardKey.A } },
        { 1, -1, new[] { KeyboardKey.W, KeyboardKey.D } },
        { -1, 1, new[] { KeyboardKey.S, KeyboardKey.A } },
        { 1, 1, new[] { KeyboardKey.S, KeyboardKey.D } }
    };

    [Fact]
    public void Neutral_ReleasesAllMovementKeys()
    {
        var output = new FakeKeyboardOutput();
        var state = new KeyboardKeyState(output);
        var move = new KeyboardMoveOutput(state);
        move.SetLeftStick(255, 0);
        move.SetLeftStick(128, 128);
        Assert.Empty(state.CurrentPressedKeys);
        Assert.Contains((KeyboardKey.W, false), output.Events);
        Assert.Contains((KeyboardKey.D, false), output.Events);
    }

    [Fact]
    public void LockedMove_KeepsKeysUntilExplicitStop()
    {
        var fixture = MoveFixture();
        fixture.Controller.TryMoveDown(out _);
        fixture.Controller.UpdateCursor(new ScreenPoint(500, 480));
        fixture.Controller.ReleaseMove();
        Assert.Equal(new[] { KeyboardKey.W }, fixture.State.CurrentPressedKeys);
        fixture.Controller.StopMovement();
        Assert.Empty(fixture.State.CurrentPressedKeys);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(26)]
    public void MoveDistance_DoesNotChangeDigitalKeyboardDirection(int distance)
    {
        var fixture = MoveFixture();
        fixture.Controller.TryMoveDown(out _);

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + distance, 500));

        Assert.Equal(new[] { KeyboardKey.D }, fixture.State.CurrentPressedKeys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MoveAtOrWithinActivationRadius_ProducesNoWasd(int distance)
    {
        var fixture = MoveFixture();
        fixture.Controller.TryMoveDown(out _);

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + distance, 500));

        Assert.Empty(fixture.State.CurrentPressedKeys);
    }

    [Fact]
    public void MoveBeyondActivationRadius_ProducesDigitalWasd()
    {
        var fixture = MoveFixture();
        fixture.Controller.TryMoveDown(out _);

        fixture.Controller.UpdateCursor(new ScreenPoint(503, 500));

        Assert.Equal(new[] { KeyboardKey.D }, fixture.State.CurrentPressedKeys);
    }

    [Fact]
    public void Reaim_ChangesOnlyMovementKeyDifference()
    {
        var fixture = MoveFixture();
        fixture.Controller.TryMoveDown(out _);
        fixture.Controller.UpdateCursor(new ScreenPoint(500, 480));
        fixture.Controller.ReleaseMove();
        fixture.Cursor.Position = new ScreenPoint(600, 500);
        fixture.Controller.TryMoveDown(out _);
        fixture.Controller.UpdateCursor(new ScreenPoint(620, 480));
        fixture.Controller.UpdateCursor(new ScreenPoint(620, 500));
        Assert.Equal(new[] { KeyboardKey.D }, fixture.State.CurrentPressedKeys);
        Assert.Equal(1, fixture.Output.Events.Count(e => e == (KeyboardKey.W, true)));
        Assert.Equal(1, fixture.Output.Events.Count(e => e == (KeyboardKey.W, false)));
        Assert.Equal(1, fixture.Output.Events.Count(e => e == (KeyboardKey.D, true)));
    }

    [Fact]
    public void MoveStop_ReleasesWasd()
    {
        var fixture = MoveFixture();
        fixture.Controller.TryMoveDown(out _);
        fixture.Controller.UpdateCursor(new ScreenPoint(480, 480));
        fixture.Controller.StopMovement();
        Assert.Empty(fixture.State.CurrentPressedKeys);
    }

    [Fact]
    public void AllActions_DefaultToNone()
    {
        var bindings = new KeyboardBindings();
        Assert.Equal(KeyboardKey.None, bindings.Cross);
        Assert.Equal(KeyboardKey.None, bindings.Circle);
        Assert.All(KeyboardBindings.ProtocolActions, action =>
            Assert.Equal(KeyboardKey.None, bindings.Get(action)));
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
    public void NoneAction_DownAndUpProduceNoOutputOrOwnership(string action)
    {
        using var service = KeyboardService(out FakeKeyboardOutput output);
        service.ProcessProtocolAction(action, "down");
        service.ProcessProtocolAction(action, "up");
        Assert.Empty(output.Events);

        var state = new KeyboardKeyState(output);
        state.Press("action:" + action, KeyboardKey.None);
        Assert.Empty(state.CurrentPressedKeys);
        state.Release("action:" + action);
        Assert.Empty(output.Events);
    }

    [Fact]
    public void Action_UsesConfiguredBinding()
    {
        using var service = KeyboardService(out FakeKeyboardOutput output);
        var bindings = service.KeyboardBindings;
        bindings.Cross = KeyboardKey.Enter;
        Assert.True(service.TryUpdateKeyboardBindings(bindings));
        service.ProcessProtocolAction("cross", "down");
        service.ProcessProtocolAction("cross", "up");
        Assert.Equal(new[] { (KeyboardKey.Enter, true), (KeyboardKey.Enter, false) }, output.Events);
    }

    [Fact]
    public void PressedAction_ReleasesActualKeyEvenIfBindingObjectChangesLater()
    {
        var output = new FakeKeyboardOutput();
        var state = new KeyboardKeyState(output);
        state.Press("action:cross", KeyboardKey.Space);
        state.Release("action:cross");
        Assert.Equal(new[] { (KeyboardKey.Space, true), (KeyboardKey.Space, false) }, output.Events);
    }

    [Fact]
    public void DuplicateBinding_DoesNotReleaseUntilLastOwner()
    {
        var output = new FakeKeyboardOutput();
        var state = new KeyboardKeyState(output);
        state.Press("circle", KeyboardKey.E);
        state.Press("square", KeyboardKey.E);
        state.Release("circle");
        Assert.Equal(new[] { (KeyboardKey.E, true) }, output.Events);
        state.Release("square");
        Assert.Equal((KeyboardKey.E, false), output.Events.Last());
    }

    [Fact]
    public void MoveAndActionSharedKey_DoesNotReleaseEarly()
    {
        var output = new FakeKeyboardOutput();
        var state = new KeyboardKeyState(output);
        state.SetMovementKeys(new HashSet<KeyboardKey> { KeyboardKey.W });
        state.Press("action:cross", KeyboardKey.W);
        state.ReleaseMovement();
        Assert.Equal(new[] { (KeyboardKey.W, true) }, output.Events);
        state.Release("action:cross");
        Assert.Equal((KeyboardKey.W, false), output.Events.Last());
    }

    [Fact]
    public void RepeatedKeyDown_DoesNotSendDuplicateOutput()
    {
        var output = new FakeKeyboardOutput();
        var state = new KeyboardKeyState(output);
        state.Press("cross", KeyboardKey.Space);
        state.Press("cross", KeyboardKey.Space);
        Assert.Single(output.Events);
    }

    [Fact]
    public void KeyUpWithoutOwner_IsIgnored()
    {
        var output = new FakeKeyboardOutput();
        var state = new KeyboardKeyState(output);
        state.Release("missing");
        Assert.Empty(output.Events);
    }

    [Fact]
    public void ReleaseAll_ReleasesEveryKey()
    {
        var output = new FakeKeyboardOutput();
        var state = new KeyboardKeyState(output);
        state.Press("one", KeyboardKey.W);
        state.Press("two", KeyboardKey.Space);
        state.ReleaseAll();
        Assert.Empty(state.CurrentPressedKeys);
        Assert.Contains((KeyboardKey.W, false), output.Events);
        Assert.Contains((KeyboardKey.Space, false), output.Events);
    }

    [Theory]
    [InlineData(Ds4ControlResetReason.Disconnect)]
    [InlineData(Ds4ControlResetReason.SessionReplacement)]
    [InlineData(Ds4ControlResetReason.ServiceStop)]
    [InlineData(Ds4ControlResetReason.OutputFailure)]
    public void ResetReason_ReleasesKeyboardActions(Ds4ControlResetReason reason)
    {
        using var service = KeyboardService(out FakeKeyboardOutput output);
        ConfigureCross(service, KeyboardKey.Space);
        service.ProcessProtocolAction("cross", "down");
        service.ReleaseAllControls(reason);
        Assert.Equal((KeyboardKey.Space, false), output.Events.Last());
    }

    [Fact]
    public void Dispose_ReleasesKeys()
    {
        var service = KeyboardService(out FakeKeyboardOutput output);
        ConfigureCross(service, KeyboardKey.Space);
        service.ProcessProtocolAction("cross", "down");
        service.Dispose();
        Assert.Equal((KeyboardKey.Space, false), output.Events.Last());
    }

    [Fact]
    public void OutputFailure_AttemptsReleaseAndClearsState()
    {
        var output = new FakeKeyboardOutput { ThrowOnNextDown = true };
        var state = new KeyboardKeyState(output);
        Assert.Throws<KeyboardOutputException>(() => state.Press("move:w", KeyboardKey.W));
        Assert.Empty(state.CurrentPressedKeys);
        Assert.Contains((KeyboardKey.W, false), output.Events);
    }

    [Fact]
    public void MappingStore_SavesAndLoads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"leftpad-bindings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonKeyboardBindingStore(path);
            var bindings = new KeyboardBindings { Cross = KeyboardKey.None, R2 = KeyboardKey.Z };
            store.Save(bindings);
            KeyboardBindings loaded = store.Load();
            Assert.Equal(KeyboardKey.None, loaded.Cross);
            Assert.Equal(KeyboardKey.Z, loaded.R2);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void MappingStore_InvalidValuesFallBackIndividually()
    {
        string path = Path.Combine(Path.GetTempPath(), $"leftpad-bindings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"cross\":\"NotAKey\",\"circle\":\"Enter\",\"r2\":\"9999\"}");
            KeyboardBindings loaded = new JsonKeyboardBindingStore(path).Load();
            Assert.Equal(KeyboardKey.None, loaded.Cross);
            Assert.Equal(KeyboardKey.Enter, loaded.Circle);
            Assert.Equal(KeyboardKey.None, loaded.R2);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DirectDs4Trigger_RemainsAnalog255AndZero()
    {
        using var service = Service(out FakeDirectDs4Factory factory, out _);
        service.Initialize();
        service.ProcessProtocolAction("l2", "down");
        service.ProcessProtocolAction("l2", "up");
        Assert.Equal(new[] { (DualShock4Slider.LeftTrigger, byte.MaxValue), (DualShock4Slider.LeftTrigger, byte.MinValue) },
            factory.Session.TriggerEvents);
    }

    private static Ds4Service KeyboardService(out FakeKeyboardOutput output)
    {
        var service = Service(out _, out output);
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        return service;
    }

    private static void ConfigureCross(Ds4Service service, KeyboardKey key)
    {
        KeyboardBindings bindings = service.KeyboardBindings;
        bindings.Cross = key;
        Assert.True(service.TryUpdateKeyboardBindings(bindings));
    }

    private static Ds4Service Service(out FakeDirectDs4Factory factory, out FakeKeyboardOutput output)
    {
        factory = new FakeDirectDs4Factory();
        output = new FakeKeyboardOutput();
        return new Ds4Service(factory, output, new MemoryBindingStore());
    }

    private static MoveTestFixture MoveFixture()
    {
        var output = new FakeKeyboardOutput();
        var state = new KeyboardKeyState(output);
        var cursor = new FakeCursor { Position = new ScreenPoint(500, 500) };
        var controller = new VirtualJoystickController(new KeyboardMoveOutput(state), new FakeOverlay(), cursor);
        return new MoveTestFixture(output, state, cursor, controller);
    }

    private sealed record MoveTestFixture(FakeKeyboardOutput Output, KeyboardKeyState State, FakeCursor Cursor, VirtualJoystickController Controller);

    private sealed class FakeKeyboardOutput : IKeyboardOutput
    {
        public List<(KeyboardKey Key, bool Pressed)> Events { get; } = new();
        public bool ThrowOnNextDown { get; set; }
        public void SetKeyState(KeyboardKey key, bool isPressed)
        {
            if (isPressed && ThrowOnNextDown)
            {
                ThrowOnNextDown = false;
                throw new InvalidOperationException("injected failure");
            }
            Events.Add((key, isPressed));
        }
    }

    private sealed class FakeDirectDs4Factory : IDirectDs4Factory
    {
        public int CreateCalls { get; private set; }
        public FakeDirectDs4Session Session { get; } = new();
        public IDirectDs4Session Create() { CreateCalls++; return Session; }
    }

    private sealed class FakeDirectDs4Session : IDirectDs4Session
    {
        public List<(DualShock4Slider, byte)> TriggerEvents { get; } = new();
        public void SetButton(DualShock4Button button, bool pressed) { }
        public void SetTrigger(DualShock4Slider trigger, byte value) => TriggerEvents.Add((trigger, value));
        public void SetLeftStick(byte x, byte y) { }
        public void SubmitReport() { }
        public void Dispose() { }
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
        public bool TryGetPosition(out ScreenPoint position, out int win32Error)
        { position = Position; win32Error = 0; return true; }
    }

    private sealed class FakeOverlay : IJoystickOverlay
    {
        public bool IsVisible { get; private set; }
        public void Show(ScreenPoint center) => IsVisible = true;
        public void UpdateKnob(double x, double y) { }
        public void Hide() => IsVisible = false;
    }
}
