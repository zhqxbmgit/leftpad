using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class RadialSettingsSimplificationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeFontEditAndSave_PreservesRevisionHiddenFieldsAndEveryMappingProfile(
        int? revisionValue)
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            RadialMenuSettings initial = SettingsWithHiddenValues(revisionValue);
            using var controller = new RadialMenuController(new FakeOverlay(), initial);
            var store = new RadialMenuSettingsStore(temporary.FilePath);
            using var control = new RadialMenuSettingsControl(
                controller,
                store,
                controller.ApplySettings,
                _ => { });

            Find<NumericUpDown>(control, "fontSize").Value = 16.5m;

            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings candidate));
            AssertOnlyFontChanged(initial, candidate, 16.5f);
            Assert.True(control.TryApplyAndSave(showDialog: false));
            RadialMenuSettings persisted = store.Load().Settings;
            AssertOnlyFontChanged(initial, persisted, 16.5f);
        });
    }

    [Theory]
    [InlineData(nameof(RadialMenuSettings.SettingsSemanticRevision))]
    [InlineData(nameof(RadialMenuSettings.HighlightAlpha))]
    [InlineData(nameof(RadialMenuSettings.FillAlpha))]
    [InlineData(nameof(RadialMenuSettings.BorderAlpha))]
    [InlineData(nameof(RadialMenuSettings.TextAlpha))]
    [InlineData(nameof(RadialMenuSettings.BaseCanvasSize))]
    [InlineData(nameof(RadialMenuSettings.HubRadius))]
    [InlineData(nameof(RadialMenuSettings.PetalInnerRadius))]
    [InlineData(nameof(RadialMenuSettings.PetalOuterRadius))]
    [InlineData(nameof(RadialMenuSettings.TextRadius))]
    [InlineData(nameof(RadialMenuSettings.PetalGapDegrees))]
    [InlineData(nameof(RadialMenuSettings.SelectionPollIntervalMs))]
    [InlineData(nameof(RadialMenuSettings.MappingProfileId))]
    [InlineData(nameof(RadialMenuSettings.MappingsByProfile))]
    public void NativeFontCandidate_PreservesEachHiddenProperty(string propertyName)
    {
        RunInSta(() =>
        {
            RadialMenuSettings initial = SettingsWithHiddenValues(2);
            using var temporary = new TemporarySettingsPath();
            using var controller = new RadialMenuController(new FakeOverlay(), initial);
            using var control = new RadialMenuSettingsControl(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { });
            Find<NumericUpDown>(control, "fontSize").Value = 16.5m;

            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings candidate));
            System.Reflection.PropertyInfo property = typeof(RadialMenuSettings)
                .GetProperty(propertyName)!;
            Assert.Equal(property.GetValue(initial), property.GetValue(candidate));
        });
    }

    [Fact]
    public void NativeFontEdit_PreservesAllHiddenFieldsAndMappingProfiles()
    {
        RunInSta(() =>
        {
            RadialMenuSettings initial = SettingsWithHiddenValues(2);
            using var temporary = new TemporarySettingsPath();
            using var controller = new RadialMenuController(new FakeOverlay(), initial);
            using var control = new RadialMenuSettingsControl(controller,
                new RadialMenuSettingsStore(temporary.FilePath), controller.ApplySettings, _ => { });
            Find<NumericUpDown>(control, "fontSize").Value = 16.5m;

            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings draft));
            AssertOnlyFontChanged(initial, draft, 16.5f);
            Assert.True(control.TryApplyAndSave(showDialog: false));
            AssertOnlyFontChanged(initial, controller.ConfiguredSettings, 16.5f);
        });
    }

    [Fact]
    public void ThemeSwitch_UsesAuthoritativeProfileAndPreservesBothProfileMappings()
    {
        RunInSta(() =>
        {
            RadialMenuSettings initial = SettingsWithHiddenValues(1);
            using var temporary = new TemporarySettingsPath();
            using var controller = new RadialMenuController(new FakeOverlay(), initial);
            using var control = new RadialMenuSettingsControl(controller,
                new RadialMenuSettingsStore(temporary.FilePath), controller.ApplySettings, _ => { });
            ComboBox theme = Find<ComboBox>(control, "visualPack");
            theme.SelectedItem = theme.Items.Cast<RadialVisualPackCatalogEntry>()
                .Single(pack => pack.Id == "radial-8-minimal-v1");

            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings draft));
            Assert.Equal("radial-8", draft.MappingProfileId);
            Assert.Equal(initial.GetProfileMappings("radial-6"), draft.GetProfileMappings("radial-6"));
            Assert.Equal(initial.GetProfileMappings("radial-8"), draft.GetProfileMappings("radial-8"));
        });
    }

    [Fact]
    public void RestoreDefaults_ResetsHiddenFieldsAndProducesNoneMappingsForKnownProfiles()
    {
        RunInSta(() =>
        {
            RadialMenuSettings initial = SettingsWithHiddenValues(2);
            using var temporary = new TemporarySettingsPath();
            using var controller = new RadialMenuController(new FakeOverlay(), initial);
            using var control = new RadialMenuSettingsControl(controller,
                new RadialMenuSettingsStore(temporary.FilePath), controller.ApplySettings, _ => { });

            NativeReceiverTestThread.Click(control, "restoreDefaultSettingsButton");

            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings draft));
            Assert.Equal(RadialMenuSettings.Default.SetProfileMappings("radial-8",
                RadialSlotMappings.Create(8)).NormalizeMappings(), draft);
            Assert.Equal(RadialMenuSettings.Default.FillAlpha, draft.FillAlpha);
            Assert.Equal(RadialMenuSettings.Default.BorderAlpha, draft.BorderAlpha);
            Assert.Equal(RadialMenuSettings.Default.TextRadius, draft.TextRadius);
            Assert.Equal(RadialMenuSettings.Default.SelectionPollIntervalMs, draft.SelectionPollIntervalMs);
            Assert.All(draft.GetProfileMappings("radial-6"),
                mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
            Assert.All(draft.GetProfileMappings("radial-8"),
                mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
            Assert.Equal(initial, controller.ConfiguredSettings);
            Assert.False(File.Exists(temporary.FilePath));
        });
    }

    [Fact]
    public void LegacyJsonLoad_MigratesSlotMappingsWithoutWritingBack()
    {
        using var temporary = new TemporarySettingsPath();
        const string legacyJson = """
            {
              "scalePercent": 111,
              "hubRadius": 50,
              "petalInnerRadius": 60,
              "petalOuterRadius": 103,
              "textRadius": 90,
              "borderAlpha": 42,
              "slotMappings": [
                { "kind": "keyboardKey", "key": "F1" }
              ]
            }
            """;
        byte[] original = Encoding.UTF8.GetBytes(legacyJson);
        File.WriteAllBytes(temporary.FilePath, original);
        var store = new RadialMenuSettingsStore(temporary.FilePath);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("radial-v5", result.Settings.VisualPackId);
        Assert.Equal("radial-6", result.Settings.MappingProfileId);
        Assert.Equal(KeyboardKey.F1, result.Settings.GetProfileMappings("radial-6")[0].Key);
        Assert.Equal(original, File.ReadAllBytes(temporary.FilePath));
    }

    [Fact]
    public void NativeUi_ContainsOnlyBasicAndMappingTabsWithRequiredGroups()
    {
        RunInSta(() =>
        {
            using var control = new RadialMenuSettingsControl();
            TabControl tabs = Find<TabControl>(control, "settingsTabs");

            Assert.Equal(["基础", "动作映射"],
                tabs.TabPages.Cast<TabPage>().Select(page => page.Text));
            Assert.Equal("环形菜单", Find<GroupBox>(control, "radialMenuSettingsGroup").Text);
            Assert.Equal("操作", Find<GroupBox>(control, "radialInteractionSettingsGroup").Text);
            Assert.Equal("接收器界面", Find<GroupBox>(control, "receiverInterfaceSettingsGroup").Text);
            Assert.Single(control.Controls.Find("fontSize", searchAllChildren: true));
            Assert.Empty(control.Controls.Find("advancedSettingsPage", searchAllChildren: true));
        });
    }

    [Fact]
    public void NativeBasicLayout_ReflowsByAvailableWidthWithoutReplacingControlsOrSettings()
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            RadialMenuSettings settings = RadialMenuSettings.Default with
            {
                VisualPackId = "dark-fantasy-radial8-v1",
                ScalePercent = 117,
                FontSize = 19.5f,
                DoubleTapWindowMs = 321,
                SelectionDeadZone = 37
            };
            using var controller = new RadialMenuController(new FakeOverlay(), settings);
            using var form = new RadialMenuSettingsForm(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { });
            form.Show();
            Application.DoEvents();

            RadialMenuSettingsControl control = Find<RadialMenuSettingsControl>(
                form,
                "radialMenuSettingsControl");
            TableLayoutPanel layout = Find<TableLayoutPanel>(control, "basicTwoColumnLayout");
            TableLayoutPanel right = Find<TableLayoutPanel>(control, "basicRightSections");
            GroupBox radial = Find<GroupBox>(control, "radialMenuSettingsGroup");
            GroupBox interaction = Find<GroupBox>(control, "radialInteractionSettingsGroup");
            GroupBox receiver = Find<GroupBox>(control, "receiverInterfaceSettingsGroup");
            ComboBox theme = Find<ComboBox>(control, "visualPack");
            NumericUpDown scale = Find<NumericUpDown>(control, "scalePercent");
            NumericUpDown fontSize = Find<NumericUpDown>(control, "fontSize");
            NumericUpDown doubleTap = Find<NumericUpDown>(control, "doubleTapWindowMs");
            NumericUpDown deadZone = Find<NumericUpDown>(control, "selectionDeadZone");
            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings before));

            int requiredWideWidth = control.RequiredWideBasicWidth;
            Assert.True(requiredWideWidth > 1);
            control.ReflowBasicLayoutForTesting(requiredWideWidth - 1);

            Assert.True(control.IsBasicLayoutCompact);
            Assert.Same(layout, radial.Parent);
            Assert.Same(layout, interaction.Parent);
            Assert.Same(layout, receiver.Parent);
            Assert.Equal(0, layout.GetRow(radial));
            Assert.Equal(1, layout.GetRow(interaction));
            Assert.Equal(2, layout.GetRow(receiver));
            layout.PerformLayout();
            foreach (object item in theme.Items)
            {
                string displayName = theme.GetItemText(item) ?? string.Empty;
                int requiredWidth = TextRenderer.MeasureText(
                    displayName,
                    theme.Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width +
                    SystemInformation.VerticalScrollBarWidth +
                    12;
                Assert.True(
                    theme.ClientSize.Width >= requiredWidth,
                    $"Compact Theme ComboBox needs {requiredWidth}px for '{displayName}' " +
                    $"but has {theme.ClientSize.Width}px.");
            }

            control.ReflowBasicLayoutForTesting(requiredWideWidth);

            Assert.False(control.IsBasicLayoutCompact);
            Assert.Same(layout, radial.Parent);
            Assert.Same(layout, right.Parent);
            Assert.Same(right, interaction.Parent);
            Assert.Same(right, receiver.Parent);
            Assert.Equal(0, layout.GetColumn(radial));
            Assert.Equal(1, layout.GetColumn(right));
            Assert.Equal(0, right.GetRow(interaction));
            Assert.Equal(1, right.GetRow(receiver));
            Assert.Same(theme, Find<ComboBox>(control, "visualPack"));
            Assert.Same(scale, Find<NumericUpDown>(control, "scalePercent"));
            Assert.Same(fontSize, Find<NumericUpDown>(control, "fontSize"));
            Assert.Same(doubleTap, Find<NumericUpDown>(control, "doubleTapWindowMs"));
            Assert.Same(deadZone, Find<NumericUpDown>(control, "selectionDeadZone"));
            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings after));
            Assert.Equal(before, after);
            form.Close();
        });
    }

    [Fact]
    public void CompactMode_DoesNotRequireScrollbars()
    {
        CompactLayoutSnapshot snapshot = CaptureCompactLayout();

        Assert.True(snapshot.IsCompact);
        Assert.False(snapshot.VerticalScrollVisible);
        Assert.False(snapshot.HorizontalScrollVisible);
    }

    [Fact]
    public void CompactMode_AllGroupsFitViewport()
    {
        CompactLayoutSnapshot snapshot = CaptureCompactLayout();

        Assert.True(snapshot.UsableViewport.Contains(snapshot.RadialBounds));
        Assert.True(snapshot.UsableViewport.Contains(snapshot.InteractionBounds));
        Assert.True(snapshot.UsableViewport.Contains(snapshot.ReceiverBounds));
    }

    [Fact]
    public void CompactMode_ReceiverBottomBorderHasMargin()
    {
        CompactLayoutSnapshot snapshot = CaptureCompactLayout();

        Assert.True(
            snapshot.BottomMargin >= 4,
            $"Compact receiver group needs at least 4px below its bottom border but has " +
            $"{snapshot.BottomMargin}px.");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void NativeThemeComboBox_FitsEveryProductionThemeAtSupportedScale(int scalePercent)
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            RadialMenuSettings settings = RadialMenuSettings.Default with
            {
                ReceiverUiScalePercent = scalePercent
            };
            using var controller = new RadialMenuController(new FakeOverlay(), settings);
            using var form = new RadialMenuSettingsForm(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { });
            form.Show();
            Application.DoEvents();
            ComboBox themes = Find<ComboBox>(form, "visualPack");
            string[] displayNames = themes.Items.Cast<object>()
                .Select(item => themes.GetItemText(item) ?? string.Empty)
                .ToArray();

            Assert.Contains("Dark Fantasy Radial 8", displayNames);
            Assert.Contains("Tactical HUD V5", displayNames);
            Assert.Contains("Radial 8 Minimal V1", displayNames);
            foreach (string displayName in displayNames)
            {
                int requiredWidth = TextRenderer.MeasureText(displayName, themes.Font).Width +
                    SystemInformation.VerticalScrollBarWidth +
                    12;
                Assert.True(
                    themes.ClientSize.Width >= requiredWidth,
                    $"'{displayName}' requires {requiredWidth}px but the closed Theme ComboBox " +
                    $"has {themes.ClientSize.Width}px at {scalePercent}% Receiver UI scale.");
            }
            form.Close();
        });
    }

    [Fact]
    public void RemovedNativeFields_HaveNoUserVisibleControlsWhilePollDefaultRemainsFrozen()
    {
        RunInSta(() =>
        {
            using var control = new RadialMenuSettingsControl();
            foreach (string name in new[]
                     {
                         "highlightAlpha", "fillAlpha", "borderAlpha", "textAlpha",
                         "baseCanvasSize", "hubRadius", "petalInnerRadius", "petalOuterRadius",
                         "textRadius", "petalGapDegrees", "selectionPollIntervalMs"
                     })
            {
                Assert.Empty(control.Controls.Find(name, searchAllChildren: true));
            }
        });
        Assert.Equal(16, RadialMenuSettings.Default.SelectionPollIntervalMs);
    }

    private static RadialMenuSettings SettingsWithHiddenValues(int? revisionValue)
    {
        RadialSettingsSemanticRevision? revision = revisionValue.HasValue
            ? (RadialSettingsSemanticRevision)revisionValue.Value
            : null;
        return (RadialMenuSettings.Default with
        {
            VisualPackId = "radial-v5",
            SettingsSemanticRevision = revision,
            ReceiverUiScalePercent = 175,
            ScalePercent = 111,
            BaseCanvasSize = 360,
            HubRadius = 50,
            PetalInnerRadius = 60,
            PetalOuterRadius = 150,
            TextRadius = 90,
            PetalGapDegrees = 5.5f,
            FontSize = 14.5f,
            FillAlpha = 177,
            BorderAlpha = 42,
            TextAlpha = 211,
            DoubleTapWindowMs = 210,
            SelectionDeadZone = 35,
            HighlightAlpha = 123,
            SelectionPollIntervalMs = 24,
            MappingProfileId = "radial-6"
        })
        .SetProfileMappings("radial-6", RadialSlotMappings.Create(6,
        [
            new RadialSlotMapping { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F1 }
        ]))
        .SetProfileMappings("radial-8", RadialSlotMappings.Create(8,
        [
            RadialSlotMapping.None,
            RadialSlotMapping.None,
            RadialSlotMapping.None,
            RadialSlotMapping.None,
            RadialSlotMapping.None,
            RadialSlotMapping.None,
            new RadialSlotMapping { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F7 },
            new RadialSlotMapping { Kind = RadialActionKind.Ds4Button, Ds4Button = "cross" }
        ]));
    }

    private static void AssertOnlyFontChanged(
        RadialMenuSettings initial,
        RadialMenuSettings actual,
        float expectedFontSize)
    {
        Assert.Equal(initial with { FontSize = expectedFontSize }, actual);
        Assert.Equal(initial.SettingsSemanticRevision, actual.SettingsSemanticRevision);
        Assert.Equal(initial.GetProfileMappings("radial-6"), actual.GetProfileMappings("radial-6"));
        Assert.Equal(initial.GetProfileMappings("radial-8"), actual.GetProfileMappings("radial-8"));
    }

    private static CompactLayoutSnapshot CaptureCompactLayout()
    {
        CompactLayoutSnapshot? snapshot = null;
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            RadialMenuSettings settings = RadialMenuSettings.Default with
            {
                ReceiverUiScalePercent = 100
            };
            using var controller = new RadialMenuController(new FakeOverlay(), settings);
            using var form = new RadialMenuSettingsForm(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { });
            form.Show();
            Application.DoEvents();

            RadialMenuSettingsControl control = Find<RadialMenuSettingsControl>(
                form,
                "radialMenuSettingsControl");
            TabPage page = Find<TabPage>(control, "basicSettingsPage");
            TableLayoutPanel layout = Find<TableLayoutPanel>(control, "basicTwoColumnLayout");
            GroupBox radial = Find<GroupBox>(control, "radialMenuSettingsGroup");
            GroupBox interaction = Find<GroupBox>(control, "radialInteractionSettingsGroup");
            GroupBox receiver = Find<GroupBox>(control, "receiverInterfaceSettingsGroup");

            control.ReflowBasicLayoutForTesting(control.RequiredWideBasicWidth - 1);
            layout.PerformLayout();
            page.PerformLayout();

            Rectangle usableViewport = new(
                page.Padding.Left,
                page.Padding.Top,
                page.ClientSize.Width - page.Padding.Horizontal,
                page.ClientSize.Height - page.Padding.Vertical);
            Rectangle radialBounds = BoundsWithin(radial, page);
            Rectangle interactionBounds = BoundsWithin(interaction, page);
            Rectangle receiverBounds = BoundsWithin(receiver, page);
            snapshot = new CompactLayoutSnapshot(
                control.IsBasicLayoutCompact,
                page.VerticalScroll.Visible,
                page.HorizontalScroll.Visible,
                usableViewport,
                radialBounds,
                interactionBounds,
                receiverBounds,
                usableViewport.Bottom - receiverBounds.Bottom);
            form.Close();
        });
        return Assert.IsType<CompactLayoutSnapshot>(snapshot);
    }

    private static Rectangle BoundsWithin(Control control, Control ancestor)
    {
        Point location = ancestor.PointToClient(control.Parent!.PointToScreen(control.Location));
        return new Rectangle(location, control.Size);
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(Assert.Single(root.Controls.Find(name, searchAllChildren: true)));

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
        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FakeOverlay : IRadialMenuOverlay
    {
        public bool IsVisible { get; private set; }
        public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
            IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Dispose() { }
    }

    private sealed record CompactLayoutSnapshot(
        bool IsCompact,
        bool VerticalScrollVisible,
        bool HorizontalScrollVisible,
        Rectangle UsableViewport,
        Rectangle RadialBounds,
        Rectangle InteractionBounds,
        Rectangle ReceiverBounds,
        int BottomMargin);

    private sealed class TemporarySettingsPath : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "LeftPad.RadialSettingsSimplification.Tests",
            Guid.NewGuid().ToString("N"));

        public TemporarySettingsPath()
        {
            Directory.CreateDirectory(_directory);
            FilePath = Path.Combine(_directory, "radial-menu-settings.json");
        }

        public string FilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
