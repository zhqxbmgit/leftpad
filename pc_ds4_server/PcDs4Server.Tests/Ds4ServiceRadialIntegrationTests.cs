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
            log.Contains("[环形菜单] Action 双击触发：cross", StringComparison.Ordinal));
        Assert.Single(fixture.RadialTriggers);
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
            log.Contains("[环形菜单] Action 双击触发：cross", StringComparison.Ordinal));
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
        Assert.Empty(fixture.RadialTriggers);
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
        Assert.Empty(fixture.RadialTriggers);
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
            log.Contains("[环形菜单] Action 双击触发：cross", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(100, false)]
    public void ActionDoubleTap_UsesRuntimeWindowAndPreservesPassThroughSemantics(
        int windowMilliseconds,
        bool shouldTrigger)
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;
        service.SetRadialDoubleTapWindow(windowMilliseconds);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 220, 240);

        Assert.Equal(
            shouldTrigger
                ? new[] { (DualShock4Button.Cross, true), (DualShock4Button.Cross, false) }
                : new[]
                {
                    (DualShock4Button.Cross, true),
                    (DualShock4Button.Cross, false),
                    (DualShock4Button.Cross, true),
                    (DualShock4Button.Cross, false)
                },
            fixture.DirectDs4.ButtonEvents);
        Assert.Equal(shouldTrigger, fixture.RadialTriggers.Count == 1);
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
            log.Contains($"[环形菜单] Action 双击触发：{protocolAction}", StringComparison.Ordinal));
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
        Assert.Empty(fixture.RadialTriggers);
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
            log.Contains("[环形菜单] MOVE 双击触发", StringComparison.Ordinal));
        Assert.Single(fixture.RadialTriggers);
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
            log.Contains("[环形菜单] MOVE 双击触发", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(100, false)]
    public void MoveDoubleTap_UsesTheSharedRuntimeWindow(int windowMilliseconds, bool shouldTrigger)
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;
        service.SetRadialDoubleTapWindow(windowMilliseconds);

        SendMoveTap(service, fixture.Clock, 0, 20);
        SendMoveTap(service, fixture.Clock, 220, 240);

        Assert.Equal(shouldTrigger, fixture.RadialTriggers.Count == 1);
    }

    [Fact]
    public void PreviewSettings_DoNotChangeRuntimeWindowUntilSettingsAreApplied()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;
        var overlay = new FakeRadialMenuOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        RadialMenuSettings temporary = RadialMenuSettings.Default with { DoubleTapWindowMs = 300 };

        controller.PreviewAt(new System.Drawing.Point(1000, 500), temporary);

        Assert.Equal(150, service.RadialDoubleTapWindowMs);

        controller.ApplySettings(temporary);
        service.SetRadialDoubleTapWindow(temporary.DoubleTapWindowMs);

        Assert.Equal(300, service.RadialDoubleTapWindowMs);
    }

    [Theory]
    [InlineData(79)]
    [InlineData(501)]
    public void InvalidRuntimeWindow_IsRejectedWithoutChangingActiveValue(int milliseconds)
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;

        Assert.Throws<ArgumentOutOfRangeException>(() => service.SetRadialDoubleTapWindow(milliseconds));
        Assert.Equal(150, service.RadialDoubleTapWindowMs);
    }

    [Fact]
    public void MoveSecondDownAfterProductionWindow_DoesNotTrigger()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;

        SendMoveTap(service, fixture.Clock, 0, 20);
        SendMoveTap(service, fixture.Clock, 171, 190);

        Assert.Empty(RadialLogs(fixture.Logs));
        Assert.Empty(fixture.RadialTriggers);
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
            log.Contains("[环形菜单] MOVE 双击触发", StringComparison.Ordinal));
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
        Assert.Empty(fixture.RadialTriggers);
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
            log.Contains("[环形菜单] MOVE 双击触发", StringComparison.Ordinal));
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

    [Fact]
    public void ActionOpenSource_ConfirmsSelectedSlotAndConsumesDownAndPairedUp()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 100, 120);
        Assert.Equal(RadialTriggerSource.ForAction("cross"), radial.Controller.OpenSource);
        Assert.True(radial.Controller.UpdateSelectionForCursor(new System.Drawing.Point(550, 550)));
        Assert.Equal(3, radial.Controller.SelectedSlot);

        fixture.Clock.SetMilliseconds(200);
        service.ProcessProtocolAction("cross", "down");

        RadialMenuCompletion completion = Assert.Single(radial.Completions);
        Assert.Equal(3, completion.SelectedSlot);
        Assert.False(radial.Controller.IsNormalMenuOpen);
        Assert.Equal(
            new[] { (DualShock4Button.Cross, true), (DualShock4Button.Cross, false) },
            fixture.DirectDs4.ButtonEvents);

        fixture.Clock.SetMilliseconds(220);
        service.ProcessProtocolAction("cross", "up");

        Assert.Equal(2, fixture.DirectDs4.ButtonEvents.Count);
        Assert.Equal(2, fixture.DirectDs4.SubmitReportCalls);
    }

    [Fact]
    public void ActionOpenSource_WithoutSelectionCancelsAndConsumesCompleteTap()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 100, 120);
        SendTap(service, fixture.Clock, "cross", 200, 220);

        RadialMenuCompletion completion = Assert.Single(radial.Completions);
        Assert.True(completion.IsCancelled);
        Assert.False(radial.Controller.IsNormalMenuOpen);
        Assert.Equal(2, fixture.DirectDs4.ButtonEvents.Count);
        Assert.Equal(2, fixture.DirectDs4.SubmitReportCalls);
    }

    [Fact]
    public void KeyboardActionOpenSource_ConsumesConfirmationWithoutSendingBoundKey()
    {
        var fixture = KeyboardFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);
        ConfigureBinding(service, "cross", KeyboardKey.Space);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 100, 120);
        SendTap(service, fixture.Clock, "cross", 200, 220);

        Assert.Single(radial.Completions);
        Assert.Equal(
            new[] { (KeyboardKey.Space, true), (KeyboardKey.Space, false) },
            fixture.Keyboard.Events);
    }

    [Fact]
    public void DifferentActionWhileMenuOpen_RoutesNormallyAndDoesNotConfirmOrClose()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 100, 120);
        SendTap(service, fixture.Clock, "circle", 200, 220);

        Assert.Empty(radial.Completions);
        Assert.True(radial.Controller.IsNormalMenuOpen);
        Assert.Equal(RadialTriggerSource.ForAction("cross"), radial.Controller.OpenSource);
        Assert.Equal(
            new[]
            {
                (DualShock4Button.Cross, true),
                (DualShock4Button.Cross, false),
                (DualShock4Button.Circle, true),
                (DualShock4Button.Circle, false)
            },
            fixture.DirectDs4.ButtonEvents);
        Assert.Single(fixture.RadialTriggers);
    }

    [Fact]
    public void MoveOpenSource_ConfirmsBeforeJoystickStateMachineAndConsumesPairedUp()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendMoveTap(service, fixture.Clock, 0, 20);
        SendMoveTap(service, fixture.Clock, 100, 120);
        Assert.Equal(RadialTriggerSource.Move, radial.Controller.OpenSource);
        Assert.True(radial.Controller.UpdateSelectionForCursor(new System.Drawing.Point(500, 550)));
        Assert.Equal(4, radial.Controller.SelectedSlot);
        VirtualJoystickSnapshot snapshotBeforeConfirmation = fixture.LastSnapshot;
        int cursorReadsBeforeConfirmation = fixture.Cursor.ReadCalls;
        int stickEventsBeforeConfirmation = fixture.DirectDs4.LeftStickEvents.Count;

        fixture.Clock.SetMilliseconds(200);
        service.ProcessProtocolAction("move", "down");
        fixture.Clock.SetMilliseconds(220);
        service.ProcessProtocolAction("move", "up");

        Assert.Equal(4, Assert.Single(radial.Completions).SelectedSlot);
        Assert.False(radial.Controller.IsNormalMenuOpen);
        Assert.Equal(snapshotBeforeConfirmation, fixture.LastSnapshot);
        Assert.Equal(cursorReadsBeforeConfirmation, fixture.Cursor.ReadCalls);
        Assert.Equal(stickEventsBeforeConfirmation, fixture.DirectDs4.LeftStickEvents.Count);
    }

    [Fact]
    public void MoveOpenSource_WithoutSelectionCancelsWithoutChangingMoveState()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendMoveTap(service, fixture.Clock, 0, 20);
        SendMoveTap(service, fixture.Clock, 100, 120);
        VirtualJoystickSnapshot snapshotBeforeConfirmation = fixture.LastSnapshot;
        int cursorReadsBeforeConfirmation = fixture.Cursor.ReadCalls;
        int stickEventsBeforeConfirmation = fixture.DirectDs4.LeftStickEvents.Count;

        SendMoveTap(service, fixture.Clock, 200, 220);

        Assert.True(Assert.Single(radial.Completions).IsCancelled);
        Assert.Equal(snapshotBeforeConfirmation, fixture.LastSnapshot);
        Assert.Equal(cursorReadsBeforeConfirmation, fixture.Cursor.ReadCalls);
        Assert.Equal(stickEventsBeforeConfirmation, fixture.DirectDs4.LeftStickEvents.Count);
    }

    [Fact]
    public void KeyboardMoveOpenSource_ConfirmationDoesNotChangeWasdOutput()
    {
        var fixture = KeyboardMoveFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendMoveTap(service, fixture.Clock, 0, 20);
        SendMoveTap(service, fixture.Clock, 100, 120);
        int keyboardEventsBeforeConfirmation = fixture.Keyboard.Events.Count;
        int cursorReadsBeforeConfirmation = fixture.Cursor.ReadCalls;

        SendMoveTap(service, fixture.Clock, 200, 220);

        Assert.Single(radial.Completions);
        Assert.Equal(keyboardEventsBeforeConfirmation, fixture.Keyboard.Events.Count);
        Assert.Equal(cursorReadsBeforeConfirmation, fixture.Cursor.ReadCalls);
    }

    [Fact]
    public void DelayedActionUp_RemainsSuppressedWhileUnrelatedActionRoutesNormally()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 100, 120);
        fixture.Clock.SetMilliseconds(200);
        service.ProcessProtocolAction("cross", "down");
        SendTap(service, fixture.Clock, "circle", 210, 220);
        fixture.Clock.SetMilliseconds(300);
        service.ProcessProtocolAction("cross", "up");

        Assert.Single(radial.Completions);
        Assert.Equal(
            new[]
            {
                (DualShock4Button.Cross, true),
                (DualShock4Button.Cross, false),
                (DualShock4Button.Circle, true),
                (DualShock4Button.Circle, false)
            },
            fixture.DirectDs4.ButtonEvents);
    }

    [Fact]
    public void ResetBetweenConfirmationDownAndUp_ClearsSuppressionForNextNormalInput()
    {
        var fixture = DirectFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendTap(service, fixture.Clock, "cross", 0, 20);
        SendTap(service, fixture.Clock, "cross", 100, 120);
        fixture.Clock.SetMilliseconds(200);
        service.ProcessProtocolAction("cross", "down");
        service.ReleaseAllControls(Ds4ControlResetReason.Disconnect);
        fixture.Clock.SetMilliseconds(220);
        service.ProcessProtocolAction("cross", "up");
        SendTap(service, fixture.Clock, "cross", 300, 320);

        Assert.Single(radial.Completions);
        Assert.Equal(
            new[]
            {
                (DualShock4Button.Cross, true),
                (DualShock4Button.Cross, false),
                (DualShock4Button.Cross, false),
                (DualShock4Button.Cross, true),
                (DualShock4Button.Cross, false)
            },
            fixture.DirectDs4.ButtonEvents);
    }

    [Fact]
    public void MoveStopAfterConfirmationDown_ClearsSuppressionForNextMoveTap()
    {
        var fixture = MoveFixture();
        using Ds4Service service = fixture.Service;
        using RadialControllerFixture radial = AttachRadialController(service);

        SendMoveTap(service, fixture.Clock, 0, 20);
        SendMoveTap(service, fixture.Clock, 100, 120);
        fixture.Clock.SetMilliseconds(200);
        service.ProcessProtocolAction("move", "down");
        fixture.Clock.SetMilliseconds(210);
        service.ProcessProtocolAction("move", "stop");
        int cursorReadsAfterStop = fixture.Cursor.ReadCalls;

        SendMoveTap(service, fixture.Clock, 300, 320);

        Assert.Single(radial.Completions);
        Assert.Equal(cursorReadsAfterStop + 1, fixture.Cursor.ReadCalls);
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
        var radialTriggers = new List<int>();
        service.OnLog += logs.Add;
        service.RadialMenuTriggered += _ => radialTriggers.Add(1);
        return new ServiceFixture(service, clock, factory.Session, keyboard, logs, radialTriggers);
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
        var radialTriggers = new List<int>();
        service.OnLog += logs.Add;
        service.RadialMenuTriggered += _ => radialTriggers.Add(1);
        return new ServiceFixture(service, clock, factory.Session, keyboard, logs, radialTriggers);
    }

    private static MoveServiceFixture MoveFixture()
    {
        return ConfigureMoveFixture(DirectFixture());
    }

    private static MoveServiceFixture KeyboardMoveFixture()
    {
        return ConfigureMoveFixture(KeyboardFixture());
    }

    private static MoveServiceFixture ConfigureMoveFixture(ServiceFixture fixture)
    {
        var cursor = new FakeCursor { Position = new ScreenPoint(500, 500) };
        fixture.Service.ConfigureVirtualJoystick(new FakeOverlay(), cursor);
        VirtualJoystickSnapshot lastSnapshot = ConfiguredController(fixture.Service).Snapshot;
        var moveFixture = new MoveServiceFixture(fixture, cursor, lastSnapshot);
        fixture.Service.OnJoystickStateChanged += snapshot => moveFixture.LastSnapshot = snapshot;
        return moveFixture;
    }

    private static RadialControllerFixture AttachRadialController(Ds4Service service)
    {
        var controller = new RadialMenuController(
            new FakeRadialMenuOverlay(),
            RadialMenuSettings.Default);
        var completions = new List<RadialMenuCompletion>();
        service.RadialMenuTriggered += source =>
            controller.OpenAt(new System.Drawing.Point(500, 500), source);
        service.RadialMenuConfirmationRequested += source =>
        {
            if (controller.TryCompleteFrom(source, out RadialMenuCompletion completion))
                completions.Add(completion);
        };
        controller.NormalMenuStateChanged += () =>
        {
            if (!controller.IsNormalMenuOpen) service.NotifyRadialMenuClosed();
        };
        return new RadialControllerFixture(controller, completions);
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
        logs.Where(log => log.Contains("[环形菜单]", StringComparison.Ordinal));

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
        List<string> Logs,
        List<int> RadialTriggers);

    private sealed record RadialControllerFixture(
        RadialMenuController Controller,
        List<RadialMenuCompletion> Completions) : IDisposable
    {
        public void Dispose() => Controller.Dispose();
    }

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
        public FakeKeyboardOutput Keyboard => _fixture.Keyboard;
        public List<string> Logs => _fixture.Logs;
        public List<int> RadialTriggers => _fixture.RadialTriggers;
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
        public int ReadCalls { get; private set; }

        public bool TryGetPosition(out ScreenPoint position, out int win32Error)
        {
            ReadCalls++;
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

    private sealed class FakeRadialMenuOverlay : IRadialMenuOverlay
    {
        public bool IsVisible { get; private set; }
        public void ShowAt(System.Drawing.Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
            IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Dispose() => IsVisible = false;
    }
}
