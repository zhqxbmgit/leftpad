using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class ReceiverUiScalingTests
{
    [Fact]
    public void Presets_ExposeOnlyTheSupportedReceiverUiSizes()
    {
        Assert.Equal(new[] { 100, 125, 150, 175, 200 }, ReceiverUiScaling.Presets);
        Assert.All(ReceiverUiScaling.Presets, value =>
            Assert.True(ReceiverUiScaling.IsPreset(value)));
        Assert.False(ReceiverUiScaling.IsPreset(99));
        Assert.False(ReceiverUiScaling.IsPreset(201));
    }

    [Fact]
    public void Scale_AlwaysUsesTheImmutableBaselineInsteadOfThePreviousResult()
    {
        Size baseline = ReceiverUiLayoutMetrics.MainClientBaseline;

        Size at150 = ReceiverUiScaling.Scale(baseline, 150);
        Size thenAt125 = ReceiverUiScaling.Scale(baseline, 125);
        Size at200 = ReceiverUiScaling.Scale(baseline, 200);
        Size backAt100 = ReceiverUiScaling.Scale(baseline, 100);

        Assert.Equal(new Size(3000, 1200), at150);
        Assert.Equal(new Size(2500, 1000), thenAt125);
        Assert.Equal(new Size(4000, 1600), at200);
        Assert.Equal(baseline, backAt100);
        Assert.NotEqual(ReceiverUiScaling.Scale(at150, 200), at200);
    }

    [Theory]
    [InlineData(100, 1670, 700)]
    [InlineData(125, 2088, 875)]
    [InlineData(150, 2505, 1050)]
    [InlineData(175, 2923, 1225)]
    [InlineData(200, 3340, 1400)]
    public void SettingsFormBaseline_ScalesToExpectedClientSize(
        int scalePercent,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.Equal(
            new Size(expectedWidth, expectedHeight),
            ReceiverUiScaling.Scale(
                ReceiverUiLayoutMetrics.SettingsClientBaseline,
                scalePercent));
    }

    [Theory]
    [InlineData(100, 2000, 800)]
    [InlineData(125, 2500, 1000)]
    [InlineData(150, 3000, 1200)]
    [InlineData(175, 3500, 1400)]
    [InlineData(200, 4000, 1600)]
    public void MainFormBaseline_ScalesToTheRoomierClientSize(
        int scalePercent,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.Equal(
            new Size(expectedWidth, expectedHeight),
            ReceiverUiScaling.Scale(
                ReceiverUiLayoutMetrics.MainClientBaseline,
                scalePercent));
    }

    [Theory]
    [InlineData(100, 2000, 800)]
    [InlineData(125, 2500, 1000)]
    [InlineData(150, 3000, 1200)]
    [InlineData(175, 3500, 1400)]
    [InlineData(200, 3840, 1600)]
    public void MainFormDesiredSize_IsClampedToSyntheticWorkingArea(
        int scalePercent,
        int expectedWidth,
        int expectedHeight)
    {
        Size desired = ReceiverUiScaling.Scale(
            ReceiverUiLayoutMetrics.MainClientBaseline,
            scalePercent);

        Size actual = ReceiverUiScaling.ClampClientSizeToWorkingArea(
            desired,
            Size.Empty,
            new Rectangle(0, 0, 3840, 2064));

        Assert.Equal(new Size(expectedWidth, expectedHeight), actual);
        Assert.True(actual.Width <= 3840);
        Assert.True(actual.Height <= 2064);
    }

    [Fact]
    public void WorkingAreaClamp_PreservesSizeAndKeepsEveryEdgeVisible()
    {
        var workingArea = new Rectangle(-1920, 40, 1920, 1040);
        var oversizedAndOffscreen = new Rectangle(-2600, -300, 2400, 1200);

        Rectangle clamped = ReceiverUiScaling.ClampWindowBoundsToWorkingArea(
            oversizedAndOffscreen,
            workingArea);
        Rectangle clampedAgain = ReceiverUiScaling.ClampWindowBoundsToWorkingArea(
            clamped,
            workingArea);

        Assert.Equal(workingArea, clamped);
        Assert.Equal(clamped, clampedAgain);
    }

    [Fact]
    public void MainTopControlColumns_DoNotGeometricallyOverlap()
    {
        Rectangle outputControls = ReceiverUiLayoutMetrics.MainOutputControlsBounds;
        Rectangle mappings = ReceiverUiLayoutMetrics.MainKeyboardMappingsBounds;

        Assert.False(outputControls.IntersectsWith(mappings));
        Assert.Equal(outputControls.Right, mappings.Left);
        Assert.True(outputControls.Width >=
            100 + 8 + ReceiverUiLayoutMetrics.MainOutputModeComboBoxWidth + 10 + 86 + 12);
        Assert.Equal(
            ReceiverUiLayoutMetrics.MainInnerContentWidth,
            mappings.Right);
    }

    [Fact]
    public void OutputModeComboBoxBaselineWidth_FitsEveryOfficialDisplayName()
    {
        using var font = new Font("Microsoft YaHei UI", 9f);
        int longestTextWidth = Enum.GetValues<OutputMode>()
            .Select(MainForm.GetOutputModeDisplayText)
            .Select(text => TextRenderer.MeasureText(text, font).Width)
            .Max();

        Assert.True(
            ReceiverUiLayoutMetrics.MainOutputModeComboBoxWidth >=
            longestTextWidth + SystemInformation.VerticalScrollBarWidth + 24,
            $"Baseline width {ReceiverUiLayoutMetrics.MainOutputModeComboBoxWidth} " +
            $"did not fit measured text width {longestTextWidth} plus ComboBox chrome.");
    }

    [Theory]
    [InlineData("radial-v5", LayoutProfileRegistry.Radial6ProfileId, 6)]
    [InlineData("radial-8-test", LayoutProfileRegistry.Radial8ProfileId, 8)]
    public void SettingsMappingLabels_FollowActiveLayoutSlotCount(
        string visualPackId,
        string mappingProfileId,
        int expectedSlotCount)
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            using RadialVisualPackTestDirectory visualPacks = CreateVisualPackCatalog();
            RadialMenuSettings settings = RadialMenuSettings.Default with
            {
                VisualPackId = visualPackId,
                MappingProfileId = mappingProfileId,
                ReceiverUiScalePercent = 100
            };
            using var controller = new RadialMenuController(new FakeRadialOverlay(), settings);
            using var form = new RadialMenuSettingsForm(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { },
                new RadialVisualPackCatalog(visualPacks.Root));
            form.PerformLayout();

            var visualPack = Assert.IsType<ComboBox>(Assert.Single(
                form.Controls.Find("visualPack", searchAllChildren: true)));
            RadialVisualPackCatalogEntry activePack =
                Assert.IsType<RadialVisualPackCatalogEntry>(visualPack.SelectedItem);
            int activeSlotCount = activePack.Definition.LayoutDefinition.SlotCount;
            var mappingTable = Assert.IsType<TableLayoutPanel>(Assert.Single(
                form.Controls.Find("mappingTable", searchAllChildren: true)));
            var leftColumn = Assert.IsType<TableLayoutPanel>(Assert.Single(
                form.Controls.Find("mappingLeftColumn", searchAllChildren: true)));
            var rightColumn = Assert.IsType<TableLayoutPanel>(Assert.Single(
                form.Controls.Find("mappingRightColumn", searchAllChildren: true)));
            Label[] labels = Enumerable.Range(1, expectedSlotCount)
                .Select(slot => Assert.IsType<Label>(Assert.Single(
                    form.Controls.Find($"slot{slot}Label", searchAllChildren: true))))
                .ToArray();
            int splitIndex = RadialMenuSettingsControl.GetMappingSplitIndex(expectedSlotCount);

            Assert.Equal(expectedSlotCount, activeSlotCount);
            Assert.Equal(ReceiverUiLayoutMetrics.SettingsContentColumnCount, mappingTable.ColumnCount);
            Assert.Equal(splitIndex, leftColumn.RowCount);
            Assert.Equal(expectedSlotCount - splitIndex, rightColumn.RowCount);
            Assert.Equal(expectedSlotCount, labels.Length);
            Assert.Equal(
                Enumerable.Range(1, expectedSlotCount).Select(slot => $"Slot {slot}"),
                labels.Select(label => label.Text));
            Assert.All(labels.Take(splitIndex), label => Assert.Same(leftColumn, label.Parent));
            Assert.All(labels.Skip(splitIndex), label => Assert.Same(rightColumn, label.Parent));
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void SettingsMappingSlotLabels_DoNotClipAtSupportedReviewScales(int scalePercent)
    {
        RunInSta(() =>
        {
            using var temporary = new TemporarySettingsPath();
            using RadialVisualPackTestDirectory visualPacks = CreateVisualPackCatalog();
            RadialMenuSettings settings = RadialMenuSettings.Default with
            {
                VisualPackId = "radial-8-test",
                MappingProfileId = LayoutProfileRegistry.Radial8ProfileId,
                ReceiverUiScalePercent = scalePercent
            };
            using var controller = new RadialMenuController(new FakeRadialOverlay(), settings);
            using var form = new RadialMenuSettingsForm(
                controller,
                new RadialMenuSettingsStore(temporary.FilePath),
                controller.ApplySettings,
                _ => { },
                new RadialVisualPackCatalog(visualPacks.Root));
            form.PerformLayout();

            foreach (int slot in Enumerable.Range(1, 8))
            {
                var label = Assert.IsType<Label>(Assert.Single(
                    form.Controls.Find($"slot{slot}Label", searchAllChildren: true)));
                int measuredWidth = TextRenderer.MeasureText(label.Text, label.Font).Width;
                Assert.True(label.ClientSize.Width >= measuredWidth,
                    $"{label.Text} measured {measuredWidth}px but had {label.ClientSize.Width}px at {scalePercent}%.");
                Assert.True(label.ClientSize.Height >= label.Font.Height,
                    $"{label.Text} height clipped at {scalePercent}%.");
            }
        });
    }

    [Fact]
    public void StatusCards_HaveGapsAndStayInsideTheMainContentWidth()
    {
        IReadOnlyList<Rectangle> cards = ReceiverUiLayoutMetrics.GetStatusCardBounds();

        Assert.Equal(ReceiverUiLayoutMetrics.StatusCardCount, cards.Count);
        for (int index = 0; index < cards.Count; index++)
        {
            Assert.True(cards[index].Width > 160);
            Assert.InRange(cards[index].Left, 0, ReceiverUiLayoutMetrics.MainInnerContentWidth);
            Assert.InRange(cards[index].Right, 0, ReceiverUiLayoutMetrics.MainInnerContentWidth);
            if (index > 0)
            {
                Assert.False(cards[index - 1].IntersectsWith(cards[index]));
                Assert.Equal(
                    ReceiverUiLayoutMetrics.StatusCardGap,
                    cards[index].Left - cards[index - 1].Right);
            }
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void MainSections_StayInsideScaledClientBounds(int scalePercent)
    {
        Size clientSize = ReceiverUiScaling.Scale(
            ReceiverUiLayoutMetrics.MainClientBaseline,
            scalePercent);
        var clientBounds = new Rectangle(Point.Empty, clientSize);

        foreach (Rectangle baseline in ReceiverUiLayoutMetrics.MainSectionBounds)
        {
            Rectangle scaled = Scale(baseline, scalePercent);
            Assert.True(clientBounds.Contains(scaled), $"Section {scaled} exceeded {clientBounds}.");
        }
    }

    [Fact]
    public void SettingsBottomButtons_HaveExplicitGapsAndStayInsideTheBaseline()
    {
        IReadOnlyList<Rectangle> buttons = ReceiverUiLayoutMetrics.GetSettingsButtonBounds();

        Assert.Equal(4, buttons.Count);
        for (int index = 0; index < buttons.Count; index++)
        {
            Assert.True(buttons[index].Right < ReceiverUiLayoutMetrics.SettingsClientBaseline.Width);
            if (index > 0)
            {
                Assert.False(buttons[index - 1].IntersectsWith(buttons[index]));
                Assert.Equal(
                    ReceiverUiLayoutMetrics.SettingsButtonGap,
                    buttons[index].Left - buttons[index - 1].Right);
            }
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void SettingsBasicControls_RemainReachableAtSupportedReviewScales(int scalePercent)
    {
        int contentHeight = ReceiverUiScaling.Scale(
            ReceiverUiLayoutMetrics.SettingsBasicContentHeight,
            scalePercent);
        int reachableHeight = ReceiverUiScaling.Scale(
            ReceiverUiLayoutMetrics.SettingsBasicReachableHeight,
            scalePercent);

        Assert.True(contentHeight <= reachableHeight,
            $"Basic content height {contentHeight} exceeded reachable height {reachableHeight}.");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void WideSettingsDefaultContent_FitsTheEmbeddedViewportWithoutVerticalOverflow(
        int scalePercent)
    {
        int reachableHeight = ReceiverUiScaling.Scale(
            ReceiverUiLayoutMetrics.SettingsPageReachableHeight,
            scalePercent);

        Assert.True(
            ReceiverUiScaling.Scale(
                ReceiverUiLayoutMetrics.SettingsBasicContentHeight,
                scalePercent) <= reachableHeight);
        Assert.True(
            ReceiverUiScaling.Scale(
                ReceiverUiLayoutMetrics.GetSettingsMappingDefaultContentHeight(
                    LayoutProfileRegistry.Radial8SlotCount),
                scalePercent) <= reachableHeight);
        Assert.True(
            ReceiverUiScaling.Scale(
                ReceiverUiLayoutMetrics.SettingsAdvancedContentHeight,
                scalePercent) <= reachableHeight);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void SettingsForm_ImportantControlsAndButtonsRemainReachable(
        int scalePercent)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var controller = new RadialMenuController(
                    new FakeRadialOverlay(),
                    RadialMenuSettings.Default with
                    {
                        ReceiverUiScalePercent = scalePercent
                    });
                string settingsPath = Path.Combine(
                    Path.GetTempPath(),
                    "LeftPad.ReceiverUiLayout.Tests",
                    Guid.NewGuid().ToString("N"),
                    "settings.json");
                using var form = new RadialMenuSettingsForm(
                    controller,
                    new RadialMenuSettingsStore(settingsPath),
                    controller.ApplySettings,
                    _ => { });
                form.PerformLayout();

                Size nonClientSize = new(
                    form.Width - form.ClientSize.Width,
                    form.Height - form.ClientSize.Height);
                Size expectedSize = ReceiverUiScaling.ClampClientSizeToWorkingArea(
                    ReceiverUiScaling.Scale(
                        ReceiverUiLayoutMetrics.SettingsClientBaseline,
                        scalePercent),
                    nonClientSize,
                    Screen.FromRectangle(form.Bounds).WorkingArea);
                Assert.Equal(expectedSize, form.ClientSize);
                var actions = Assert.IsType<FlowLayoutPanel>(Assert.Single(
                    form.Controls.Find("settingsActionButtons", searchAllChildren: true)));
                actions.PerformLayout();
                Button[] buttons = actions.Controls.OfType<Button>().ToArray();
                Assert.Equal(4, buttons.Length);
                Assert.DoesNotContain(buttons, button => button.Text == "关闭");
                Assert.All(buttons, button =>
                {
                    Assert.True(button.Width > 0);
                    Assert.True(button.Height > 0);
                    Assert.True(actions.ClientRectangle.Contains(button.Bounds));
                });
                for (int index = 1; index < buttons.Length; index++)
                    Assert.False(buttons[index - 1].Bounds.IntersectsWith(buttons[index].Bounds));

                var receiverScale = Assert.IsType<ComboBox>(Assert.Single(
                    form.Controls.Find("receiverUiScalePercent", searchAllChildren: true)));
                Assert.True(receiverScale.Width > 0);
                Assert.True(receiverScale.Height > 0);
                Assert.True(receiverScale.Parent!.ClientRectangle.Contains(receiverScale.Bounds));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Apply_RebuildsFormAndControlsFromBaselineAcrossScaleChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new Form
                {
                    AutoScaleMode = AutoScaleMode.None,
                    ClientSize = new Size(100, 80),
                    Font = new Font("Microsoft YaHei UI", 10f)
                };
                var leftPanel = new Panel
                {
                    Dock = DockStyle.Left,
                    Width = 20,
                    Padding = new Padding(2)
                };
                var button = new Button
                {
                    Location = new Point(4, 6),
                    Size = new Size(20, 10),
                    Margin = new Padding(3)
                };
                leftPanel.Controls.Add(button);
                form.Controls.Add(leftPanel);

                var scaling = new ReceiverUiScaling(form);
                scaling.Apply(150);
                Assert.Equal(new Size(150, 120), form.ClientSize);
                Assert.Equal(30, leftPanel.Width);
                Assert.Equal(new Padding(3), leftPanel.Padding);
                Assert.Equal(new Rectangle(6, 9, 30, 15), button.Bounds);
                Assert.Equal(15f, form.Font.Size, precision: 2);

                scaling.Apply(125);
                Assert.Equal(new Size(125, 100), form.ClientSize);
                Assert.Equal(25, leftPanel.Width);
                Assert.Equal(new Rectangle(5, 8, 25, 13), button.Bounds);
                Assert.Equal(12.5f, form.Font.Size, precision: 2);

                scaling.Apply(200);
                Assert.Equal(new Size(200, 160), form.ClientSize);
                Assert.Equal(40, leftPanel.Width);
                Assert.Equal(new Rectangle(8, 12, 40, 20), button.Bounds);
                Assert.Equal(20f, form.Font.Size, precision: 2);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null) throw failure;
    }

    [Fact]
    public void ModernStatusCard_ConstructionAllowsEarlyLayoutBeforeIndicatorCreation()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var card = new ModernStatusCard("status", "ready");
                card.PerformLayout();
                card.SetStatusColor(Color.Green);
                Assert.Equal(new Size(160, 80), card.Size);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null) throw failure;
    }

    [Theory]
    [InlineData(150, 192, 192, 150)]
    [InlineData(150, 192, 144, 113)]
    [InlineData(150, 192, 96, 75)]
    [InlineData(150, 96, 192, 300)]
    public void EffectiveScale_CombinesMonitorChangeWithThePersistedUserPreferenceOnce(
        int userScalePercent,
        int referenceDpi,
        int currentDpi,
        int expectedEffectivePercent)
    {
        Assert.Equal(
            expectedEffectivePercent,
            ReceiverUiScaling.GetEffectiveScalePercent(
                userScalePercent,
                referenceDpi,
                currentDpi));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(175)]
    [InlineData(200)]
    public void Settings_AcceptEveryReceiverUiPreset(int scalePercent)
    {
        Assert.True((RadialMenuSettings.Default with
        {
            ReceiverUiScalePercent = scalePercent
        }).TryValidate(out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(110)]
    [InlineData(201)]
    public void Settings_RejectNonPresetReceiverUiValues(int scalePercent)
    {
        Assert.False((RadialMenuSettings.Default with
        {
            ReceiverUiScalePercent = scalePercent
        }).TryValidate(out string error));
        Assert.Contains("接收器界面缩放", error);
    }

    [Fact]
    public void LegacyJsonWithoutReceiverUiScale_Uses150AndPreservesRadialSettings()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "scalePercent": 90,
              "selectionDeadZone": 36,
              "highlightAlpha": 120
            }
            """);

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(150, result.Settings.ReceiverUiScalePercent);
        Assert.Equal(90, result.Settings.ScalePercent);
        Assert.Equal(36, result.Settings.SelectionDeadZone);
        Assert.Equal(120, result.Settings.HighlightAlpha);
    }

    [Fact]
    public void InvalidStoredReceiverUiScale_NormalizesOnlyThatPreference()
    {
        using var temporary = new TemporarySettingsPath();
        Directory.CreateDirectory(temporary.DirectoryPath);
        File.WriteAllText(temporary.FilePath, """
            {
              "receiverUiScalePercent": 333,
              "scalePercent": 125,
              "selectionDeadZone": 44
            }
            """);

        RadialMenuSettingsLoadResult result =
            new RadialMenuSettingsStore(temporary.FilePath).Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(150, result.Settings.ReceiverUiScalePercent);
        Assert.Equal(125, result.Settings.ScalePercent);
        Assert.Equal(44, result.Settings.SelectionDeadZone);
    }

    [Fact]
    public void JsonStore_RoundTripsReceiverUiScaleIndependently()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings expected = RadialMenuSettings.Default with
        {
            ReceiverUiScalePercent = 200,
            ScalePercent = 90,
            SelectionDeadZone = 40
        };

        Assert.True(store.TrySave(expected, out string saveError), saveError);
        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(200, result.Settings.ReceiverUiScalePercent);
        Assert.Equal(90, result.Settings.ScalePercent);
        Assert.Equal(40, result.Settings.SelectionDeadZone);
        Assert.Contains("\"receiverUiScalePercent\": 200", File.ReadAllText(temporary.FilePath));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void ReceiverUiScale_DoesNotChangeRadialRenderMetrics(int receiverUiScalePercent)
    {
        RadialMenuRenderMetrics baseline = RadialMenuSettings.Default.CreateRenderMetrics();
        RadialMenuRenderMetrics actual = (RadialMenuSettings.Default with
        {
            ReceiverUiScalePercent = receiverUiScalePercent
        }).CreateRenderMetrics();

        Assert.Equal(baseline, actual);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void ReceiverUiScale_DoesNotChangePhysicalRadialBacking(int receiverUiScalePercent)
    {
        RadialMenuSettings settings = RadialMenuSettings.Default with
        {
            ReceiverUiScalePercent = receiverUiScalePercent
        };

        Assert.Equal(
            560,
            RadialDpiScaling.ToPhysicalPixels(
                settings.CreateRenderMetrics().CanvasSize,
                dpi: 192));
    }

    private static Rectangle Scale(Rectangle baseline, int scalePercent) => new(
        ReceiverUiScaling.Scale(baseline.X, scalePercent),
        ReceiverUiScaling.Scale(baseline.Y, scalePercent),
        ReceiverUiScaling.Scale(baseline.Width, scalePercent),
        ReceiverUiScaling.Scale(baseline.Height, scalePercent));

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static RadialVisualPackTestDirectory CreateVisualPackCatalog()
    {
        var temporary = new RadialVisualPackTestDirectory();
        temporary.AddPack("default", "radial-v5", "Tactical HUD V5");
        temporary.AddRadial8Pack();
        return temporary;
    }

    private sealed class FakeRadialOverlay : IRadialMenuOverlay
    {
        public bool IsVisible { get; private set; }

        public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
            IsVisible = true;

        public void Hide() => IsVisible = false;

        public void Dispose() => IsVisible = false;
    }

    private sealed class TemporarySettingsPath : IDisposable
    {
        public TemporarySettingsPath()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "LeftPad.ReceiverUiScaling.Tests",
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
