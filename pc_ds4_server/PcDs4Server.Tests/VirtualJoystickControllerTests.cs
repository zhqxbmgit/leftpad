using PcDs4Server;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class VirtualJoystickControllerTests
{
    [Fact]
    public void ControlConstants_MatchVampireSurvivorsCursorModel()
    {
        Assert.Equal(20.0, VirtualJoystickController.JoystickRadius);
        Assert.Equal(4.0, VirtualJoystickController.ActivationRadius);
        Assert.Equal(10, CursorJoystickSampler.IntervalMilliseconds);
        Assert.Equal(20, VirtualJoystickOverlay.VisualRadius);
        Assert.Equal(40, VirtualJoystickOverlay.BaseDiameter);
        Assert.Equal(11, VirtualJoystickOverlay.KnobDiameter);
        Assert.Equal(1.0, VirtualJoystickOverlay.VisualScale);
    }

    [Fact]
    public void OverlayForm_RecalculatesCanvasForHalfSizeVisuals()
    {
        using var overlay = new VirtualJoystickOverlay();

        Assert.Equal(54, overlay.ClientSize.Width);
        Assert.Equal(54, overlay.ClientSize.Height);
    }

    [Theory]
    [InlineData(20.0, 20.0)]
    [InlineData(10.0, 10.0)]
    [InlineData(-20.0, -20.0)]
    public void OverlayVisualScale_MapsLogicalOffsetWithoutChangingControllerMath(
        double logicalOffset,
        double expectedVisualOffset)
    {
        Assert.Equal(
            expectedVisualOffset,
            VirtualJoystickOverlay.MapLogicalOffsetToVisual(logicalOffset),
            6);
    }

    [Fact]
    public void OverlayVisualScale_MapsFullDiagonalToCircleEdge()
    {
        double logical = 20.0 / Math.Sqrt(2.0);

        Assert.Equal(14.142136, VirtualJoystickOverlay.MapLogicalOffsetToVisual(logical), 6);
        Assert.Equal(-14.142136, VirtualJoystickOverlay.MapLogicalOffsetToVisual(-logical), 6);
    }

    [Fact]
    public void Center_IsNeutral()
    {
        var fixture = ActiveFixture();
        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;

        Assert.Equal(new ScreenPoint(500, 500), state.Center);
        Assert.Equal(new ScreenPoint(500, 500), state.CurrentCursor);
        Assert.Equal(0.0, state.CursorDistance);
        Assert.Equal(0.0, state.LogicalKnobX);
        Assert.Equal(0.0, state.LogicalKnobY);
        Assert.Equal(0.0, state.StickX);
        Assert.Equal(0.0, state.StickY);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
    }

    [Fact]
    public void DeltaTen_ProducesFullDirection()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(510, 500));

        AssertState(fixture.Controller.Snapshot, 10, 0, 20, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    public void DeltaBeyondActivationRadius_ProducesFullDirection(int deltaX)
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500));

        AssertState(
            fixture.Controller.Snapshot,
            deltaX,
            0,
            20,
            0,
            1,
            0);
    }

    [Fact]
    public void DeltaTwenty_IsExactlyFullStrength()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(520, 500));

        AssertState(fixture.Controller.Snapshot, 20, 0, 20, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
        Assert.Equal((255, 128), fixture.Stick.LastValue);
    }

    [Fact]
    public void DeltaOneHundred_ClampsKnobToTwentyAndStaysFullStrength()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(600, 500));

        AssertState(fixture.Controller.Snapshot, 100, 0, 20, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(70)]
    public void DeltaOutsideRadius_ClampsKnobToTwentyAndStaysFullStrength(int deltaX)
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500));

        AssertState(fixture.Controller.Snapshot, deltaX, 0, 20, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
    }

    [Fact]
    public void DeltaOneHundredFifty_ClampsKnobToTwentyAndStaysFullStrength()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));

        AssertState(fixture.Controller.Snapshot, 150, 0, 20, 0, 1, 0);
    }

    [Fact]
    public void ReturningTowardCenter_RemainsFullUntilActivationBoundary()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));

        foreach (int currentX in new[] { 520, 510, 505 })
        {
            fixture.Controller.UpdateCursor(new ScreenPoint(currentX, 500));

            AssertState(
                fixture.Controller.Snapshot,
                currentX - 500,
                0,
                20,
                0,
                1,
                0);
        }

        fixture.Controller.UpdateCursor(new ScreenPoint(504, 500));
        AssertState(fixture.Controller.Snapshot, 4, 0, 0, 0, 0, 0);
        Assert.False(fixture.Controller.Snapshot.DirectionActive);
    }

    [Fact]
    public void ReturningFromOneHundredFiftyToOneHundred_RemainsFullStrength()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));

        fixture.Controller.UpdateCursor(new ScreenPoint(600, 500));

        AssertState(fixture.Controller.Snapshot, 100, 0, 20, 0, 1, 0);
    }

    [Fact]
    public void DeltaNineteen_RemainsFullDirection()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));

        fixture.Controller.UpdateCursor(new ScreenPoint(519, 500));

        AssertState(fixture.Controller.Snapshot, 19, 0, 20, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
    }

    [Fact]
    public void OutsideRadius_DiagonalDirectionMovesKnobAlongCircle()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(600, 500));

        fixture.Controller.UpdateCursor(new ScreenPoint(600, 400));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.Equal(100.0, state.CursorDeltaX);
        Assert.Equal(-100.0, state.CursorDeltaY);
        Assert.Equal(14.142136, state.LogicalKnobX, 6);
        Assert.Equal(-14.142136, state.LogicalKnobY, 6);
        Assert.Equal(0.707107, state.StickX, 6);
        Assert.Equal(-0.707107, state.StickY, 6);
        Assert.Equal(1.0, Math.Sqrt((state.StickX * state.StickX) + (state.StickY * state.StickY)), 6);
        Assert.Equal(20.0, Math.Sqrt((state.LogicalKnobX * state.LogicalKnobX) + (state.LogicalKnobY * state.LogicalKnobY)), 6);
    }

    [Fact]
    public void ActivatedDirection_PreservesContinuousNonQuantizedAngle()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(510, 495));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.Equal(0.894427, state.StickX, 6);
        Assert.Equal(-0.447214, state.StickY, 6);
        Assert.Equal(17.888544, state.LogicalKnobX, 6);
        Assert.Equal(-8.944272, state.LogicalKnobY, 6);
        Assert.Equal(1.0, Math.Sqrt((state.StickX * state.StickX) + (state.StickY * state.StickY)), 6);
        Assert.Equal(20.0, Math.Sqrt((state.LogicalKnobX * state.LogicalKnobX) + (state.LogicalKnobY * state.LogicalKnobY)), 6);
    }

    [Fact]
    public void DirectionChangesImmediatelyAcrossContinuousCircle()
    {
        var fixture = ActiveFixture();
        var cursorPositions = new[]
        {
            new ScreenPoint(600, 500),
            new ScreenPoint(600, 400),
            new ScreenPoint(500, 400)
        };

        foreach (ScreenPoint cursor in cursorPositions)
        {
            fixture.Controller.UpdateCursor(cursor);
            VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
            Assert.Equal(1.0, Math.Sqrt((state.StickX * state.StickX) + (state.StickY * state.StickY)), 6);
            Assert.Equal(20.0, Math.Sqrt((state.LogicalKnobX * state.LogicalKnobX) + (state.LogicalKnobY * state.LogicalKnobY)), 6);
        }

        Assert.Equal(0.0, fixture.Controller.Snapshot.StickX, 6);
        Assert.Equal(-1.0, fixture.Controller.Snapshot.StickY, 6);
    }

    [Fact]
    public void NegativeYTwenty_MapsToFullUp()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500, 480));

        Assert.Equal((128, 0), fixture.Stick.LastValue);
        Assert.Equal(-1.0, fixture.Controller.Snapshot.StickY);
    }

    [Fact]
    public void PositiveYTwenty_MapsToFullDownWithoutExtraInversion()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500, 520));

        Assert.Equal((128, 255), fixture.Stick.LastValue);
        Assert.Equal(1.0, fixture.Controller.Snapshot.StickY);
    }

    [Fact]
    public void NegativeXTwenty_MapsToFullLeft()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(480, 500));

        Assert.Equal((0, 128), fixture.Stick.LastValue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void DisplacementAtOrWithinActivationRadius_RemainsNeutral(int deltaX)
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500));

        Assert.Equal(0.0, fixture.Controller.Snapshot.StickX);
        Assert.Equal(0.0, fixture.Controller.Snapshot.LogicalKnobX);
        Assert.Equal(VirtualJoystickController.NeutralAxis, fixture.Controller.Snapshot.Ds4X);
        Assert.False(fixture.Controller.Snapshot.DirectionActive);
    }

    [Fact]
    public void MoveDown_ReadsCursorOnceAndUsesItAsFixedCenter()
    {
        var fixture = new Fixture(new ScreenPoint(-300, -120));

        Assert.True(fixture.Controller.TryMoveDown(out int error));

        Assert.Equal(0, error);
        Assert.Equal(1, fixture.Cursor.ReadCount);
        Assert.Equal(new ScreenPoint(-300, -120), fixture.Controller.Snapshot.Center);
        Assert.Equal(new ScreenPoint(-300, -120), fixture.Overlay.Center);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.True(fixture.Overlay.IsVisible);
    }

    [Fact]
    public void CursorSampling_ReadsChangedFakeCursorAndUpdatesDirection()
    {
        var fixture = ActiveFixture();
        fixture.Cursor.Position = new ScreenPoint(500, 430);

        fixture.Controller.SampleCursor();

        Assert.Equal(2, fixture.Cursor.ReadCount);
        Assert.Equal(new ScreenPoint(500, 430), fixture.Controller.Snapshot.CurrentCursor);
        Assert.Equal(-1.0, fixture.Controller.Snapshot.StickY);
    }

    [Fact]
    public void RepeatedMoveDown_DoesNotReadCursorOrMoveCenterAgain()
    {
        var fixture = ActiveFixture();
        fixture.Cursor.Position = new ScreenPoint(900, 900);

        Assert.True(fixture.Controller.TryMoveDown(out _));

        Assert.Equal(1, fixture.Cursor.ReadCount);
        Assert.Equal(new ScreenPoint(500, 500), fixture.Controller.Snapshot.Center);
        Assert.Equal(new ScreenPoint(500, 500), fixture.Overlay.Center);
    }

    [Fact]
    public void MoveUp_ClearsAllStateNeutralizesAndHidesImmediately()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));
        fixture.Actions.Clear();

        fixture.Controller.Reset(JoystickResetReason.MoveUp);

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.False(state.MoveButtonPressed);
        Assert.False(state.JoystickActive);
        Assert.Null(state.Center);
        Assert.Null(state.CurrentCursor);
        Assert.Equal(0.0, state.CursorDistance);
        Assert.Equal(0.0, state.LogicalKnobX);
        Assert.Equal(0.0, state.StickX);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.False(fixture.Overlay.IsVisible);
        Assert.Equal(new[] { "stick:128/128", "hide" }, fixture.Actions);
    }

    [Fact]
    public void SamplingAfterMoveUp_IsIgnoredWithoutReadingCursor()
    {
        var fixture = ActiveFixture();
        fixture.Controller.Reset(JoystickResetReason.MoveUp);
        int readsAtRelease = fixture.Cursor.ReadCount;
        fixture.Cursor.Position = new ScreenPoint(700, 500);

        fixture.Controller.SampleCursor();

        Assert.Equal(readsAtRelease, fixture.Cursor.ReadCount);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.False(fixture.Controller.Snapshot.JoystickActive);
    }

    [Fact]
    public void MoveDownCursorFailure_DoesNotActivateAndReportsWin32Error()
    {
        var fixture = new Fixture();
        fixture.Cursor.Success = false;
        fixture.Cursor.Win32Error = 5;

        bool activated = fixture.Controller.TryMoveDown(out int error);

        Assert.False(activated);
        Assert.Equal(5, error);
        Assert.False(fixture.Controller.Snapshot.MoveButtonPressed);
        Assert.False(fixture.Controller.Snapshot.JoystickActive);
        Assert.Null(fixture.Controller.Snapshot.Center);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.False(fixture.Overlay.IsVisible);
        Assert.Equal(JoystickResetReason.CursorPositionFailure, fixture.Controller.Snapshot.LastResetReason);
    }

    [Fact]
    public void HeldSamplingCursorFailure_NeutralizesHidesAndRaisesError()
    {
        var fixture = ActiveFixture();
        int? reportedError = null;
        fixture.Controller.CursorPositionReadFailed += error => reportedError = error;
        fixture.Cursor.Success = false;
        fixture.Cursor.Win32Error = 1400;

        fixture.Controller.SampleCursor();

        Assert.Equal(1400, reportedError);
        Assert.False(fixture.Controller.Snapshot.JoystickActive);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.False(fixture.Overlay.IsVisible);
    }

    [Theory]
    [InlineData(JoystickResetReason.Disconnect)]
    [InlineData(JoystickResetReason.SessionReplacement)]
    [InlineData(JoystickResetReason.ServiceStop)]
    [InlineData(JoystickResetReason.NormalExit)]
    [InlineData(JoystickResetReason.ControllerDispose)]
    public void SafetyReset_NeutralizesBeforeHiding(JoystickResetReason reason)
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(520, 500));
        fixture.Actions.Clear();

        fixture.Controller.Reset(reason);

        Assert.Equal(new[] { "stick:128/128", "hide" }, fixture.Actions);
        Assert.False(fixture.Controller.Snapshot.JoystickActive);
        Assert.Equal(reason, fixture.Controller.Snapshot.LastResetReason);
    }

    [Fact]
    public void CursorUpdateWhileReleased_IsIgnored()
    {
        var fixture = new Fixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(700, 700));

        Assert.Equal(0.0, fixture.Controller.Snapshot.CursorDistance);
        Assert.Empty(fixture.Actions);
    }

    private static void AssertState(
        VirtualJoystickSnapshot state,
        double expectedDeltaX,
        double expectedDeltaY,
        double expectedKnobX,
        double expectedKnobY,
        double expectedStickX,
        double expectedStickY)
    {
        Assert.Equal(expectedDeltaX, state.CursorDeltaX, 6);
        Assert.Equal(expectedDeltaY, state.CursorDeltaY, 6);
        Assert.Equal(expectedKnobX, state.LogicalKnobX, 6);
        Assert.Equal(expectedKnobY, state.LogicalKnobY, 6);
        Assert.Equal(expectedStickX, state.StickX, 6);
        Assert.Equal(expectedStickY, state.StickY, 6);
    }

    private static Fixture ActiveFixture()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Controller.TryMoveDown(out _));
        return fixture;
    }

    private sealed class Fixture
    {
        public List<string> Actions { get; } = new();
        public RecordingStick Stick { get; }
        public RecordingOverlay Overlay { get; }
        public RecordingCursor Cursor { get; }
        public VirtualJoystickController Controller { get; }

        public Fixture(ScreenPoint? initialCursor = null)
        {
            Stick = new RecordingStick(Actions);
            Overlay = new RecordingOverlay(Actions);
            Cursor = new RecordingCursor(initialCursor ?? new ScreenPoint(500, 500));
            Controller = new VirtualJoystickController(Stick, Overlay, Cursor);
        }
    }

    private sealed class RecordingStick(List<string> actions) : ILeftStickOutput
    {
        public (byte X, byte Y) LastValue { get; private set; } = (128, 128);

        public void SetLeftStick(byte x, byte y)
        {
            LastValue = (x, y);
            actions.Add($"stick:{x}/{y}");
        }
    }

    private sealed class RecordingOverlay(List<string> actions) : IJoystickOverlay
    {
        public bool IsVisible { get; private set; }
        public ScreenPoint? Center { get; private set; }
        public (double X, double Y) LastKnob { get; private set; }

        public void Show(ScreenPoint center)
        {
            Center = center;
            LastKnob = default;
            IsVisible = true;
            actions.Add("show");
        }

        public void UpdateKnob(double x, double y)
        {
            LastKnob = (x, y);
            actions.Add("update");
        }

        public void Hide()
        {
            IsVisible = false;
            Center = null;
            LastKnob = default;
            actions.Add("hide");
        }
    }

    private sealed class RecordingCursor(ScreenPoint initialPosition) : ICursorPositionProvider
    {
        public ScreenPoint Position { get; set; } = initialPosition;
        public bool Success { get; set; } = true;
        public int Win32Error { get; set; }
        public int ReadCount { get; private set; }

        public bool TryGetPosition(out ScreenPoint position, out int win32Error)
        {
            ReadCount++;
            position = Success ? Position : default;
            win32Error = Success ? 0 : Win32Error;
            return Success;
        }
    }
}
