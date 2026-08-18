using System.Drawing;
using System.Windows.Forms;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialActionMappingTests
{
    [Fact]
    public void KeyboardKey_WithMainKey_IsValid()
    {
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.KeyboardKey,
            Key = KeyboardKey.F1
        };

        Assert.True(mapping.TryValidate(out _));
    }

    [Fact]
    public void KeyboardKey_WithoutMainKey_IsInvalidAndSanitizesToNone()
    {
        var mapping = new RadialSlotMapping { Kind = RadialActionKind.KeyboardKey };

        Assert.False(mapping.TryValidate(out _));
        Assert.Equal(RadialActionKind.None, mapping.Sanitize().Kind);
    }

    [Fact]
    public void KeyboardKey_ModifierCannotBeUsedAsMainKey()
    {
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.KeyboardKey,
            Key = KeyboardKey.LeftControl
        };

        Assert.False(mapping.TryValidate(out _));
    }

    [Fact]
    public void KeyboardShortcut_WithModifierAndMainKey_IsValid()
    {
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.KeyboardShortcut,
            Key = KeyboardKey.K,
            Ctrl = true
        };

        Assert.True(mapping.TryValidate(out _));
    }

    [Fact]
    public void KeyboardShortcut_WithoutModifier_IsInvalid()
    {
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.KeyboardShortcut,
            Key = KeyboardKey.K
        };

        Assert.False(mapping.TryValidate(out string error));
        Assert.Contains("至少需要一个", error);
    }

    [Fact]
    public void KeyboardShortcut_WithoutMainKey_IsInvalid()
    {
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.KeyboardShortcut,
            Ctrl = true
        };

        Assert.False(mapping.TryValidate(out _));
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
    public void Ds4Button_SupportedRadialAction_IsValid(string button)
    {
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.Ds4Button,
            Ds4Button = button
        };

        Assert.True(mapping.TryValidate(out _));
    }

    [Theory]
    [InlineData("r1")]
    [InlineData("l2")]
    [InlineData("r2")]
    [InlineData("anything_else")]
    public void Ds4Button_NonRadialAction_IsInvalidAndSanitizesToNone(string button)
    {
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.Ds4Button,
            Ds4Button = button
        };

        Assert.False(mapping.TryValidate(out _));
        Assert.Equal(RadialSlotMapping.None, mapping.Sanitize());
    }

    [Fact]
    public void Ds4Button_UnknownButton_IsInvalidAndSanitizesToNone()
    {
        var mapping = new RadialSlotMapping
        {
            Kind = RadialActionKind.Ds4Button,
            Ds4Button = "options"
        };

        Assert.False(mapping.TryValidate(out _));
        Assert.Equal(RadialActionKind.None, mapping.Sanitize().Kind);
    }

    [Fact]
    public void ActiveAndTemporaryMappings_AreIsolatedUntilApply()
    {
        var overlay = new FakeOverlay();
        RadialMenuSettings active = RadialMenuSettings.Default;
        RadialMenuSettings temporary = active with
        {
            SlotMappings = active.SlotMappings.WithSlot(2, new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardKey,
                Key = KeyboardKey.F1
            })
        };
        using var controller = new RadialMenuController(overlay, active);

        RadialMenuCompletion beforeApply = CompleteSlot(controller, 2);
        Assert.Equal(RadialActionKind.None,
            RadialActionResolver.GetMapping(controller.ActiveSettings, beforeApply).Kind);

        controller.ApplySettings(temporary);
        RadialMenuCompletion afterApply = CompleteSlot(controller, 2);

        RadialSlotMapping applied = RadialActionResolver.GetMapping(
            controller.ActiveSettings,
            afterApply);
        Assert.Equal(RadialActionKind.KeyboardKey, applied.Kind);
        Assert.Equal(KeyboardKey.F1, applied.Key);
    }

    [Fact]
    public void ResetDefaults_ChangesTemporaryMappingsOnlyUntilApply()
    {
        var overlay = new FakeOverlay();
        RadialMenuSettings customized = RadialMenuSettings.Default with
        {
            SlotMappings = RadialMenuSettings.Default.SlotMappings.WithSlot(2,
                new RadialSlotMapping
                {
                    Kind = RadialActionKind.KeyboardKey,
                    Key = KeyboardKey.F1
                })
        };
        using var controller = new RadialMenuController(overlay, customized);

        RadialMenuSettings resetTemporary = RadialMenuSettings.Default;

        Assert.All(resetTemporary.SlotMappings,
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
        Assert.Equal(RadialActionKind.KeyboardKey,
            controller.ActiveSettings.SlotMappings[1].Kind);

        controller.ApplySettings(resetTemporary);

        Assert.All(controller.ActiveSettings.SlotMappings,
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
    }

    [Fact]
    public void CompletionHandling_ExecutesKeyboardAndDs4MappingsAfterMenuClose()
    {
        var directFactory = new CountingDirectDs4Factory();
        var keyboardOutput = new CountingKeyboardOutput();
        using var service = new Ds4Service(
            directFactory,
            keyboardOutput,
            new MemoryBindingStore(),
            radialDs4Delay: _ => { });
        Assert.True(service.Initialize());

        RadialMenuSettings settings = RadialMenuSettings.Default with
        {
            SlotMappings = RadialSlotMappings.Create(new RadialSlotMapping[]
            {
                RadialSlotMapping.None,
                new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F1 },
                new()
                {
                    Kind = RadialActionKind.KeyboardShortcut,
                    Key = KeyboardKey.K,
                    Ctrl = true,
                    Shift = true
                },
                new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "cross" },
                new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "dpad_down" }
            })
        };
        using var controller = new RadialMenuController(new FakeOverlay(), settings);

        RadialMenuCompletion keyboardCompletion = CompleteSlot(controller, 2);
        Assert.False(controller.IsNormalMenuOpen);
        string keyboardLog = RadialActionCompletionHandler.Handle(
            service,
            controller.ActiveSettings,
            keyboardCompletion);
        RadialMenuCompletion shortcutCompletion = CompleteSlot(controller, 3);
        string shortcutLog = RadialActionCompletionHandler.Handle(
            service,
            controller.ActiveSettings,
            shortcutCompletion);
        RadialMenuCompletion ds4Completion = CompleteSlot(controller, 4);
        string ds4Log = RadialActionCompletionHandler.Handle(
            service,
            controller.ActiveSettings,
            ds4Completion);
        RadialMenuCompletion dpadCompletion = CompleteSlot(controller, 5);
        string dpadLog = RadialActionCompletionHandler.Handle(
            service,
            controller.ActiveSettings,
            dpadCompletion);

        Assert.Equal("[环形菜单] 已执行：Slot 2（键盘 F1）", keyboardLog);
        Assert.Equal("[环形菜单] 已执行：Slot 3（Ctrl+Shift+K）", shortcutLog);
        Assert.Equal("[环形菜单] 已执行：Slot 4（DS4 CROSS）", ds4Log);
        Assert.Equal("[环形菜单] 已执行：Slot 5（DS4 十字键下）", dpadLog);
        Assert.Equal(new[]
        {
            "F1 down",
            "F1 up",
            "LeftControl down",
            "LeftShift down",
            "K down",
            "K up",
            "LeftShift up",
            "LeftControl up"
        }, keyboardOutput.Events);
        Assert.Equal(2, directFactory.Session.SetButtonCalls);
        Assert.Equal(2, directFactory.Session.SetDPadCalls);
        Assert.Equal(0, directFactory.Session.SetTriggerCalls);
        Assert.Equal(4, directFactory.Session.SubmitCalls);
    }

    [Fact]
    public void NoneAndCancelledCompletions_UseExistingSafeMessages()
    {
        var keyboardOutput = new CountingKeyboardOutput();
        using var service = new Ds4Service(
            keyboardOutput: keyboardOutput,
            bindingStore: new MemoryBindingStore());
        RadialMenuSettings settings = RadialMenuSettings.Default;
        using var controller = new RadialMenuController(new FakeOverlay(), settings);

        RadialMenuCompletion noneCompletion = CompleteSlot(controller, 1);
        RadialTriggerSource source = RadialTriggerSource.ForAction("cross");
        controller.OpenAt(new Point(500, 500), source);
        Assert.True(controller.TryCompleteFrom(source, out RadialMenuCompletion cancelledCompletion));

        Assert.Equal("[环形菜单] 已确认：Slot 1（未配置动作）",
            RadialActionCompletionHandler.Handle(
                service,
                controller.ActiveSettings,
                noneCompletion));
        Assert.Equal("[环形菜单] 已取消",
            RadialActionCompletionHandler.Handle(
                service,
                controller.ActiveSettings,
                cancelledCompletion));
        Assert.Empty(keyboardOutput.Events);
    }

    [Fact]
    public void CompletionHandling_KeyboardFailureReturnsFailureLogWithoutThrowing()
    {
        var keyboardOutput = new CountingKeyboardOutput { ThrowOnCall = 1 };
        using var service = new Ds4Service(
            keyboardOutput: keyboardOutput,
            bindingStore: new MemoryBindingStore());
        RadialMenuSettings settings = RadialMenuSettings.Default with
        {
            SlotMappings = RadialMenuSettings.Default.SlotMappings.WithSlot(2,
                new RadialSlotMapping
                {
                    Kind = RadialActionKind.KeyboardKey,
                    Key = KeyboardKey.F1
                })
        };

        string log = RadialActionCompletionHandler.Handle(
            service,
            settings,
            new RadialMenuCompletion(2));

        Assert.Equal("[环形菜单] Slot 2 键盘动作执行失败：failure on call 1", log);
    }

    [Fact]
    public void CompletionHandling_KeyboardModeReturnsDs4FailureLogWithoutOutput()
    {
        var directFactory = new CountingDirectDs4Factory();
        using var service = new Ds4Service(
            directFactory,
            new CountingKeyboardOutput(),
            new MemoryBindingStore());
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        Assert.True(service.Initialize());
        RadialMenuSettings settings = RadialMenuSettings.Default with
        {
            SlotMappings = RadialMenuSettings.Default.SlotMappings.WithSlot(4,
                new RadialSlotMapping
                {
                    Kind = RadialActionKind.Ds4Button,
                    Ds4Button = "cross"
                })
        };

        string log = RadialActionCompletionHandler.Handle(
            service,
            settings,
            new RadialMenuCompletion(4));

        Assert.Equal(
            "[环形菜单] Slot 4 DS4 动作执行失败：当前输出模式不是 Direct DS4。",
            log);
        Assert.Equal(0, directFactory.Session.SetButtonCalls);
        Assert.Equal(0, directFactory.Session.SetDPadCalls);
        Assert.Equal(0, directFactory.Session.SubmitCalls);
    }

    [Fact]
    public void AppliedMappings_SurviveShutdownAndFreshStartupInstances()
    {
        using var temporary = new TemporarySettingsPath();
        RadialMenuSettings settingsA = RadialMenuSettings.Default with
        {
            ScalePercent = 90,
            SelectionDeadZone = 36,
            SlotMappings = RadialSlotMappings.Create(new RadialSlotMapping[]
            {
                RadialSlotMapping.None,
                new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F1 },
                new()
                {
                    Kind = RadialActionKind.KeyboardShortcut,
                    Key = KeyboardKey.K,
                    Ctrl = true,
                    Shift = true
                },
                new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "cross" }
            })
        };

        string jsonAfterApply;
        using (var firstController = new RadialMenuController(
            new FakeOverlay(),
            RadialMenuSettings.Default))
        {
            var firstStore = new RadialMenuSettingsStore(temporary.FilePath);
            Assert.True(RadialMenuSettingsPersistence.TryApplyAndSave(
                firstController,
                firstStore,
                settingsA,
                firstController.ApplySettings,
                out string saveError), saveError);
            Assert.Equal(settingsA, firstController.ActiveSettings);
            jsonAfterApply = File.ReadAllText(temporary.FilePath);
            Assert.Contains("\"slotMappings\"", jsonAfterApply);
            Assert.Contains("\"key\": \"F1\"", jsonAfterApply);
            Assert.Contains("\"ds4Button\": \"cross\"", jsonAfterApply);
        }

        Assert.Equal(jsonAfterApply, File.ReadAllText(temporary.FilePath));

        var secondStore = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettingsLoadResult startupLoad = secondStore.Load();
        using var secondController = new RadialMenuController(
            new FakeOverlay(),
            startupLoad.Settings);

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, startupLoad.Status);
        Assert.Equal(90, secondController.ActiveSettings.ScalePercent);
        Assert.Equal(36, secondController.ActiveSettings.SelectionDeadZone);
        Assert.Equal(KeyboardKey.F1, secondController.ActiveSettings.SlotMappings[1].Key);
        Assert.Equal(RadialActionKind.KeyboardShortcut,
            secondController.ActiveSettings.SlotMappings[2].Kind);
        Assert.True(secondController.ActiveSettings.SlotMappings[2].Ctrl);
        Assert.True(secondController.ActiveSettings.SlotMappings[2].Shift);
        Assert.Equal(KeyboardKey.K, secondController.ActiveSettings.SlotMappings[2].Key);
        Assert.Equal("cross", secondController.ActiveSettings.SlotMappings[3].Ds4Button);
        Assert.Equal("Ctrl+Shift+K", RadialActionResolver.FormatShortcut(
            secondController.ActiveSettings.SlotMappings[2]));
    }

    [Fact]
    public void FreshStartupSettingsForm_PopulatesLoadedMappings()
    {
        using var temporary = new TemporarySettingsPath();
        RadialMenuSettings savedSettings = RadialMenuSettings.Default with
        {
            SlotMappings = RadialSlotMappings.Create(new RadialSlotMapping[]
            {
                RadialSlotMapping.None,
                new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F1 },
                new()
                {
                    Kind = RadialActionKind.KeyboardShortcut,
                    Key = KeyboardKey.K,
                    Ctrl = true,
                    Shift = true
                },
                new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "cross" }
            })
        };
        var savingStore = new RadialMenuSettingsStore(temporary.FilePath);
        Assert.True(savingStore.TrySave(savedSettings, out string saveError), saveError);

        var startupStore = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettingsLoadResult startupLoad = startupStore.Load();
        using var startupController = new RadialMenuController(
            new FakeOverlay(),
            startupLoad.Settings);
        using var settingsForm = new RadialMenuSettingsForm(
            startupController,
            startupStore,
            startupController.ApplySettings,
            _ => { });

        Assert.Equal(RadialActionKind.KeyboardKey,
            FindComboBox(settingsForm, "slot2ActionKind").SelectedItem);
        Assert.Equal(KeyboardKey.F1,
            FindComboBox(settingsForm, "slot2MainKey").SelectedItem);
        Assert.Equal(RadialActionKind.KeyboardShortcut,
            FindComboBox(settingsForm, "slot3ActionKind").SelectedItem);
        ComboBox shortcutKey = FindComboBox(settingsForm, "slot3MainKey");
        Assert.Equal(KeyboardKey.K, shortcutKey.SelectedItem);
        Dictionary<string, bool> modifiers = shortcutKey.Parent!.Controls
            .OfType<CheckBox>()
            .ToDictionary(checkBox => checkBox.Text, checkBox => checkBox.Checked);
        Assert.True(modifiers["Ctrl"]);
        Assert.False(modifiers["Alt"]);
        Assert.True(modifiers["Shift"]);
        Assert.False(modifiers["Win"]);
        Assert.Equal(RadialActionKind.Ds4Button,
            FindComboBox(settingsForm, "slot4ActionKind").SelectedItem);
        Assert.Equal("cross",
            Assert.IsType<RadialDs4ActionMapping>(
                FindComboBox(settingsForm, "slot4Ds4Button").SelectedItem).Id);
        Assert.Equal(
            new[] { "CROSS", "CIRCLE", "SQUARE", "TRIANGLE", "L1", "L3", "R3", "十字键下" },
            FindComboBox(settingsForm, "slot4Ds4Button").Items
                .Cast<RadialDs4ActionMapping>()
                .Select(action => action.DisplayName));
        Assert.Equal(savedSettings, startupController.ActiveSettings);
    }

    private static RadialMenuCompletion CompleteSlot(RadialMenuController controller, int slot)
    {
        var center = new Point(500, 500);
        Point cursor = slot switch
        {
            1 => new Point(500, 450),
            2 => new Point(550, 450),
            3 => new Point(550, 550),
            4 => new Point(500, 550),
            5 => new Point(450, 550),
            6 => new Point(450, 450),
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
        RadialTriggerSource source = RadialTriggerSource.ForAction("cross");
        controller.OpenAt(center, source);
        Assert.True(controller.UpdateSelectionForCursor(cursor));
        Assert.True(controller.TryCompleteFrom(source, out RadialMenuCompletion completion));
        Assert.Equal(slot, completion.SelectedSlot);
        return completion;
    }

    private static ComboBox FindComboBox(Control parent, string name)
    {
        return Assert.IsType<ComboBox>(Assert.Single(parent.Controls.Find(name, searchAllChildren: true)));
    }

    private sealed class FakeOverlay : IRadialMenuOverlay
    {
        public bool IsVisible { get; private set; }

        public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
            IsVisible = true;

        public void Hide() => IsVisible = false;

        public void Dispose() => IsVisible = false;
    }

    private sealed class CountingKeyboardOutput : IKeyboardOutput
    {
        private int _callCount;

        public List<string> Events { get; } = new();
        public int? ThrowOnCall { get; init; }

        public void SetKeyState(KeyboardKey key, bool isPressed)
        {
            int call = ++_callCount;
            Events.Add($"{key} {(isPressed ? "down" : "up")}");
            if (call == ThrowOnCall) throw new InvalidOperationException($"failure on call {call}");
        }
    }

    private sealed class CountingDirectDs4Factory : IDirectDs4Factory
    {
        public CountingDirectDs4Session Session { get; } = new();

        public IDirectDs4Session Create() => Session;
    }

    private sealed class CountingDirectDs4Session : IDirectDs4Session
    {
        public int SetButtonCalls { get; private set; }
        public int SetDPadCalls { get; private set; }
        public int SetTriggerCalls { get; private set; }
        public int SubmitCalls { get; private set; }

        public void SetButton(DualShock4Button button, bool pressed) => SetButtonCalls++;
        public void SetDPadDirection(DualShock4DPadDirection direction) => SetDPadCalls++;
        public void SetTrigger(DualShock4Slider trigger, byte value) => SetTriggerCalls++;
        public void SetLeftStick(byte x, byte y) { }
        public void SubmitReport() => SubmitCalls++;
        public void Dispose() { }
    }

    private sealed class MemoryBindingStore : IKeyboardBindingStore
    {
        public KeyboardBindings Load() => new();
        public void Save(KeyboardBindings bindings) { }
    }

    private sealed class TemporarySettingsPath : IDisposable
    {
        public TemporarySettingsPath()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "LeftPad.Tests",
                Guid.NewGuid().ToString("N"));
            FilePath = Path.Combine(DirectoryPath, "radial-menu-settings.json");
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
