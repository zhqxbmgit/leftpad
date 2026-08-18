using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialDs4ExecutionTests
{
    [Fact]
    public void Catalog_ContainsExactlyTheEightFinalActions()
    {
        Assert.Equal(
            new[] { "cross", "circle", "square", "triangle", "l1", "l3", "r3", "dpad_down" },
            RadialDs4ActionCatalog.Actions.Select(action => action.Id));
        Assert.Equal(
            new[] { "CROSS", "CIRCLE", "SQUARE", "TRIANGLE", "L1", "L3", "R3", "十字键下" },
            RadialDs4ActionCatalog.Actions.Select(action => action.DisplayName));
    }

    [Theory]
    [InlineData("cross")]
    [InlineData("circle")]
    [InlineData("square")]
    [InlineData("triangle")]
    [InlineData("l1")]
    [InlineData("l3")]
    [InlineData("r3")]
    [InlineData("dpad_down")]
    public void Catalog_AcceptsEveryFinalAction(string id)
    {
        Assert.True(RadialDs4ActionCatalog.TryGet(id, out RadialDs4ActionMapping action));
        Assert.Equal(id, action.Id);
    }

    [Theory]
    [InlineData("r1")]
    [InlineData("l2")]
    [InlineData("r2")]
    [InlineData("anything_else")]
    public void Catalog_RejectsActionsOutsideTheFinalSet(string id)
    {
        Assert.False(RadialDs4ActionCatalog.TryGet(id, out _));
    }

    [Theory]
    [InlineData("r1")]
    [InlineData("l2")]
    [InlineData("r2")]
    public void OrdinaryDs4Mapper_StillSupportsActionsExcludedFromRadial(string id)
    {
        Assert.True(Ds4ActionMapper.TryGet(id, out _));
    }

    [Theory]
    [InlineData("cross")]
    [InlineData("l1")]
    [InlineData("l3")]
    [InlineData("r3")]
    public void DigitalAction_PulsesWithoutSleepingAndFinishesReleased(string id)
    {
        using TestFixture fixture = CreateInitializedFixture();
        RadialDs4ActionCatalog.TryGet(id, out RadialDs4ActionMapping action);

        Assert.True(fixture.Service.TryExecuteRadialDs4Action(Mapping(id), out string error), error);

        Assert.Equal(new OutputEvent[]
        {
            new ButtonOutput(action.DigitalButton!, true),
            new SubmitOutput(),
            new DelayOutput(Ds4Service.RadialDs4TapDurationMs),
            new ButtonOutput(action.DigitalButton!, false),
            new SubmitOutput()
        }, fixture.Session.Events);

        fixture.Session.Events.Clear();
        fixture.Service.ReleaseAllControls(Ds4ControlResetReason.Disconnect);
        Assert.Empty(fixture.Session.Events);
    }

    [Fact]
    public void DPadDown_PulsesSouthThenNeutralWithoutSleeping()
    {
        using TestFixture fixture = CreateInitializedFixture();

        Assert.True(fixture.Service.TryExecuteRadialDs4Action(Mapping("dpad_down"), out string error), error);

        Assert.Equal(new OutputEvent[]
        {
            new DPadOutput(DualShock4DPadDirection.South),
            new SubmitOutput(),
            new DelayOutput(Ds4Service.RadialDs4TapDurationMs),
            new DPadOutput(DualShock4DPadDirection.None),
            new SubmitOutput()
        }, fixture.Session.Events);

        fixture.Session.Events.Clear();
        fixture.Service.ReleaseAllControls(Ds4ControlResetReason.Disconnect);
        Assert.Empty(fixture.Session.Events);
    }

    [Theory]
    [InlineData("cross")]
    [InlineData("l1")]
    public void HeldTarget_IsRejectedWithoutChangingTheOrdinaryHold(string id)
    {
        using TestFixture fixture = CreateInitializedFixture();
        RadialDs4ActionCatalog.TryGet(id, out RadialDs4ActionMapping action);
        fixture.Service.ProcessProtocolAction(id, "down");
        fixture.Session.Events.Clear();

        Assert.False(fixture.Service.TryExecuteRadialDs4Action(Mapping(id), out string error));
        Assert.Contains("当前已按下", error);
        Assert.Empty(fixture.Session.Events);

        fixture.Service.ProcessProtocolAction(id, "up");
        Assert.Equal(new OutputEvent[]
        {
            new ButtonOutput(action.DigitalButton!, false),
            new SubmitOutput()
        }, fixture.Session.Events);
    }

    [Fact]
    public void OtherOrdinaryHold_RemainsPressedAcrossRadialPulse()
    {
        using TestFixture fixture = CreateInitializedFixture();
        fixture.Service.ProcessProtocolAction("square", "down");
        fixture.Session.Events.Clear();

        Assert.True(fixture.Service.TryExecuteRadialDs4Action(Mapping("cross"), out string error), error);

        Assert.DoesNotContain(
            fixture.Session.Events,
            item => item is ButtonOutput { Button: var button, Pressed: false } &&
                Equals(button, DualShock4Button.Square));

        fixture.Service.ProcessProtocolAction("square", "up");
        Assert.Contains(new ButtonOutput(DualShock4Button.Square, false), fixture.Session.Events);
    }

    [Theory]
    [InlineData("cross")]
    [InlineData("dpad_down")]
    public void KeyboardMode_RejectsRadialDs4WithoutCreatingViGEm(string id)
    {
        var factory = new FakeDirectDs4Factory();
        using var service = new Ds4Service(
            factory,
            new FakeKeyboardOutput(),
            new MemoryBindingStore(),
            radialDs4Delay: _ => throw new InvalidOperationException("delay must not run"));
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        Assert.True(service.Initialize());

        Assert.False(service.TryExecuteRadialDs4Action(Mapping(id), out string error));
        Assert.Equal("当前输出模式不是 Direct DS4。", error);
        Assert.Equal(0, factory.CreateCalls);
        Assert.Empty(factory.Session.Events);
    }

    [Fact]
    public void DirectModeWithoutSession_ReturnsUnavailableWithoutOutput()
    {
        var factory = new FakeDirectDs4Factory();
        using var service = new Ds4Service(factory, new FakeKeyboardOutput(), new MemoryBindingStore());

        Assert.False(service.TryExecuteRadialDs4Action(Mapping("cross"), out string error));
        Assert.Equal("虚拟 DS4 当前不可用。", error);
        Assert.Equal(0, factory.CreateCalls);
        Assert.Empty(factory.Session.Events);
    }

    [Theory]
    [InlineData("cross")]
    [InlineData("dpad_down")]
    public void FirstSubmitFailure_ReturnsFalseAndCleansTarget(string id)
    {
        using TestFixture fixture = CreateInitializedFixture();
        fixture.Session.ThrowOnSubmitCall = 1;

        Assert.False(fixture.Service.TryExecuteRadialDs4Action(Mapping(id), out string error));

        Assert.Contains("DS4 输出失败", error);
        AssertTargetWasCleaned(id, fixture.Session.Events);
        Assert.Equal(2, fixture.Session.SubmitCalls);
    }

    [Theory]
    [InlineData("cross")]
    [InlineData("dpad_down")]
    public void ReleaseSubmitFailure_ReturnsFalseAndRetriesNeutralCleanup(string id)
    {
        using TestFixture fixture = CreateInitializedFixture();
        fixture.Session.ThrowOnSubmitCall = 2;

        Assert.False(fixture.Service.TryExecuteRadialDs4Action(Mapping(id), out string error));

        Assert.Contains("DS4 输出失败", error);
        AssertTargetWasCleaned(id, fixture.Session.Events);
        Assert.Equal(3, fixture.Session.SubmitCalls);
        Assert.Contains(new DelayOutput(Ds4Service.RadialDs4TapDurationMs), fixture.Session.Events);
    }

    private static void AssertTargetWasCleaned(string id, IReadOnlyList<OutputEvent> events)
    {
        OutputEvent lastTargetOutput = events.Last(item => item is ButtonOutput or DPadOutput);
        if (id == "dpad_down")
            Assert.Equal(new DPadOutput(DualShock4DPadDirection.None), lastTargetOutput);
        else
            Assert.Equal(new ButtonOutput(DualShock4Button.Cross, false), lastTargetOutput);
    }

    private static RadialSlotMapping Mapping(string id) => new()
    {
        Kind = RadialActionKind.Ds4Button,
        Ds4Button = id
    };

    private static TestFixture CreateInitializedFixture()
    {
        var factory = new FakeDirectDs4Factory();
        var service = new Ds4Service(
            factory,
            new FakeKeyboardOutput(),
            new MemoryBindingStore(),
            radialDs4Delay: milliseconds => factory.Session.Events.Add(new DelayOutput(milliseconds)));
        Assert.True(service.Initialize());
        return new TestFixture(service, factory.Session);
    }

    private sealed record TestFixture(Ds4Service Service, FakeDirectDs4Session Session) : IDisposable
    {
        public void Dispose() => Service.Dispose();
    }

    private abstract record OutputEvent;
    private sealed record ButtonOutput(DualShock4Button Button, bool Pressed) : OutputEvent;
    private sealed record DPadOutput(DualShock4DPadDirection Direction) : OutputEvent;
    private sealed record SubmitOutput : OutputEvent;
    private sealed record DelayOutput(int Milliseconds) : OutputEvent;

    private sealed class FakeDirectDs4Factory : IDirectDs4Factory
    {
        public int CreateCalls { get; private set; }
        public FakeDirectDs4Session Session { get; } = new();

        public IDirectDs4Session Create()
        {
            CreateCalls++;
            return Session;
        }
    }

    private sealed class FakeDirectDs4Session : IDirectDs4Session
    {
        public List<OutputEvent> Events { get; } = new();
        public int SubmitCalls { get; private set; }
        public int? ThrowOnSubmitCall { get; set; }

        public void SetButton(DualShock4Button button, bool pressed) =>
            Events.Add(new ButtonOutput(button, pressed));

        public void SetDPadDirection(DualShock4DPadDirection direction) =>
            Events.Add(new DPadOutput(direction));

        public void SetTrigger(DualShock4Slider trigger, byte value) { }
        public void SetLeftStick(byte x, byte y) { }

        public void SubmitReport()
        {
            Events.Add(new SubmitOutput());
            SubmitCalls++;
            if (SubmitCalls == ThrowOnSubmitCall)
                throw new InvalidOperationException($"submit failure {SubmitCalls}");
        }

        public void Dispose() { }
    }

    private sealed class FakeKeyboardOutput : IKeyboardOutput
    {
        public void SetKeyState(KeyboardKey key, bool isPressed) { }
    }

    private sealed class MemoryBindingStore : IKeyboardBindingStore
    {
        public KeyboardBindings Load() => new();
        public void Save(KeyboardBindings bindings) { }
    }
}
