using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialKeyboardActionExecutorTests
{
    [Fact]
    public void KeyboardKey_F1_SendsDownThenUp()
    {
        var fixture = new ExecutorFixture();

        bool executed = fixture.Executor.TryExecute(Key(KeyboardKey.F1), out string error);

        Assert.True(executed, error);
        Assert.Equal(new[] { "F1 down", "F1 up" }, fixture.Output.Events);
        Assert.Empty(fixture.State.CurrentPressedKeys);
    }

    [Fact]
    public void CtrlShiftK_UsesDeterministicPressAndReverseReleaseOrder()
    {
        var fixture = new ExecutorFixture();
        var mapping = Shortcut(KeyboardKey.K, ctrl: true, shift: true);

        bool executed = fixture.Executor.TryExecute(mapping, out string error);

        Assert.True(executed, error);
        Assert.Equal(new[]
        {
            "LeftControl down",
            "LeftShift down",
            "K down",
            "K up",
            "LeftShift up",
            "LeftControl up"
        }, fixture.Output.Events);
    }

    [Fact]
    public void AltF4_UsesDeterministicPressAndReverseReleaseOrder()
    {
        var fixture = new ExecutorFixture();

        bool executed = fixture.Executor.TryExecute(
            Shortcut(KeyboardKey.F4, alt: true),
            out string error);

        Assert.True(executed, error);
        Assert.Equal(new[]
        {
            "LeftAlt down",
            "F4 down",
            "F4 up",
            "LeftAlt up"
        }, fixture.Output.Events);
    }

    [Fact]
    public void AllModifiers_UseCtrlAltShiftWinOrderAndReverseReleaseOrder()
    {
        var fixture = new ExecutorFixture();
        var mapping = Shortcut(
            KeyboardKey.F1,
            ctrl: true,
            alt: true,
            shift: true,
            win: true);

        bool executed = fixture.Executor.TryExecute(mapping, out string error);

        Assert.True(executed, error);
        Assert.Equal(new[]
        {
            "LeftControl down",
            "LeftAlt down",
            "LeftShift down",
            "LeftWin down",
            "F1 down",
            "F1 up",
            "LeftWin up",
            "LeftShift up",
            "LeftAlt up",
            "LeftControl up"
        }, fixture.Output.Events);
    }

    [Fact]
    public void None_DoesNotExecuteKeyboardOutput()
    {
        var fixture = new ExecutorFixture();

        Assert.False(fixture.Executor.TryExecute(RadialSlotMapping.None, out _));
        Assert.Empty(fixture.Output.Events);
    }

    [Fact]
    public void Ds4Button_DoesNotExecuteKeyboardOutput()
    {
        var fixture = new ExecutorFixture();
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.Ds4Button,
            Ds4Button = "cross"
        };

        Assert.False(fixture.Executor.TryExecute(mapping, out _));
        Assert.Empty(fixture.Output.Events);
    }

    [Fact]
    public void ExistingModifierOwner_RemainsPressedUntilExistingSourceReleases()
    {
        var fixture = new ExecutorFixture();
        fixture.State.Press("existing", KeyboardKey.LeftControl);

        bool executed = fixture.Executor.TryExecute(
            Shortcut(KeyboardKey.K, ctrl: true),
            out string error);

        Assert.True(executed, error);
        Assert.Contains(KeyboardKey.LeftControl, fixture.State.CurrentPressedKeys);
        Assert.Equal(new[]
        {
            "LeftControl down",
            "K down",
            "K up"
        }, fixture.Output.Events);

        fixture.State.Release("existing");

        Assert.Equal("LeftControl up", fixture.Output.Events[^1]);
        Assert.Empty(fixture.State.CurrentPressedKeys);
    }

    [Fact]
    public void ExistingMainKeyOwner_IsNotInterruptedByRadialTap()
    {
        var fixture = new ExecutorFixture();
        fixture.State.Press("existing", KeyboardKey.A);

        bool executed = fixture.Executor.TryExecute(Key(KeyboardKey.A), out string error);

        Assert.True(executed, error);
        Assert.Contains(KeyboardKey.A, fixture.State.CurrentPressedKeys);
        Assert.Equal(new[] { "A down" }, fixture.Output.Events);

        fixture.State.Release("existing");

        Assert.Equal(new[] { "A down", "A up" }, fixture.Output.Events);
        Assert.Empty(fixture.State.CurrentPressedKeys);
    }

    [Fact]
    public void MainKeyDownFailure_ReturnsFailureAndLeavesNoPressedKeys()
    {
        var fixture = new ExecutorFixture();
        fixture.Output.ThrowOnCall = 3;

        bool executed = fixture.Executor.TryExecute(
            Shortcut(KeyboardKey.K, ctrl: true, shift: true),
            out string error);

        Assert.False(executed);
        Assert.Contains("call 3", error);
        Assert.Empty(fixture.State.CurrentPressedKeys);
    }

    [Fact]
    public void KeyUpFailure_ReturnsFailureAndLeavesNoPressedKeys()
    {
        var fixture = new ExecutorFixture();
        fixture.Output.ThrowOnCall = 2;

        bool executed = fixture.Executor.TryExecute(Key(KeyboardKey.F1), out string error);

        Assert.False(executed);
        Assert.Contains("call 2", error);
        Assert.Empty(fixture.State.CurrentPressedKeys);
    }

    [Fact]
    public void Ds4Service_DefaultDirectDs4Mode_StillExecutesRadialKeyboardAction()
    {
        var output = new RecordingKeyboardOutput();
        using var service = new Ds4Service(
            keyboardOutput: output,
            bindingStore: new MemoryBindingStore());

        Assert.Equal(OutputMode.DirectDs4, service.OutputMode);
        Assert.True(service.TryExecuteRadialKeyboardAction(Key(KeyboardKey.Escape), out string error), error);
        Assert.Equal(new[] { "Escape down", "Escape up" }, output.Events);
    }

    [Fact]
    public void LeftWin_IsModifierOnlyAndCannotBeSelectedAsMainKey()
    {
        Assert.DoesNotContain(KeyboardKey.LeftWin, KeyboardKeyCatalog.MainKeys);
        Assert.False(KeyboardKeyCatalog.IsMainKey(KeyboardKey.LeftWin));
    }

    private static RadialSlotMapping Key(KeyboardKey key) => new()
    {
        Kind = RadialActionKind.KeyboardKey,
        Key = key
    };

    private static RadialSlotMapping Shortcut(
        KeyboardKey key,
        bool ctrl = false,
        bool alt = false,
        bool shift = false,
        bool win = false) => new()
    {
        Kind = RadialActionKind.KeyboardShortcut,
        Key = key,
        Ctrl = ctrl,
        Alt = alt,
        Shift = shift,
        Win = win
    };

    private sealed class ExecutorFixture
    {
        public RecordingKeyboardOutput Output { get; } = new();
        public KeyboardKeyState State { get; }
        public RadialKeyboardActionExecutor Executor { get; }

        public ExecutorFixture()
        {
            State = new KeyboardKeyState(Output);
            Executor = new RadialKeyboardActionExecutor(State);
        }
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private int _callCount;

        public List<string> Events { get; } = new();
        public int? ThrowOnCall { get; set; }

        public void SetKeyState(KeyboardKey key, bool isPressed)
        {
            int call = ++_callCount;
            Events.Add($"{key} {(isPressed ? "down" : "up")}");
            if (call == ThrowOnCall) throw new InvalidOperationException($"failure on call {call}");
        }
    }

    private sealed class MemoryBindingStore : IKeyboardBindingStore
    {
        public KeyboardBindings Load() => new();
        public void Save(KeyboardBindings bindings) { }
    }
}
