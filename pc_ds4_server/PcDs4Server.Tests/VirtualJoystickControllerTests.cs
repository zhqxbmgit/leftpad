using System.Drawing;
using PcDs4Server;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class VirtualJoystickControllerTests
{
    [Fact]
    public void ControlConstants_MatchVampireSurvivorsCursorModel()
    {
        Assert.Equal(26.0, VirtualJoystickController.JoystickRadius);
        Assert.Equal(2.0, VirtualJoystickController.ActivationRadius);
        Assert.Null(typeof(VirtualJoystickController).GetField("MinimumOutputMagnitude"));
        Assert.Equal(10, CursorJoystickSampler.IntervalMilliseconds);
        Assert.Equal(26, VirtualJoystickOverlay.VisualRadius);
        Assert.Equal(52, VirtualJoystickOverlay.BaseDiameter);
        Assert.Equal(26, VirtualJoystickOverlay.KnobDiameter);
        Assert.Equal(1.0, VirtualJoystickOverlay.VisualScale);
        Assert.Equal(
            2.0 / 26.0,
            VirtualJoystickController.ActivationRadius / VirtualJoystickController.JoystickRadius);
        Assert.Equal(
            0.5,
            (double)VirtualJoystickOverlay.KnobDiameter / VirtualJoystickOverlay.BaseDiameter);
        Assert.Equal(155, VirtualJoystickOverlay.BaseFillAlpha);
        Assert.Equal(191, VirtualJoystickOverlay.KnobAlpha);
        Assert.Equal(Color.FromArgb(155, 255, 255, 255), VirtualJoystickOverlay.BaseFillColor);
        Assert.Equal(Color.FromArgb(191, 255, 255, 255), VirtualJoystickOverlay.KnobFillColor);
        Assert.True(VirtualJoystickOverlay.KnobAlpha > VirtualJoystickOverlay.BaseFillAlpha);

        using var overlay = new VirtualJoystickOverlay();
        Assert.Equal(1.0, overlay.Opacity);
        Assert.Equal(Color.Empty, overlay.TransparencyKey);
    }

    [Fact]
    public void InitialState_IsStoppedNeutralAndUnlocked()
    {
        var fixture = new Fixture();
        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;

        Assert.False(state.MoveButtonPressed);
        Assert.False(state.JoystickActive);
        Assert.False(state.MovementLocked);
        Assert.False(state.DirectionCapturedDuringHold);
        Assert.Equal(0.0, state.CurrentDirectionX);
        Assert.Equal(0.0, state.CurrentDirectionY);
        Assert.Equal(0.0, state.LockedDirectionX);
        Assert.Equal(0.0, state.LockedDirectionY);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.False(fixture.Overlay.IsVisible);
    }

    [Fact]
    public void OverlayForm_RecalculatesCanvasForEnlargedVisuals()
    {
        using var overlay = new VirtualJoystickOverlay();

        Assert.Equal(82, overlay.ClientSize.Width);
        Assert.Equal(82, overlay.ClientSize.Height);
        int transparentMargin = (overlay.ClientSize.Width - VirtualJoystickOverlay.BaseDiameter) / 2;
        Assert.Equal(15, transparentMargin);
        Assert.True(transparentMargin >= (VirtualJoystickOverlay.KnobDiameter / 2) + 2);
    }

    [Theory]
    [InlineData(26.0, 26.0)]
    [InlineData(20.0, 20.0)]
    [InlineData(12.0, 12.0)]
    [InlineData(-26.0, -26.0)]
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
        double logical = 26.0 / Math.Sqrt(2.0);

        Assert.Equal(18.384776, VirtualJoystickOverlay.MapLogicalOffsetToVisual(logical), 6);
        Assert.Equal(-18.384776, VirtualJoystickOverlay.MapLogicalOffsetToVisual(-logical), 6);
    }

    [Theory]
    [InlineData(26.0, 0.0)]
    [InlineData(-26.0, 0.0)]
    [InlineData(0.0, 26.0)]
    [InlineData(0.0, -26.0)]
    [InlineData(18.384776310850235, 18.384776310850235)]
    [InlineData(18.384776310850235, -18.384776310850235)]
    [InlineData(-18.384776310850235, 18.384776310850235)]
    [InlineData(-18.384776310850235, -18.384776310850235)]
    public void KnobAtMaximumCardinalAndDiagonalOffsets_RemainsInsideCanvas(
        double logicalX,
        double logicalY)
    {
        using var overlay = new VirtualJoystickOverlay();
        double center = overlay.ClientSize.Width / 2.0;
        double knobRadius = VirtualJoystickOverlay.KnobDiameter / 2.0;
        double visualX = VirtualJoystickOverlay.MapLogicalOffsetToVisual(logicalX);
        double visualY = VirtualJoystickOverlay.MapLogicalOffsetToVisual(logicalY);
        double safeMargin = 2.0;

        Assert.True(center + visualX - knobRadius >= safeMargin);
        Assert.True(center + visualY - knobRadius >= safeMargin);
        Assert.True(center + visualX + knobRadius <= overlay.ClientSize.Width - safeMargin);
        Assert.True(center + visualY + knobRadius <= overlay.ClientSize.Height - safeMargin);
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
        Assert.False(state.MovementLocked);
        Assert.False(state.DirectionCapturedDuringHold);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
    }

    [Fact]
    public void DeltaTen_ProducesFullMagnitudeWhileOverlayTracksRealCursorGeometry()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(510, 500));

        AssertState(fixture.Controller.Snapshot, 10, 0, 10, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
        Assert.True(fixture.Controller.Snapshot.DirectionCapturedDuringHold);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(40)]
    public void DistanceBeyondActivationRadius_ProducesFullMagnitude(int deltaX)
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500));

        AssertState(
            fixture.Controller.Snapshot,
            deltaX,
            0,
            Math.Min(deltaX, 26),
            0,
            1,
            0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
        Assert.Equal(1.0, StickMagnitude(fixture.Controller.Snapshot), 6);
        Assert.Equal(byte.MaxValue, fixture.Controller.Snapshot.Ds4X);
    }

    [Fact]
    public void FullMagnitude_IsIndependentOfPhysicalTravel()
    {
        var fixture = ActiveFixture();
        (byte X, byte Y)? firstOutput = null;

        foreach (int distance in new[] { 3, 5, 10, 15, 20, 25, 26 })
        {
            fixture.Controller.UpdateCursor(new ScreenPoint(500 + distance, 500));
            VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
            Assert.Equal(1.0, StickMagnitude(state), 6);
            firstOutput ??= (state.Ds4X, state.Ds4Y);
            Assert.Equal(firstOutput.Value, (state.Ds4X, state.Ds4Y));
        }
    }

    [Theory]
    [InlineData(15, 0, 1.0, 0.0)]
    [InlineData(25, 0, 1.0, 0.0)]
    [InlineData(-15, 0, -1.0, 0.0)]
    [InlineData(-25, 0, -1.0, 0.0)]
    [InlineData(0, -15, 0.0, -1.0)]
    [InlineData(0, -25, 0.0, -1.0)]
    [InlineData(0, 15, 0.0, 1.0)]
    [InlineData(0, 25, 0.0, 1.0)]
    public void CardinalDirections_AreFullMagnitudeAtMultipleDistances(
        int deltaX,
        int deltaY,
        double expectedDirectionX,
        double expectedDirectionY)
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500 + deltaY));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.Equal(expectedDirectionX, state.CurrentDirectionX, 6);
        Assert.Equal(expectedDirectionY, state.CurrentDirectionY, 6);
        Assert.Equal(1.0, StickMagnitude(state), 6);
        Assert.Equal(expectedDirectionX, state.StickX, 6);
        Assert.Equal(expectedDirectionY, state.StickY, 6);
        Assert.Equal(VirtualJoystickController.MapAxis(state.StickX), state.Ds4X);
        Assert.Equal(VirtualJoystickController.MapAxis(state.StickY), state.Ds4Y);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(15, 15)]
    [InlineData(20, 20)]
    [InlineData(25, 25)]
    [InlineData(26, 26)]
    [InlineData(40, 26)]
    public void CursorDistance_MapsOneToOneToVisualRadiusThenClamps(
        int deltaX,
        double expectedKnobX)
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.Equal((double)deltaX, state.CursorDistance);
        Assert.Equal(expectedKnobX, state.LogicalKnobX);
        Assert.Equal((expectedKnobX, 0.0), fixture.Overlay.LastKnob);
    }

    [Fact]
    public void DeltaFifteen_IsFullStrengthInsideVisualRadius()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(515, 500));

        AssertState(fixture.Controller.Snapshot, 15, 0, 15, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
        Assert.Equal((byte.MaxValue, (byte)128), fixture.Stick.LastValue);
    }

    [Fact]
    public void DeltaOneHundred_ClampsKnobToJoystickRadiusAndStaysFullStrength()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(600, 500));

        AssertState(fixture.Controller.Snapshot, 100, 0, 26, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(70)]
    public void DeltaOutsideRadius_ClampsKnobToJoystickRadiusAndStaysFullStrength(int deltaX)
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500));

        AssertState(fixture.Controller.Snapshot, deltaX, 0, 26, 0, 1, 0);
        Assert.True(fixture.Controller.Snapshot.DirectionActive);
    }

    [Fact]
    public void DeltaOneHundredFifty_ClampsKnobToJoystickRadiusAndStaysFullStrength()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));

        AssertState(fixture.Controller.Snapshot, 150, 0, 26, 0, 1, 0);
    }

    [Fact]
    public void ReturningTowardCenter_RemainsFullUntilActivationBoundary()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));

        foreach (int currentX in new[] { 530, 525, 520, 515, 513 })
        {
            fixture.Controller.UpdateCursor(new ScreenPoint(currentX, 500));

            double distance = currentX - 500;
            AssertState(
                fixture.Controller.Snapshot,
                distance,
                0,
                Math.Min(distance, VirtualJoystickController.JoystickRadius),
                0,
                1,
                0);
        }

        fixture.Controller.UpdateCursor(new ScreenPoint(502, 500));
        AssertState(fixture.Controller.Snapshot, 2, 0, 2, 0, 0, 0);
        Assert.False(fixture.Controller.Snapshot.DirectionActive);
        Assert.True(fixture.Controller.Snapshot.DirectionCapturedDuringHold);
    }

    [Fact]
    public void ReturningInsideActivationRadiusAfterCapture_IsLiveNeutralButLocksLastValidDirectionOnRelease()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(520, 500));
        fixture.Controller.UpdateCursor(new ScreenPoint(502, 500));

        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.True(fixture.Controller.Snapshot.DirectionCapturedDuringHold);

        fixture.Controller.ReleaseMove();

        Assert.True(fixture.Controller.Snapshot.MovementLocked);
        Assert.Equal(1.0, fixture.Controller.Snapshot.LockedDirectionX);
        Assert.Equal(0.0, fixture.Controller.Snapshot.LockedDirectionY);
        Assert.Equal(1.0, StickMagnitude(fixture.Controller.Snapshot), 6);
        Assert.Equal((byte.MaxValue, (byte)128), fixture.Stick.LastValue);
    }

    [Fact]
    public void ReturningFromOneHundredFiftyToOneHundred_RemainsFullStrength()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));

        fixture.Controller.UpdateCursor(new ScreenPoint(600, 500));

        AssertState(fixture.Controller.Snapshot, 100, 0, 26, 0, 1, 0);
    }

    [Fact]
    public void DeltaNineteen_HasFullMagnitude()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));

        fixture.Controller.UpdateCursor(new ScreenPoint(519, 500));

        AssertState(fixture.Controller.Snapshot, 19, 0, 19, 0, 1, 0);
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
        Assert.Equal(18.384776, state.LogicalKnobX, 6);
        Assert.Equal(-18.384776, state.LogicalKnobY, 6);
        Assert.Equal(0.707107, state.StickX, 6);
        Assert.Equal(-0.707107, state.StickY, 6);
        Assert.Equal(1.0, Math.Sqrt((state.StickX * state.StickX) + (state.StickY * state.StickY)), 6);
        Assert.Equal(26.0, Math.Sqrt((state.LogicalKnobX * state.LogicalKnobX) + (state.LogicalKnobY * state.LogicalKnobY)), 6);
    }

    [Fact]
    public void ActivatedDirection_PreservesContinuousNonQuantizedAngle()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(520, 490));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.Equal(0.894427, state.CurrentDirectionX, 6);
        Assert.Equal(-0.447214, state.CurrentDirectionY, 6);
        Assert.Equal(state.CurrentDirectionX, state.StickX, 6);
        Assert.Equal(state.CurrentDirectionY, state.StickY, 6);
        Assert.Equal(20.0, state.LogicalKnobX, 6);
        Assert.Equal(-10.0, state.LogicalKnobY, 6);
        Assert.Equal(1.0, StickMagnitude(state), 6);
        Assert.Equal(22.36068, state.CursorDistance, 6);
    }

    [Fact]
    public void Diagonal_NormalizesDirectionToFullUnitCircleMagnitude()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(515, 515));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.Equal(0.707107, state.CurrentDirectionX, 6);
        Assert.Equal(0.707107, state.CurrentDirectionY, 6);
        Assert.Equal(1.0 / Math.Sqrt(2.0), state.StickX, 6);
        Assert.Equal(1.0 / Math.Sqrt(2.0), state.StickY, 6);
        Assert.Equal(1.0, StickMagnitude(state), 6);
        Assert.True(Math.Abs(state.StickX) < 1.0);
        Assert.True(Math.Abs(state.StickY) < 1.0);
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
            Assert.Equal(26.0, Math.Sqrt((state.LogicalKnobX * state.LogicalKnobX) + (state.LogicalKnobY * state.LogicalKnobY)), 6);
        }

        Assert.Equal(0.0, fixture.Controller.Snapshot.StickX, 6);
        Assert.Equal(-1.0, fixture.Controller.Snapshot.StickY, 6);
    }

    [Fact]
    public void NegativeYAtRadius_MapsToFullUp()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500, 474));

        Assert.Equal((128, 0), fixture.Stick.LastValue);
        Assert.Equal(-1.0, fixture.Controller.Snapshot.StickY);
    }

    [Fact]
    public void PositiveYAtRadius_MapsToFullDownWithoutExtraInversion()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500, 526));

        Assert.Equal((128, 255), fixture.Stick.LastValue);
        Assert.Equal(1.0, fixture.Controller.Snapshot.StickY);
    }

    [Fact]
    public void NegativeXAtRadius_MapsToFullLeft()
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(474, 500));

        Assert.Equal((0, 128), fixture.Stick.LastValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DisplacementAtOrWithinActivationRadius_RemainsNeutral(int deltaX)
    {
        var fixture = ActiveFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500));

        Assert.Equal(0.0, fixture.Controller.Snapshot.StickX);
        Assert.Equal((double)deltaX, fixture.Controller.Snapshot.LogicalKnobX);
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
        Assert.False(fixture.Controller.Snapshot.DirectionCapturedDuringHold);
        Assert.False(fixture.Controller.Snapshot.MovementLocked);
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
    public void MoveUpAfterValidDirection_LocksDirectionAndHidesWithoutNeutralizing()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(650, 500));
        fixture.Actions.Clear();

        fixture.Controller.ReleaseMove();

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.False(state.MoveButtonPressed);
        Assert.False(state.JoystickActive);
        Assert.True(state.MovementLocked);
        Assert.False(state.DirectionCapturedDuringHold);
        Assert.Null(state.Center);
        Assert.Null(state.CurrentCursor);
        Assert.Equal(0.0, state.CursorDistance);
        Assert.Equal(0.0, state.LogicalKnobX);
        Assert.Equal(1.0, state.CurrentDirectionX);
        Assert.Equal(1.0, state.LockedDirectionX);
        Assert.Equal(1.0, state.StickX);
        Assert.Equal((255, 128), fixture.Stick.LastValue);
        Assert.False(fixture.Overlay.IsVisible);
        Assert.Equal(new[] { "stick:255/128", "hide" }, fixture.Actions);
    }

    [Fact]
    public void SamplingAfterLockedMoveUp_IsIgnoredWithoutReadingCursorOrChangingDirection()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(520, 500));
        fixture.Controller.ReleaseMove();
        int readsAtRelease = fixture.Cursor.ReadCount;
        fixture.Cursor.Position = new ScreenPoint(400, 500);

        fixture.Controller.SampleCursor();

        Assert.Equal(readsAtRelease, fixture.Cursor.ReadCount);
        Assert.Equal((byte.MaxValue, (byte)128), fixture.Stick.LastValue);
        Assert.True(fixture.Controller.Snapshot.MovementLocked);
        Assert.False(fixture.Controller.Snapshot.JoystickActive);
    }

    [Theory]
    [InlineData(400, 500)]
    [InlineData(500, 400)]
    public void CursorUpdatesWhileLockedAndReleased_CannotChangeLockedRightDirection(
        int cursorX,
        int cursorY)
    {
        var fixture = LockedRightFixture();

        fixture.Controller.UpdateCursor(new ScreenPoint(cursorX, cursorY));

        Assert.Equal(1.0, fixture.Controller.Snapshot.LockedDirectionX);
        Assert.Equal(0.0, fixture.Controller.Snapshot.LockedDirectionY);
        Assert.Equal(1.0, fixture.Controller.Snapshot.StickX, 6);
        Assert.Equal(0.0, fixture.Controller.Snapshot.StickY);
        Assert.Equal((byte.MaxValue, (byte)128), fixture.Stick.LastValue);
    }

    [Fact]
    public void MoveDownWhileLocked_KeepsOldDirectionUntilNewDirectionIsValid()
    {
        var fixture = LockedRightFixture();
        fixture.Cursor.Position = new ScreenPoint(700, 500);

        Assert.True(fixture.Controller.TryMoveDown(out _));
        fixture.Controller.UpdateCursor(new ScreenPoint(702, 500));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.True(state.MoveButtonPressed);
        Assert.True(state.JoystickActive);
        Assert.True(state.MovementLocked);
        Assert.False(state.DirectionCapturedDuringHold);
        Assert.False(state.DirectionActive);
        Assert.Equal(new ScreenPoint(700, 500), state.Center);
        Assert.Equal(1.0, state.StickX, 6);
        Assert.Equal((byte.MaxValue, (byte)128), fixture.Stick.LastValue);
        Assert.True(fixture.Overlay.IsVisible);
    }

    [Fact]
    public void MoveDownWhileLocked_NewValidDirectionImmediatelyBecomesLiveLeftUp()
    {
        var fixture = LockedRightFixture();
        fixture.Cursor.Position = new ScreenPoint(700, 500);
        fixture.Controller.TryMoveDown(out _);

        fixture.Controller.UpdateCursor(new ScreenPoint(691, 488));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.True(state.DirectionCapturedDuringHold);
        Assert.Equal(-0.6, state.CurrentDirectionX, 6);
        Assert.Equal(-0.8, state.CurrentDirectionY, 6);
        Assert.Equal(-0.6, state.StickX, 6);
        Assert.Equal(-0.8, state.StickY, 6);
        Assert.Equal(1.0, StickMagnitude(state), 6);
    }

    [Fact]
    public void MoveUpAfterReadjustment_LocksNewLeftUpDirection()
    {
        var fixture = LockedRightFixture();
        fixture.Cursor.Position = new ScreenPoint(700, 500);
        fixture.Controller.TryMoveDown(out _);
        fixture.Controller.UpdateCursor(new ScreenPoint(691, 488));

        fixture.Controller.ReleaseMove();

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.True(state.MovementLocked);
        Assert.False(state.JoystickActive);
        Assert.Equal(-0.6, state.LockedDirectionX, 6);
        Assert.Equal(-0.8, state.LockedDirectionY, 6);
        Assert.Equal(-0.6, state.StickX, 6);
        Assert.Equal(-0.8, state.StickY, 6);
        Assert.Equal(1.0, StickMagnitude(state), 6);
        Assert.Equal(
            (
                VirtualJoystickController.MapAxis(state.StickX),
                VirtualJoystickController.MapAxis(state.StickY)),
            fixture.Stick.LastValue);
        Assert.False(fixture.Overlay.IsVisible);
    }

    [Fact]
    public void TapMoveWithoutDirectionWhileLocked_StopsMovement()
    {
        var fixture = LockedRightFixture();
        fixture.Cursor.Position = new ScreenPoint(700, 500);
        fixture.Controller.TryMoveDown(out _);

        fixture.Controller.ReleaseMove();

        AssertStopped(fixture);
    }

    [Fact]
    public void TapMoveWithoutDirectionWhileAlreadyStopped_RemainsStopped()
    {
        var fixture = ActiveFixture();

        fixture.Controller.ReleaseMove();

        AssertStopped(fixture);
    }

    [Fact]
    public void ValidDiagonalDirection_RemainsNormalizedAfterReleaseLock()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(600, 400));

        fixture.Controller.ReleaseMove();

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.True(state.MovementLocked);
        Assert.Equal(0.707107, state.LockedDirectionX, 6);
        Assert.Equal(-0.707107, state.LockedDirectionY, 6);
        Assert.Equal(1.0, StickMagnitude(state), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReleaseAtOrWithinActivationRadius_StopsWithoutCapturingDirection(int deltaX)
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(500 + deltaX, 500));

        fixture.Controller.ReleaseMove();

        AssertStopped(fixture);
    }

    [Fact]
    public void ReleaseBeyondActivationRadius_LocksFullDirection()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(520, 500));

        fixture.Controller.ReleaseMove();

        Assert.True(fixture.Controller.Snapshot.MovementLocked);
        Assert.Equal(1.0, fixture.Controller.Snapshot.LockedDirectionX);
        Assert.Equal(1.0, StickMagnitude(fixture.Controller.Snapshot), 6);
        Assert.Equal((byte.MaxValue, (byte)128), fixture.Stick.LastValue);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(26)]
    public void ReleaseAtDifferentDistances_LocksIdenticalFullDirection(int distance)
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(500 + distance, 500));

        fixture.Controller.ReleaseMove();

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.True(state.MovementLocked);
        Assert.Equal(1.0, state.LockedDirectionX);
        Assert.Equal(0.0, state.LockedDirectionY);
        Assert.Equal(1.0, StickMagnitude(state), 6);
        Assert.Equal((byte.MaxValue, (byte)128), fixture.Stick.LastValue);
    }

    [Fact]
    public void Reaim_FromRightToUp_UpdatesFullDirection()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(505, 500));
        fixture.Controller.ReleaseMove();
        Assert.Equal(1.0, StickMagnitude(fixture.Controller.Snapshot), 6);

        fixture.Cursor.Position = new ScreenPoint(700, 500);
        fixture.Controller.TryMoveDown(out _);
        fixture.Controller.UpdateCursor(new ScreenPoint(700, 485));
        fixture.Controller.ReleaseMove();

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.True(state.MovementLocked);
        Assert.Equal(0.0, state.LockedDirectionX, 6);
        Assert.Equal(-1.0, state.LockedDirectionY, 6);
        Assert.Equal(1.0, StickMagnitude(state), 6);
        Assert.Equal(((byte)128, byte.MinValue), fixture.Stick.LastValue);
    }

    [Fact]
    public void ExplicitStopFromLockedRight_NeutralizesUnlocksAndHides()
    {
        var fixture = LockedRightFixture();
        fixture.Actions.Clear();

        fixture.Controller.StopMovement();

        AssertStopped(fixture);
        Assert.Equal(new[] { "stick:128/128", "hide" }, fixture.Actions);
        Assert.Equal(JoystickResetReason.ExplicitStop, fixture.Controller.Snapshot.LastResetReason);
    }

    [Fact]
    public void ExplicitStopFromLockedDiagonal_ClearsNormalizedLockedDirection()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(600, 400));
        fixture.Controller.ReleaseMove();
        Assert.True(fixture.Controller.Snapshot.MovementLocked);
        Assert.Equal(1.0, StickMagnitude(fixture.Controller.Snapshot), 6);

        fixture.Controller.StopMovement();

        AssertStopped(fixture);
        Assert.Equal(0.0, fixture.Controller.Snapshot.LockedDirectionX);
        Assert.Equal(0.0, fixture.Controller.Snapshot.LockedDirectionY);
    }

    [Fact]
    public void ExplicitStopDuringLiveHeldDirection_NeutralizesAndHides()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(520, 500));
        Assert.True(fixture.Controller.Snapshot.DirectionCapturedDuringHold);
        fixture.Actions.Clear();

        fixture.Controller.StopMovement();

        AssertStopped(fixture);
        Assert.Equal(new[] { "stick:128/128", "hide" }, fixture.Actions);
        Assert.False(fixture.Controller.Snapshot.MoveButtonPressed);
        Assert.False(fixture.Controller.Snapshot.JoystickActive);
        Assert.False(fixture.Controller.Snapshot.DirectionCapturedDuringHold);
    }

    [Fact]
    public void RepeatedExplicitStop_IsIdempotent()
    {
        var fixture = LockedRightFixture();

        fixture.Controller.StopMovement();
        fixture.Controller.StopMovement();

        AssertStopped(fixture);
        Assert.Equal(JoystickResetReason.ExplicitStop, fixture.Controller.Snapshot.LastResetReason);
    }

    [Fact]
    public void MoveDownAfterExplicitStop_StartsANewLiveMovementRound()
    {
        var fixture = LockedRightFixture();
        fixture.Controller.StopMovement();
        fixture.Cursor.Position = new ScreenPoint(700, 500);

        Assert.True(fixture.Controller.TryMoveDown(out int error));
        fixture.Controller.UpdateCursor(new ScreenPoint(690, 490));

        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.Equal(0, error);
        Assert.True(state.MoveButtonPressed);
        Assert.True(state.JoystickActive);
        Assert.False(state.MovementLocked);
        Assert.True(state.DirectionCapturedDuringHold);
        double expectedStickAxis = -1.0 / Math.Sqrt(2.0);
        Assert.Equal(expectedStickAxis, state.StickX, 6);
        Assert.Equal(expectedStickAxis, state.StickY, 6);
        Assert.True(fixture.Overlay.IsVisible);
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
    public void MoveDownCursorFailureWhileLocked_ClearsLockAndNeutralizes()
    {
        var fixture = LockedRightFixture();
        fixture.Cursor.Success = false;
        fixture.Cursor.Win32Error = 5;

        bool activated = fixture.Controller.TryMoveDown(out int error);

        Assert.False(activated);
        Assert.Equal(5, error);
        AssertStopped(fixture);
        Assert.Equal(JoystickResetReason.CursorPositionFailure, fixture.Controller.Snapshot.LastResetReason);
    }

    [Fact]
    public void HeldSamplingCursorFailure_NeutralizesHidesAndRaisesError()
    {
        var fixture = LockedRightFixture();
        fixture.Cursor.Position = new ScreenPoint(700, 500);
        fixture.Controller.TryMoveDown(out _);
        int? reportedError = null;
        fixture.Controller.CursorPositionReadFailed += error => reportedError = error;
        fixture.Cursor.Success = false;
        fixture.Cursor.Win32Error = 1400;

        fixture.Controller.SampleCursor();

        Assert.Equal(1400, reportedError);
        Assert.False(fixture.Controller.Snapshot.JoystickActive);
        Assert.False(fixture.Controller.Snapshot.MovementLocked);
        Assert.Equal(0.0, fixture.Controller.Snapshot.LockedDirectionX);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.False(fixture.Overlay.IsVisible);
    }

    [Fact]
    public void UnexpectedCursorSamplingFailureDuringHeld_ClearsExistingLock()
    {
        var fixture = LockedRightFixture();
        fixture.Cursor.Position = new ScreenPoint(700, 500);
        fixture.Controller.TryMoveDown(out _);
        fixture.Controller.UpdateCursor(new ScreenPoint(690, 490));

        fixture.Controller.Reset(JoystickResetReason.CursorSamplingFailure);

        AssertStopped(fixture);
        Assert.Equal(
            JoystickResetReason.CursorSamplingFailure,
            fixture.Controller.Snapshot.LastResetReason);
    }

    [Theory]
    [InlineData(JoystickResetReason.ExplicitStop)]
    [InlineData(JoystickResetReason.Disconnect)]
    [InlineData(JoystickResetReason.SessionReplacement)]
    [InlineData(JoystickResetReason.ServiceStop)]
    [InlineData(JoystickResetReason.NormalExit)]
    [InlineData(JoystickResetReason.ControllerDispose)]
    public void SafetyReset_NeutralizesBeforeHiding(JoystickResetReason reason)
    {
        var fixture = LockedRightFixture();
        fixture.Actions.Clear();

        fixture.Controller.Reset(reason);

        Assert.Equal(new[] { "stick:128/128", "hide" }, fixture.Actions);
        Assert.False(fixture.Controller.Snapshot.JoystickActive);
        Assert.False(fixture.Controller.Snapshot.MovementLocked);
        Assert.False(fixture.Controller.Snapshot.DirectionCapturedDuringHold);
        Assert.Equal(0.0, fixture.Controller.Snapshot.LockedDirectionX);
        Assert.Equal(0.0, fixture.Controller.Snapshot.LockedDirectionY);
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

    private static double StickMagnitude(VirtualJoystickSnapshot state)
    {
        return Math.Sqrt((state.StickX * state.StickX) + (state.StickY * state.StickY));
    }

    private static void AssertStopped(Fixture fixture)
    {
        VirtualJoystickSnapshot state = fixture.Controller.Snapshot;
        Assert.False(state.MoveButtonPressed);
        Assert.False(state.JoystickActive);
        Assert.False(state.MovementLocked);
        Assert.False(state.DirectionCapturedDuringHold);
        Assert.Equal(0.0, state.CurrentDirectionX);
        Assert.Equal(0.0, state.CurrentDirectionY);
        Assert.Equal(0.0, state.LockedDirectionX);
        Assert.Equal(0.0, state.LockedDirectionY);
        Assert.Equal(0.0, state.StickX);
        Assert.Equal(0.0, state.StickY);
        Assert.Equal((128, 128), fixture.Stick.LastValue);
        Assert.False(fixture.Overlay.IsVisible);
    }

    private static Fixture ActiveFixture()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Controller.TryMoveDown(out _));
        return fixture;
    }

    private static Fixture LockedRightFixture()
    {
        var fixture = ActiveFixture();
        fixture.Controller.UpdateCursor(new ScreenPoint(520, 500));
        fixture.Controller.ReleaseMove();
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
