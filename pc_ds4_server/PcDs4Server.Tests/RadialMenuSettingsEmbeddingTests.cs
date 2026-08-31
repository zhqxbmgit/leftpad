using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[CollectionDefinition("Radial settings WinForms geometry", DisableParallelization = true)]
public sealed class RadialSettingsWinFormsGeometryCollection;

[Collection("Radial settings WinForms geometry")]
public sealed class RadialMenuSettingsEmbeddingTests
{
    [Fact]
    public void RefreshPendingCrossProfilePublish_ShowsConfiguredThemeAndEightMappingRows()
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            var overlay = CrossProfileSettingsScenario.PendingOverlay();
            using var controller = new RadialMenuController(overlay, CrossProfileSettingsScenario.Original);
            using var control = new RadialMenuSettingsControl(
                controller, new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings, _ => { });
            Assert.Equal("radial-6", control.ActiveMappingProfileId);
            Assert.Equal(6, control.MappingRowCount);
            RadialMenuSettings candidate = CrossProfileSettingsScenario.Candidate;
            controller.ApplySettings(candidate);

            control.RefreshFromRuntime();

            Assert.Equal("dark-fantasy-radial8-v1", control.SelectedVisualPackId);
            Assert.Equal("radial-8", control.ActiveMappingProfileId);
            Assert.Equal(8, control.MappingRowCount);
            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings readBack));
            Assert.Equal(candidate, readBack.NormalizeMappings());
            Assert.Equal(candidate.GetProfileMappings("radial-6"), readBack.GetProfileMappings("radial-6"));
            Assert.Equal(candidate.GetProfileMappings("radial-8"), readBack.GetProfileMappings("radial-8"));
            Assert.Equal("radial-6", controller.ActiveSettings.MappingProfileId);
            Assert.Equal(new[] { "基础", "动作映射" },
                Find<TabControl>(control, "settingsTabs").TabPages.Cast<TabPage>().Select(page => page.Text));
            Assert.Empty(control.Controls.Find("advancedSettingsPage", searchAllChildren: true));
        });
    }

    [Fact]
    public void MainForm_ExposesEmbeddedSettingsControlAndNoLongerHasPopupEntryPoint()
    {
        PropertyInfo? settingsControl = typeof(MainForm).GetProperty(
            "SettingsControl",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(settingsControl);
        Assert.Equal(typeof(RadialMenuSettingsControl), settingsControl!.PropertyType);
        Assert.Null(typeof(MainForm).GetMethod(
            "OpenRadialMenuSettings",
            BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void SettingsControl_HasOneReusableLifecycleAndNoCloseButton()
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            using var controller = new RadialMenuController(
                new FakeOverlay(),
                RadialMenuSettings.Default);
            using var control = new RadialMenuSettingsControl(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { });

            int initialRefreshCount = control.RefreshCount;
            ComboBox visualPack = Find<ComboBox>(control, "visualPack");
            visualPack.SelectedItem = visualPack.Items
                .Cast<RadialVisualPackCatalogEntry>()
                .Single(pack => pack.Id == "radial-8-minimal-v1");
            Assert.Equal(8, control.MappingRowCount);

            control.RefreshFromRuntime();

            Assert.True(control.IsInitialized);
            Assert.Equal(initialRefreshCount + 1, control.RefreshCount);
            Assert.Equal(RadialVisualPackContract.DefaultVisualPackId, control.SelectedVisualPackId);
            Assert.Equal(LayoutProfileRegistry.Radial8SlotCount, control.MappingRowCount);
            Assert.Throws<InvalidOperationException>(() => control.Initialize(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { }));
            Assert.Empty(control.Controls.Find("closeSettingsButton", searchAllChildren: true));
            Assert.DoesNotContain(
                control.Controls.Find("settingsActionButtons", searchAllChildren: true)
                    .SelectMany(container => container.Controls.OfType<Button>()),
                button => button.Text == "关闭");
        });
    }

    [Fact]
    public void SimplifiedBasicPage_PreservesGroupedWideLayout()
    {
        RunInSta(() =>
        {
            using var control = new RadialMenuSettingsControl();
            TableLayoutPanel basic = Find<TableLayoutPanel>(control, "basicTwoColumnLayout");
            GroupBox radial = Find<GroupBox>(control, "radialMenuSettingsGroup");
            GroupBox interaction = Find<GroupBox>(control, "radialInteractionSettingsGroup");
            GroupBox receiver = Find<GroupBox>(control, "receiverInterfaceSettingsGroup");
            TabControl tabs = Find<TabControl>(control, "settingsTabs");

            control.ReflowBasicLayoutForTesting(control.RequiredWideBasicWidth);

            Assert.Equal(2, basic.ColumnCount);
            Assert.False(control.IsBasicLayoutCompact);
            Assert.Equal("环形菜单", radial.Text);
            Assert.Equal("操作", interaction.Text);
            Assert.Equal("接收器界面", receiver.Text);
            Assert.Equal(2, tabs.TabCount);
            Assert.Equal(["基础", "动作映射"], tabs.TabPages.Cast<TabPage>().Select(page => page.Text));
            Assert.Empty(control.Controls.Find("advancedSettingsPage", searchAllChildren: true));
        });
    }

    [Theory]
    [InlineData("radial-v5", LayoutProfileRegistry.Radial6ProfileId, 6)]
    [InlineData("radial-8-minimal-v1", LayoutProfileRegistry.Radial8ProfileId, 8)]
    [InlineData("dark-fantasy-radial8-v1", LayoutProfileRegistry.Radial8ProfileId, 8)]
    public void SettingsControl_VisualPackSelectionBuildsNumberedProfileRows(
        string visualPackId,
        string expectedProfileId,
        int expectedRows)
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            var overlay = new FakeOverlay();
            using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
            RadialMenuSettings? applied = null;
            LayoutDefinition? selectedLayout = null;
            using var control = new RadialMenuSettingsControl(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                settings =>
                {
                    applied = settings;
                    overlay.ActiveLayoutDefinition = selectedLayout;
                    controller.ApplySettings(settings);
                },
                _ => { });
            ComboBox visualPack = Find<ComboBox>(control, "visualPack");
            RadialVisualPackCatalogEntry selectedPack = visualPack.Items
                .Cast<RadialVisualPackCatalogEntry>()
                .Single(pack => pack.Id == visualPackId);
            selectedLayout = selectedPack.Definition.LayoutDefinition;
            visualPack.SelectedItem = selectedPack;
            control.PerformLayout();

            Assert.Equal(expectedRows, control.MappingRowCount);
            Assert.Equal(expectedProfileId, control.ActiveMappingProfileId);
            Assert.Equal(visualPackId, control.SelectedVisualPackId);
            int splitIndex = RadialMenuSettingsControl.GetMappingSplitIndex(expectedRows);
            TableLayoutPanel leftColumn = Find<TableLayoutPanel>(control, "mappingLeftColumn");
            TableLayoutPanel rightColumn = Find<TableLayoutPanel>(control, "mappingRightColumn");
            Assert.Equal(splitIndex, leftColumn.RowCount);
            Assert.Equal(expectedRows - splitIndex, rightColumn.RowCount);
            Assert.Equal(
                Enumerable.Range(1, expectedRows).Select(slot => $"Slot {slot}"),
                Enumerable.Range(1, expectedRows).Select(slot =>
                    Find<Label>(control, $"slot{slot}Label").Text));
            Assert.All(
                Enumerable.Range(1, splitIndex),
                slot => Assert.Same(leftColumn, Find<Label>(control, $"slot{slot}Label").Parent));
            Assert.All(
                Enumerable.Range(splitIndex + 1, expectedRows - splitIndex),
                slot => Assert.Same(rightColumn, Find<Label>(control, $"slot{slot}Label").Parent));

            Assert.True(control.TryReadSettingsForTesting(out RadialMenuSettings edited));
            Assert.Equal(expectedProfileId, edited.MappingProfileId);
            Assert.True(control.TryApplyAndSave(showDialog: false));
            Assert.NotNull(applied);
            Assert.Equal(expectedProfileId, applied!.MappingProfileId);
            Assert.Equal(expectedProfileId, controller.ActiveSettings.MappingProfileId);
        });
    }

    [Fact]
    public void ApplyAndSave_RoundTripsVisualPackAndPreservesEveryMappingProfile()
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            RadialMenuSettings initial = RadialMenuSettings.Default
                .SetProfileMappings(
                    LayoutProfileRegistry.Radial6ProfileId,
                    RadialSlotMappings.Create(LayoutProfileRegistry.Radial6SlotCount, new RadialSlotMapping[]
                    {
                        new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F1 },
                        RadialSlotMapping.None,
                        RadialSlotMapping.None,
                        RadialSlotMapping.None,
                        RadialSlotMapping.None,
                        RadialSlotMapping.None
                    }))
                .SetProfileMappings(
                    LayoutProfileRegistry.Radial8ProfileId,
                    RadialSlotMappings.Create(LayoutProfileRegistry.Radial8SlotCount, new RadialSlotMapping[]
                    {
                        RadialSlotMapping.None,
                        RadialSlotMapping.None,
                        RadialSlotMapping.None,
                        RadialSlotMapping.None,
                        RadialSlotMapping.None,
                        RadialSlotMapping.None,
                        new() { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.F7 },
                        new() { Kind = RadialActionKind.Ds4Button, Ds4Button = "cross" }
                    }));
            Assert.Equal(KeyboardKey.F7,
                initial.GetProfileMappings(LayoutProfileRegistry.Radial8ProfileId)[6].Key);
            var overlay = new FakeOverlay();
            using var controller = new RadialMenuController(overlay, initial);
            Assert.Equal(KeyboardKey.F7,
                controller.ActiveSettings.GetProfileMappings(LayoutProfileRegistry.Radial8ProfileId)[6].Key);
            var store = new RadialMenuSettingsStore(temporary.FilePath);
            RadialMenuSettings? applied = null;
            using var control = new RadialMenuSettingsControl(
                controller,
                store,
                settings =>
                {
                    applied = settings;
                    overlay.ActiveLayoutDefinition = new RadialVisualPackCatalog()
                        .Discover()
                        .Packs
                        .Single(pack => pack.Id == settings.VisualPackId)
                        .Definition
                        .LayoutDefinition;
                    controller.ApplySettings(settings);
                },
                _ => { });
            ComboBox visualPack = Find<ComboBox>(control, "visualPack");
            visualPack.SelectedItem = visualPack.Items
                .Cast<RadialVisualPackCatalogEntry>()
                .Single(pack => pack.Id == "radial-8-minimal-v1");

            Assert.Equal(
                RadialActionKind.KeyboardKey,
                Find<ComboBox>(control, "slot7ActionKind").SelectedItem);
            Assert.Equal(KeyboardKey.F7, Find<ComboBox>(control, "slot7MainKey").SelectedItem);

            Assert.True(control.TryApplyAndSave(showDialog: false));
            RadialMenuSettings persisted = store.Load().Settings;

            Assert.NotNull(applied);
            Assert.Equal("radial-8-minimal-v1", persisted.VisualPackId);
            Assert.Equal(LayoutProfileRegistry.Radial8ProfileId, persisted.MappingProfileId);
            Assert.Equal(KeyboardKey.F1,
                persisted.GetProfileMappings(LayoutProfileRegistry.Radial6ProfileId)[0].Key);
            Assert.Equal(KeyboardKey.F7,
                persisted.GetProfileMappings(LayoutProfileRegistry.Radial8ProfileId)[6].Key);
            Assert.Equal("cross",
                persisted.GetProfileMappings(LayoutProfileRegistry.Radial8ProfileId)[7].Ds4Button);
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void SavingReceiverScale_KeepsEmbeddedSettingsReachable(int scalePercent)
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            using var controller = new RadialMenuController(
                new FakeOverlay(),
                RadialMenuSettings.Default with { ReceiverUiScalePercent = 100 });
            using var host = new Form
            {
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = ReceiverUiLayoutMetrics.MainClientBaseline
            };
            using var control = new RadialMenuSettingsControl { Dock = DockStyle.Fill };
            host.Controls.Add(control);
            using var scaling = new ReceiverUiScaling(host);
            control.AttachReceiverUiScaling(scaling);
            control.Initialize(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                settings =>
                {
                    scaling.Apply(settings.ReceiverUiScalePercent);
                    controller.ApplySettings(settings);
                },
                _ => { });
            Find<ComboBox>(control, "receiverUiScalePercent").SelectedItem = scalePercent;

            Assert.True(control.TryApplyAndSave(showDialog: false));
            host.PerformLayout();

            Assert.Equal(
                ReceiverUiScaling.Scale(ReceiverUiLayoutMetrics.MainClientBaseline, scalePercent),
                host.ClientSize);
            Assert.Equal(DockStyle.Fill, control.Dock);
            Assert.True(control.Width > 0 && control.Height > 0);
            Assert.True(control.ClientRectangle.Contains(
                Find<TabControl>(control, "settingsTabs").Bounds));
            Assert.True(control.ClientRectangle.Contains(
                Find<FlowLayoutPanel>(control, "settingsActionButtons").Bounds));
            Assert.All(
                Find<TabControl>(control, "settingsTabs").TabPages.Cast<TabPage>(),
                page => Assert.True(page.AutoScroll));
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void EmbeddedPages_DefaultStateHasNoVerticalOverflowAtNormalReviewScales(
        int scalePercent)
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            RadialMenuSettings settings = RadialMenuSettings.Default with
            {
                VisualPackId = "radial-8-minimal-v1",
                MappingProfileId = LayoutProfileRegistry.Radial8ProfileId,
                ReceiverUiScalePercent = 100
            };
            using var controller = new RadialMenuController(new FakeOverlay(), settings);
            using var host = new Form
            {
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = ReceiverUiLayoutMetrics.SettingsEmbeddedViewportBaseline
            };
            using var control = new RadialMenuSettingsControl { Dock = DockStyle.Fill };
            host.Controls.Add(control);
            using var scaling = new ReceiverUiScaling(host);
            control.AttachReceiverUiScaling(scaling);
            control.Initialize(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { });
            scaling.Apply(scalePercent);
            host.CreateControl();
            host.PerformLayout();

            TabControl tabs = Find<TabControl>(control, "settingsTabs");
            tabs.CreateControl();
            foreach (string pageName in new[]
            {
                "basicSettingsPage",
                "mappingSettingsPage"
            })
            {
                TabPage page = Find<TabPage>(control, pageName);
                tabs.SelectedTab = page;
                page.Bounds = tabs.DisplayRectangle;
                page.CreateControl();
                PerformLayoutTree(control);
                Control content = Assert.Single(page.Controls.Cast<Control>());
                int contentBottomWithPadding = content.Bottom + page.Padding.Bottom;
                Assert.True(contentBottomWithPadding <= page.ClientSize.Height,
                    $"{pageName} overflowed vertically at {scalePercent}%. " +
                    $"Host={host.ClientSize}, Control={control.ClientSize}, Tabs={tabs.ClientSize}, " +
                    $"Page={page.ClientSize}, Display={page.DisplayRectangle}, Content={content.Bounds}.");
            }
        });
    }

    [Fact]
    public void ActionBar_RemainsFixedOutsideEveryScrollableTabPage()
    {
        RunInSta(() =>
        {
            using var control = new RadialMenuSettingsControl
            {
                Size = ReceiverUiLayoutMetrics.SettingsEmbeddedViewportBaseline
            };
            control.PerformLayout();
            TabControl tabs = Find<TabControl>(control, "settingsTabs");
            FlowLayoutPanel actions = Find<FlowLayoutPanel>(control, "settingsActionButtons");

            Assert.Same(control, tabs.Parent);
            Assert.Same(control, actions.Parent);
            Assert.Equal(DockStyle.Bottom, actions.Dock);
            Assert.False(tabs.Bounds.IntersectsWith(actions.Bounds));
            Assert.All(tabs.TabPages.Cast<TabPage>(), page => Assert.True(page.AutoScroll));
        });
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(Assert.Single(root.Controls.Find(name, searchAllChildren: true)));

    private static void PerformLayoutTree(Control root)
    {
        foreach (Control child in root.Controls)
            PerformLayoutTree(child);
        root.PerformLayout();
    }

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

    private sealed class FakeOverlay : IRadialMenuOverlay, IRadialLayoutProvider
    {
        public bool IsVisible { get; private set; }
        public LayoutDefinition? ActiveLayoutDefinition { get; set; }
        public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
            IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Dispose() { }
    }

    private sealed class TemporarySettingsPath : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "LeftPad.SettingsEmbedding.Tests",
            Guid.NewGuid().ToString("N"));

        public TemporarySettingsPath()
        {
            Directory.CreateDirectory(_directory);
            FilePath = Path.Combine(_directory, "radial-menu.json");
        }

        public string FilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
