using System.Drawing;
using System.Reflection;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialActionPressSessionTests
{
    [Theory]
    [InlineData("circle")]
    [InlineData("move")]
    public void KeyboardShortTap_FollowsSourceDownUpWithoutLeakingTrigger(string source)
    {
        using var f = new Fixture(Key(), source);
        f.Open();
        f.Down();
        Assert.Equal(new[] { "F1 down" }, f.Keyboard.Events);
        AssertHeldAndHidden(f);
        f.Up();
        Assert.Equal(new[] { "F1 down", "F1 up" }, f.Keyboard.Events);
        AssertReleased(f);
        Assert.Empty(f.Ds4.Buttons);
        Assert.Equal(0, f.DelayCalls);
        Assert.Single(f.Logs, log => log.Contains("已执行：Slot 3", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("circle")]
    [InlineData("move")]
    public void KeyboardHold_RemainsPressedAcrossArbitraryEventsAndMismatchedUps(string source)
    {
        using var f = new Fixture(Key(), source);
        f.Open();
        f.Down();
        for (int index = 0; index < 20; index++)
        {
            f.Advance(1000);
            f.Service.UpdateRadialMenuSelection(() =>
                f.Controller.UpdateSelectionForCursor(new Point(400, 500)));
            f.Service.ProcessProtocolAction("triangle", "down");
            f.Service.ProcessProtocolAction("triangle", "up");
            f.Service.ProcessProtocolAction(source == "move" ? "circle" : "move", "up");
        }
        Assert.Equal(new[] { "F1 down" }, f.Keyboard.Events);
        Assert.Contains(KeyboardKey.F1, f.KeyState.CurrentPressedKeys);
        Assert.Equal(3, f.Session!.SelectedSlot);
        Assert.True(f.Service.HasRadialActionSession);
        f.Up();
        Assert.Equal(new[] { "F1 down", "F1 up" }, f.Keyboard.Events);
        AssertReleased(f);
    }

    [Fact]
    public void Shortcut_HoldsAllModifiersAndReleasesInExactReverseOrder()
    {
        using var f = new Fixture(Shortcut());
        f.Open();
        f.Down();
        string[] down = ["LeftControl down", "LeftAlt down", "LeftShift down", "LeftWin down", "G down"];
        Assert.Equal(down, f.Keyboard.Events);
        Assert.Equal(5, f.KeyState.CurrentPressedKeys.Count);
        f.Advance(5000);
        Assert.Equal(down, f.Keyboard.Events);
        f.Up();
        Assert.Equal(down.Concat(new[] { "G up", "LeftWin up", "LeftShift up", "LeftAlt up", "LeftControl up" }),
            f.Keyboard.Events);
        AssertReleased(f);
    }

    [Theory]
    [InlineData(KeyboardKey.LeftControl)]
    [InlineData(KeyboardKey.G)]
    public void KeyboardOwnership_ExternalSourceSurvivesRadialRelease(KeyboardKey externalKey)
    {
        using var f = new Fixture(Shortcut());
        f.KeyState.Press("external", externalKey);
        f.Open();
        f.Down();
        f.Up();
        Assert.Equal(new[] { externalKey }, f.KeyState.CurrentPressedKeys);
        Assert.DoesNotContain($"{externalKey} up", f.Keyboard.Events);
        Assert.Null(f.Session);
        f.KeyState.Release("external");
        Assert.Equal($"{externalKey} up", f.Keyboard.Events[^1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ShortcutBeginFailure_UnwindsAttemptedKeysInReverseAndClearsSession(int failOn)
    {
        using var f = new Fixture(Shortcut());
        f.Open();
        f.Keyboard.FailOnCall = failOn;
        f.Down();
        string[] keys = ["LeftControl", "LeftAlt", "LeftShift", "LeftWin", "G"];
        Assert.Equal(keys.Take(failOn).Select(key => key + " down")
            .Concat(keys.Take(failOn).Reverse().Select(key => key + " up")), f.Keyboard.Events);
        AssertReleased(f);
        Assert.Contains(f.Logs, log => log.Contains("动作开始失败", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void ShortcutReleaseFailure_BestEffortReleasesAllKeysAndClearsSession(int failOn)
    {
        using var f = new Fixture(Shortcut());
        f.Open();
        f.Down();
        f.Keyboard.FailOnCall = failOn;
        f.Up();
        AssertReleased(f);
        Assert.Empty(f.Keyboard.Pressed);
        Assert.Contains(f.Logs, log => log.Contains("动作释放失败", StringComparison.Ordinal));
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
    public void EveryCurrentDs4Action_HoldsUntilMatchingUpWithZeroPulseDelay(string targetId)
    {
        using var f = new Fixture(Ds4(targetId), "move");
        f.Open();
        f.Down();
        AssertHeldAndHidden(f);
        RadialDs4ActionCatalog.TryGet(targetId, out var target);
        if (target.Kind == RadialDs4ActionKind.DigitalButton)
        {
            Assert.Equal(new[] { (target.DigitalButton!, true) }, f.Ds4.Buttons);
            Assert.Contains(target.DigitalButton!, f.Ds4.Pressed);
        }
        else
        {
            Assert.Equal(new[] { target.DPadDirection! }, f.Ds4.DPads);
            Assert.Equal(DualShock4DPadDirection.South, f.Ds4.DPad);
        }
        f.Advance(5000);
        Assert.Equal(1, f.Ds4.SubmitCalls);
        f.Up();
        AssertReleased(f);
        Assert.Empty(f.Ds4.Pressed);
        Assert.Equal(DualShock4DPadDirection.None, f.Ds4.DPad);
        Assert.Equal(2, f.Ds4.SubmitCalls);
        Assert.Equal(0, f.DelayCalls);
    }

    [Theory]
    [InlineData("cross")]
    [InlineData("dpad_down")]
    public void ExistingDs4Target_IsNotAcquiredOrReleasedByRadial(string target)
    {
        using var f = new Fixture(Ds4(target));
        f.Open();
        if (target == "cross") f.Service.ProcessProtocolAction("cross", "down");
        else
        {
            // No ordinary protocol DPad action exists; seed the existing control authority.
            Field<Ds4ControlState>(f.Service, "_controlState").SetDPadDirection(DualShock4DPadDirection.South);
            f.Ds4.SetDPadDirection(DualShock4DPadDirection.South);
        }
        f.Ds4.Buttons.Clear();
        f.Ds4.DPads.Clear();
        f.Down();
        f.Up();
        Assert.Null(f.Session);
        Assert.Empty(f.Ds4.Buttons);
        Assert.Empty(f.Ds4.DPads);
        if (target == "cross") Assert.Contains(DualShock4Button.Cross, f.Ds4.Pressed);
        else Assert.Equal(DualShock4DPadDirection.South, f.Ds4.DPad);
        Assert.Contains(f.Logs, log => log.Contains("当前已按下或非空闲", StringComparison.Ordinal));
    }

    [Fact]
    public void SameTargetOrdinaryUp_CannotReleaseRadialOwnership()
    {
        using var f = new Fixture(Ds4("cross"));
        f.Open();
        f.Down();
        f.Service.ProcessProtocolAction("cross", "up");
        f.Service.ProcessProtocolAction("cross", "down");
        f.Service.ProcessProtocolAction("cross", "up");
        Assert.Equal(new[] { (DualShock4Button.Cross, true) }, f.Ds4.Buttons);
        Assert.Contains(DualShock4Button.Cross, f.Ds4.Pressed);
        f.Up();
        Assert.Equal(new[] { (DualShock4Button.Cross, true), (DualShock4Button.Cross, false) }, f.Ds4.Buttons);
        AssertReleased(f);
    }

    [Fact]
    public void SameTargetOrdinaryDown_TransfersHoldUntilOrdinaryUpAfterRadialRelease()
    {
        using var f = new Fixture(Ds4("cross"));
        f.Open();
        f.Down();
        f.Service.ProcessProtocolAction("cross", "down");
        f.Up();
        Assert.Null(f.Session);
        Assert.Equal(new[] { (DualShock4Button.Cross, true) }, f.Ds4.Buttons);
        f.Service.ProcessProtocolAction("cross", "up");
        Assert.Equal((DualShock4Button.Cross, false), f.Ds4.Buttons[^1]);
    }

    [Fact]
    public void OtherInputsWorkWhileHeld_AndDoubleTapCannotOpenSecondMenu()
    {
        using var f = new Fixture(Ds4("cross"));
        f.Open();
        f.Down();
        RadialActionPressSession first = f.Session!;
        f.Tap("triangle");
        f.Tap("triangle");
        f.Tap("move");
        f.Tap("move");
        Assert.Equal(1, f.OpenCount);
        Assert.Same(first, f.Session);
        Assert.Contains(DualShock4Button.Cross, f.Ds4.Pressed);
        Assert.Equal(2, f.Ds4.Buttons.Count(e => e == (DualShock4Button.Triangle, true)));
        Assert.Equal(2, f.Ds4.Buttons.Count(e => e == (DualShock4Button.Triangle, false)));
        f.Up();
        f.Open();
        Assert.Equal(2, f.OpenCount);
    }

    [Theory]
    [InlineData("circle", 0)]
    [InlineData("move", 0)]
    [InlineData("circle", 3)]
    [InlineData("move", 3)]
    public void CenterAndNone_CloseWithoutTargetAndConsumeBothTriggerEdges(string source, int slot)
    {
        using var f = new Fixture(RadialSlotMapping.None, source);
        f.Open(slot);
        f.Down();
        Assert.False(f.Controller.IsOpen);
        Assert.False(f.Overlay.IsVisible);
        Assert.Null(f.Session);
        Assert.Equal(f.Source, Field<RadialTriggerSource?>(f.Service, "_radialSuppressedUpSource"));
        f.Up();
        AssertReleased(f);
        Assert.Empty(f.Keyboard.Events);
        Assert.Empty(f.Ds4.Buttons);
        Assert.Empty(f.Ds4.DPads);
    }

    [Theory]
    [InlineData("circle", false)]
    [InlineData("move", false)]
    [InlineData("circle", true)]
    [InlineData("move", true)]
    public void QuickDownUpBeforeUiDispatch_ProducesExactlyOneOrderedBeginEnd(string source, bool ds4)
    {
        using var f = new Fixture(ds4 ? Ds4("cross") : Key(), source, queuedUi: true);
        f.Open();
        f.Down();
        f.Up();
        Assert.True(f.Session!.ReleaseRequested);
        Assert.Empty(f.Keyboard.Events);
        Assert.Empty(f.Ds4.Buttons);
        Assert.True(f.Controller.IsNormalMenuOpen);
        int selectionUpdates = 0;
        f.Service.UpdateRadialMenuSelection(() =>
        {
            selectionUpdates++;
            f.Controller.UpdateSelectionForCursor(new Point(400, 500));
        });
        Assert.Equal(0, selectionUpdates);
        Assert.Equal(3, f.Controller.SelectedSlot);
        f.Dispatch();
        AssertReleased(f);
        if (ds4)
            Assert.Equal(new[] { (DualShock4Button.Cross, true), (DualShock4Button.Cross, false) }, f.Ds4.Buttons);
        else
            Assert.Equal(new[] { "F1 down", "F1 up" }, f.Keyboard.Events);
        Assert.Equal(1, f.SelectionCompletions);
        Assert.Equal(0, f.DelayCalls);
    }

    [Fact]
    public void DuplicateDispatchAndRepeatedSourceDown_DoNotBeginTwice()
    {
        using var f = new Fixture(Key(), queuedUi: true);
        f.Open();
        f.Down();
        RadialActionPressSession token = f.Session!;
        f.Down();
        Assert.Single(f.Pending);
        f.Dispatch();
        f.Complete(token);
        f.Down();
        Assert.Equal(new[] { "F1 down" }, f.Keyboard.Events);
        Assert.Equal(1, f.SelectionCompletions);
        f.Up();
        AssertReleased(f);
    }

    [Theory]
    [InlineData(Ds4ControlResetReason.Disconnect, false)]
    [InlineData(Ds4ControlResetReason.SessionReplacement, false)]
    [InlineData(Ds4ControlResetReason.ServiceStop, false)]
    [InlineData(Ds4ControlResetReason.OutputFailure, false)]
    [InlineData(Ds4ControlResetReason.Disconnect, true)]
    [InlineData(Ds4ControlResetReason.SessionReplacement, true)]
    [InlineData(Ds4ControlResetReason.ServiceStop, true)]
    [InlineData(Ds4ControlResetReason.OutputFailure, true)]
    public void ResetBeforeUiDispatch_InvalidatesTokenEvenIfSameSourceReopens(
        Ds4ControlResetReason reason, bool ds4)
    {
        using var f = new Fixture(ds4 ? Ds4("cross") : Key(), queuedUi: true);
        f.Open();
        f.Down();
        f.Up();
        RadialActionPressSession oldToken = f.Session!;
        f.Service.ReleaseAllControls(reason);
        f.Open();
        f.Down();
        RadialActionPressSession newToken = f.Session!;
        f.Complete(oldToken);
        Assert.Same(newToken, f.Session);
        Assert.True(f.Controller.IsOpen);
        Assert.Equal(0, f.SelectionCompletions);
        f.Complete(newToken);
        f.Up();
        AssertReleased(f);
        Assert.Equal(1, f.SelectionCompletions);
    }

    [Theory]
    [InlineData(Ds4ControlResetReason.Disconnect, "keyboard")]
    [InlineData(Ds4ControlResetReason.SessionReplacement, "keyboard")]
    [InlineData(Ds4ControlResetReason.ServiceStop, "keyboard")]
    [InlineData(Ds4ControlResetReason.OutputFailure, "keyboard")]
    [InlineData(Ds4ControlResetReason.Disconnect, "cross")]
    [InlineData(Ds4ControlResetReason.SessionReplacement, "cross")]
    [InlineData(Ds4ControlResetReason.ServiceStop, "cross")]
    [InlineData(Ds4ControlResetReason.OutputFailure, "cross")]
    [InlineData(Ds4ControlResetReason.Disconnect, "dpad_down")]
    [InlineData(Ds4ControlResetReason.SessionReplacement, "dpad_down")]
    [InlineData(Ds4ControlResetReason.ServiceStop, "dpad_down")]
    [InlineData(Ds4ControlResetReason.OutputFailure, "dpad_down")]
    public void ReleaseAllAuthorities_ReleaseOwnedOutputAndAllSessionMetadata(
        Ds4ControlResetReason reason, string target)
    {
        using var f = new Fixture(target == "keyboard" ? Shortcut() : Ds4(target));
        f.Open();
        f.Down();
        f.Service.ReleaseAllControls(reason);
        AssertReleased(f);
        Assert.Empty(f.Keyboard.Pressed);
        Assert.Empty(f.Ds4.Pressed);
        Assert.Equal(DualShock4DPadDirection.None, f.Ds4.DPad);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ServiceStopAndDispose_ReleaseHeldOutputs(bool ds4, bool dispose)
    {
        using var f = new Fixture(ds4 ? Ds4("cross") : Shortcut());
        f.Open();
        f.Down();
        if (dispose) f.Service.Dispose();
        else f.Service.Stop();
        AssertReleased(f);
        Assert.Empty(f.Keyboard.Pressed);
        Assert.Empty(f.Ds4.Pressed);
    }

    [Theory]
    [InlineData(JoystickResetReason.Disconnect)]
    [InlineData(JoystickResetReason.SessionReplacement)]
    [InlineData(JoystickResetReason.ServiceStop)]
    [InlineData(JoystickResetReason.NormalExit)]
    [InlineData(JoystickResetReason.ControllerDispose)]
    [InlineData(JoystickResetReason.CursorPositionFailure)]
    public void InputReset_CannotOrphanTheHeldSession(JoystickResetReason reason)
    {
        using var f = new Fixture(Shortcut());
        f.Open();
        f.Down();
        f.Service.ResetVirtualJoystick(reason);
        AssertReleased(f);
        Assert.Empty(f.Keyboard.Pressed);
    }

    [Theory]
    [InlineData("cross", false)]
    [InlineData("dpad_down", false)]
    [InlineData("cross", true)]
    [InlineData("dpad_down", true)]
    public void Ds4OutputFailure_OnBeginOrRelease_RetriesNeutralViaReleaseAll(string target, bool release)
    {
        using var f = new Fixture(Ds4(target));
        f.Open();
        if (release) f.Down();
        f.Ds4.FailOnSubmit = release ? 2 : 1;
        if (release) f.Up();
        else f.Down();
        AssertReleased(f);
        Assert.Empty(f.Ds4.Pressed);
        Assert.Equal(DualShock4DPadDirection.None, f.Ds4.DPad);
        Assert.Contains(f.Logs, log => log.Contains(release ? "动作释放失败" : "动作开始失败", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedDs4SubmitFailure_DoesNotEscapeUiConfirmationOrLeaveSession(bool release)
    {
        using var f = new Fixture(Ds4("cross"));
        f.Open();
        if (release) f.Down();
        f.Ds4.FailEverySubmit = true;
        if (release) f.Up();
        else f.Down();
        AssertReleased(f);
        Assert.Empty(f.Ds4.Pressed);
        Assert.Contains(f.Logs, log => log.Contains("输出安全释放失败", StringComparison.Ordinal));
        Assert.Contains(f.Logs, log => log.Contains(release ? "动作释放失败" : "动作开始失败", StringComparison.Ordinal));
        f.Ds4.FailEverySubmit = false;
    }

    [Fact]
    public void UnrelatedKeyboardOutputFailure_ClearsRadialOwnershipToo()
    {
        using var f = new Fixture(Key(), keyboardMode: true);
        var bindings = f.Service.KeyboardBindings;
        bindings.Triangle = KeyboardKey.G;
        Assert.True(f.Service.TryUpdateKeyboardBindings(bindings));
        f.Open();
        f.Down();
        f.Keyboard.FailOnCall = 2;
        f.Service.ProcessProtocolAction("triangle", "down");
        AssertReleased(f);
        Assert.Empty(f.Keyboard.Pressed);
    }

    [Fact]
    public void MouseCloseAndPreview_DoNotBeginOutput()
    {
        using var f = new Fixture(Key(), queuedUi: true);
        f.Open();
        f.Controller.Close();
        Assert.Null(f.Session);
        f.Controller.PreviewAt(new Point(500, 500), f.Settings);
        f.Tap("circle");
        f.Tap("circle");
        Assert.False(f.Controller.IsNormalMenuOpen);
        Assert.Null(f.Session);
        Assert.Empty(f.Keyboard.Events);
        f.Controller.Close();
        f.Open();
        f.Down();
        f.Controller.PreviewAt(new Point(500, 500), f.Settings);
        f.Dispatch();
        f.Up();
        Assert.True(f.Controller.IsPreviewActive);
        Assert.Null(f.Session);
        Assert.Empty(f.Keyboard.Events);
    }

    [Fact]
    public void MappingSnapshot_IsFrozenAndOutputModeCannotChangeDuringHold()
    {
        using var f = new Fixture(Key());
        f.Open();
        f.Down();
        f.Controller.ApplySettings(f.Settings with
        {
            SlotMappings = f.Settings.SlotMappings.WithSlot(3, Key(KeyboardKey.G))
        });
        Assert.False(f.Service.TrySetOutputMode(OutputMode.Keyboard));
        f.Up();
        Assert.Equal(new[] { "F1 down", "F1 up" }, f.Keyboard.Events);
        AssertReleased(f);
    }

    private static void AssertHeldAndHidden(Fixture f)
    {
        Assert.True(f.Service.HasRadialActionSession);
        Assert.True(f.Session!.BeginHandled);
        Assert.Equal(3, f.Session.SelectedSlot);
        Assert.False(f.Controller.IsNormalMenuOpen);
        Assert.False(f.Overlay.IsVisible);
        Assert.Null(Field<RadialTriggerSource?>(f.Service, "_radialOpenSource"));
    }

    private static void AssertReleased(Fixture f)
    {
        Assert.False(f.Service.HasRadialActionSession);
        Assert.Null(f.Session);
        Assert.Empty(f.KeyState.CurrentPressedKeys);
        Assert.Null(Field<RadialTriggerSource?>(f.Service, "_radialSuppressedUpSource"));
    }

    internal static RadialSlotMapping Key(KeyboardKey key = KeyboardKey.F1) =>
        new() { Kind = RadialActionKind.KeyboardKey, Key = key };
    private static RadialSlotMapping Shortcut() => new()
    {
        Kind = RadialActionKind.KeyboardShortcut, Key = KeyboardKey.G,
        Ctrl = true, Alt = true, Shift = true, Win = true
    };
    private static RadialSlotMapping Ds4(string target) => new()
    {
        Kind = RadialActionKind.Ds4Button, Ds4Button = target
    };

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private sealed class Fixture : IDisposable
    {
        private int _time;
        private readonly string _source;
        public Fixture(RadialSlotMapping mapping, string source = "circle", bool queuedUi = false,
            bool keyboardMode = false)
        {
            _source = source;
            Settings = RadialMenuSettings.SafeFallback with
            {
                SlotMappings = RadialMenuSettings.SafeFallback.SlotMappings.WithSlot(3, mapping)
            };
            Service = new Ds4Service(new OutputFactory(Ds4), Keyboard, new MemoryStore(),
                inputTimestampProvider: () => TimeSpan.FromMilliseconds(_time),
                radialDs4Delay: _ => DelayCalls++);
            if (keyboardMode) Assert.True(Service.TrySetOutputMode(OutputMode.Keyboard));
            Assert.True(Service.Initialize());
            Service.ConfigureVirtualJoystick(new JoystickOverlay(), new FixedCursor());
            Controller = new RadialMenuController(Overlay, Settings);
            Service.OnLog += Logs.Add;
            Service.OnInputStateReset += _ => Controller.Close();
            Service.RadialMenuTriggered += trigger =>
            {
                OpenCount++;
                Controller.OpenAt(new Point(500, 500), trigger);
                if (!Controller.IsNormalMenuOpen) Service.NotifyRadialMenuClosed();
            };
            Controller.NormalMenuStateChanged += () =>
            {
                if (!Controller.IsNormalMenuOpen) Service.NotifyRadialMenuClosed();
            };
            Service.RadialMenuConfirmationRequested += request =>
            {
                if (queuedUi) Pending.Enqueue(request);
                else Complete(request);
            };
        }

        public RadialMenuSettings Settings { get; }
        public Ds4Service Service { get; }
        public RadialMenuController Controller { get; }
        public RadialOverlay Overlay { get; } = new();
        public KeyboardOutput Keyboard { get; } = new();
        public Ds4Output Ds4 { get; } = new();
        public List<string> Logs { get; } = [];
        public Queue<RadialActionPressSession> Pending { get; } = new();
        public int DelayCalls { get; private set; }
        public int OpenCount { get; private set; }
        public int SelectionCompletions { get; private set; }
        public RadialActionPressSession? Session => Field<RadialActionPressSession?>(Service, "_radialActionSession");
        public KeyboardKeyState KeyState => Field<KeyboardKeyState>(Service, "_keyboardState");
        public RadialTriggerSource Source => _source == "move" ? RadialTriggerSource.Move :
            RadialTriggerSource.ForAction(_source);

        public void Open(int selectedSlot = 3)
        {
            Advance(1000);
            Tap(_source);
            Tap(_source);
            Assert.True(Controller.IsNormalMenuOpen);
            if (selectedSlot != 0) Controller.UpdateSelectionForCursor(new Point(550, 550));
            Assert.Equal(selectedSlot, Controller.SelectedSlot);
            Keyboard.Events.Clear();
            Keyboard.CallCount = 0;
            Ds4.Buttons.Clear();
            Ds4.DPads.Clear();
            Ds4.SubmitCalls = 0;
            Logs.Clear();
        }
        public void Advance(int milliseconds) => _time += milliseconds;
        public void Down() { Advance(20); Service.ProcessProtocolAction(_source, "down"); }
        public void Up() { Advance(20); Service.ProcessProtocolAction(_source, "up"); }
        public void Tap(string source)
        {
            Advance(50);
            Service.ProcessProtocolAction(source, "down");
            Advance(20);
            Service.ProcessProtocolAction(source, "up");
        }
        public void Dispatch()
        {
            while (Pending.TryDequeue(out var request)) Complete(request);
        }
        public void Complete(RadialActionPressSession request) => Service.CompleteRadialActionPress(request, () =>
        {
            if (!Controller.TryCompleteFrom(request.Source, out var completion)) return null;
            SelectionCompletions++;
            var settings = Controller.ActiveSettings;
            return new RadialActionSelection(completion.SelectedSlot,
                RadialActionResolver.GetMapping(settings, settings.MappingProfileId, completion.SelectedSlot));
        });
        public void Dispose() { Service.Dispose(); Controller.Dispose(); }
    }

    private sealed class KeyboardOutput : IKeyboardOutput
    {
        public List<string> Events { get; } = [];
        public HashSet<KeyboardKey> Pressed { get; } = [];
        public int CallCount { get; set; }
        public int? FailOnCall { get; set; }
        public void SetKeyState(KeyboardKey key, bool isPressed)
        {
            Events.Add($"{key} {(isPressed ? "down" : "up")}");
            if (++CallCount == FailOnCall) throw new InvalidOperationException("injected keyboard failure");
            if (isPressed) Pressed.Add(key);
            else Pressed.Remove(key);
        }
    }
    private sealed class Ds4Output : IDirectDs4Session
    {
        public List<(DualShock4Button, bool)> Buttons { get; } = [];
        public List<DualShock4DPadDirection> DPads { get; } = [];
        public HashSet<DualShock4Button> Pressed { get; } = [];
        public DualShock4DPadDirection DPad { get; private set; } = DualShock4DPadDirection.None;
        public int SubmitCalls { get; set; }
        public int? FailOnSubmit { get; set; }
        public bool FailEverySubmit { get; set; }
        public void SetButton(DualShock4Button button, bool pressed)
        {
            Buttons.Add((button, pressed));
            if (pressed) Pressed.Add(button); else Pressed.Remove(button);
        }
        public void SetDPadDirection(DualShock4DPadDirection direction) { DPads.Add(direction); DPad = direction; }
        public void SetTrigger(DualShock4Slider trigger, byte value) { }
        public void SetLeftStick(byte x, byte y) { }
        public void SubmitReport()
        {
            if (++SubmitCalls == FailOnSubmit || FailEverySubmit)
                throw new InvalidOperationException("injected submit failure");
        }
        public void Dispose() { }
    }
    private sealed class OutputFactory(Ds4Output output) : IDirectDs4Factory
    {
        public IDirectDs4Session Create() => output;
    }
    private sealed class MemoryStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();
        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
    }
    private sealed class FixedCursor : ICursorPositionProvider
    {
        public bool TryGetPosition(out ScreenPoint position, out int win32Error)
        { position = new ScreenPoint(500, 500); win32Error = 0; return true; }
    }
    private sealed class JoystickOverlay : IJoystickOverlay
    {
        public bool IsVisible { get; private set; }
        public void Show(ScreenPoint center) => IsVisible = true;
        public void UpdateKnob(double x, double y) { }
        public void Hide() => IsVisible = false;
    }
    private sealed class RadialOverlay : IRadialMenuOverlay
    {
        public bool IsVisible { get; private set; }
        public void ShowAt(Point point, RadialMenuSettings settings, int selectedSlot) => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Dispose() { }
    }
}
