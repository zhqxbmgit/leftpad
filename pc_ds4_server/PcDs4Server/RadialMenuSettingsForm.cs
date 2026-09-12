namespace PcDs4Server;

public sealed class RadialMenuSettingsControl : UserControl
{
    private const float RadialLabelColumnPercent = 38;
    private const float DefaultLabelColumnPercent = 46;
    private const int GroupHorizontalPadding = 12;
    private const int GroupTopPadding = 18;
    private const int GroupBottomPadding = 12;
    private const int CompactGroupBottomPadding = 6;

    private RadialMenuController? _controller;
    private RadialMenuSettingsStore? _store;
    private Action<RadialMenuSettings>? _applySettings;
    private Action<string>? _log;
    private RadialVisualPackCatalog? _visualPackCatalogProvider;
    private RadialVisualPackCatalogSnapshot? _visualPackCatalog;
    private ReceiverUiScaling? _hostUiScaling;

    private readonly ComboBox _visualPack = VisualPackDropDown();
    private readonly ComboBox _receiverUiScale = ReceiverUiScaleDropDown();
    private readonly StartupToggleControl _startupToggle = new();
    private readonly NumericUpDown _scale = Editor(
        "scalePercent",
        RadialMenuSettings.MinimumScalePercent,
        RadialMenuSettings.MaximumScalePercent);
    private readonly NumericUpDown _fontSize = Editor(
        "fontSize",
        6,
        48,
        decimalPlaces: 1,
        increment: 0.5m);
    private readonly NumericUpDown _doubleTapWindow = Editor(
        "doubleTapWindowMs",
        RadialMenuSettings.MinimumDoubleTapWindowMs,
        RadialMenuSettings.MaximumDoubleTapWindowMs);
    private readonly NumericUpDown _selectionDeadZone = Editor(
        "selectionDeadZone",
        RadialMenuSettings.MinimumSelectionDeadZone,
        RadialMenuSettings.MaximumSelectionDeadZone);
    private TableLayoutPanel? _basicLayout;
    private TableLayoutPanel? _basicRightSections;
    private GroupBox? _radialSettingsGroup;
    private GroupBox? _interactionSettingsGroup;
    private GroupBox? _receiverSettingsGroup;
    private bool? _basicLayoutCompact;
    private bool _reflowingBasicLayout;
    private readonly TableLayoutPanel _mappingTable = new()
    {
        Name = "mappingTable",
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = ReceiverUiLayoutMetrics.GetSettingsMappingDefaultContentHeight(
            LayoutProfileRegistry.Radial8SlotCount),
        ColumnCount = ReceiverUiLayoutMetrics.SettingsContentColumnCount,
        RowCount = 1,
        Padding = new Padding(6, 4, 6, 4)
    };
    private readonly TableLayoutPanel _mappingLeftColumn = MappingColumnTable(
        "mappingLeftColumn");
    private readonly TableLayoutPanel _mappingRightColumn = MappingColumnTable(
        "mappingRightColumn");
    private MappingEditor[] _mappingEditors = Array.Empty<MappingEditor>();
    private readonly Label _validationStatus = new()
    {
        Dock = DockStyle.Bottom,
        Height = ReceiverUiLayoutMetrics.SettingsValidationAreaHeight,
        Padding = new Padding(16, 6, 16, 0),
        ForeColor = ThemeColors.Error,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private bool _populating;
    private string _populatedVisualPackId = RadialVisualPackContract.DefaultVisualPackId;
    private string _mappingProfileId = LayoutProfileRegistry.Radial6ProfileId;
    private RadialMenuSettings _workingSettings = RadialMenuSettings.Default;
    private bool _initialized;
    private int _refreshCount;

    public RadialMenuSettingsControl()
    {
        Name = "radialMenuSettingsControl";
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.TextMain;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.None;

        var tabs = new TabControl
        {
            Name = "settingsTabs",
            Dock = DockStyle.Fill,
            Padding = new Point(18, 8)
        };
        tabs.TabPages.Add(CreateBasicPage());
        tabs.TabPages.Add(CreateMappingsPage());

        var buttons = new FlowLayoutPanel
        {
            Name = "settingsActionButtons",
            Dock = DockStyle.Bottom,
            Height = ReceiverUiLayoutMetrics.SettingsButtonAreaHeight,
            Padding = new Padding(14, 14, 14, 12),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true
        };
        buttons.Controls.Add(ActionButton("previewSettingsButton", "预览", Preview));
        buttons.Controls.Add(ActionButton(
            "hideSettingsPreviewButton",
            "隐藏预览",
            () => _controller?.ClosePreview()));
        buttons.Controls.Add(ActionButton("applySettingsButton", "应用并保存", ApplyAndSave));
        buttons.Controls.Add(ActionButton(
            "restoreDefaultSettingsButton",
            "恢复默认",
            () => Populate(RadialMenuSettings.Default)));

        Controls.Add(tabs);
        Controls.Add(_validationStatus);
        Controls.Add(buttons);
        SubscribeToLivePreviewChanges();
    }

    public RadialMenuSettingsControl(
        RadialMenuController controller,
        RadialMenuSettingsStore store,
        Action<RadialMenuSettings> applySettings,
        Action<string> log,
        RadialVisualPackCatalog? visualPackCatalog = null) : this()
    {
        Initialize(controller, store, applySettings, log, visualPackCatalog);
    }

    internal bool IsInitialized => _initialized;
    internal int RefreshCount => _refreshCount;
    internal int MappingRowCount => _mappingEditors.Length;
    internal string ActiveMappingProfileId => _mappingProfileId;
    internal string? SelectedVisualPackId =>
        (_visualPack.SelectedItem as RadialVisualPackCatalogEntry)?.Id;
    internal bool IsBasicLayoutCompact => _basicLayoutCompact == true;
    internal int RequiredWideBasicWidth => GetRequiredWideBasicWidth();

    internal void ReflowBasicLayoutForTesting(int availableWidth) =>
        ReflowBasicLayout(availableWidth);

    public void Initialize(
        RadialMenuController controller,
        RadialMenuSettingsStore store,
        Action<RadialMenuSettings> applySettings,
        Action<string> log,
        RadialVisualPackCatalog? visualPackCatalog = null)
    {
        if (_initialized)
            throw new InvalidOperationException("The radial settings control is already initialized.");
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _applySettings = applySettings ?? throw new ArgumentNullException(nameof(applySettings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _visualPackCatalogProvider = visualPackCatalog ?? new RadialVisualPackCatalog();
        _initialized = true;
        RefreshFromRuntime();
    }

    internal void AttachReceiverUiScaling(ReceiverUiScaling receiverUiScaling)
    {
        _hostUiScaling = receiverUiScaling ?? throw new ArgumentNullException(nameof(receiverUiScaling));
    }

    internal void InitializeStartup(StartupRegistration registration) =>
        _startupToggle.Initialize(registration);

    public void RefreshFromRuntime()
    {
        EnsureInitialized();
        RefreshVisualPackCatalog();
        Populate(_controller!.ConfiguredSettings);
        _refreshCount++;
    }

    public void Deactivate()
    {
        _controller?.ClosePreview();
    }

    private void RefreshVisualPackCatalog()
    {
        _visualPackCatalog = _visualPackCatalogProvider!.Discover();
        _populating = true;
        try
        {
            _visualPack.BeginUpdate();
            _visualPack.Items.Clear();
            _visualPack.Items.AddRange(_visualPackCatalog.Packs.Cast<object>().ToArray());
        }
        finally
        {
            _visualPack.EndUpdate();
            _populating = false;
        }

        foreach (RadialVisualPackCatalogIssue issue in _visualPackCatalog.Issues)
        {
            _log!(
                $"[视觉主题] 忽略 '{issue.PackId ?? issue.DirectoryPath}'：" +
                issue.Message);
        }

        ReflowBasicLayout();
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("The radial settings control has not been initialized.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _controller?.ClosePreview();
        base.Dispose(disposing);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        ReflowBasicLayout();
    }

    private TabPage CreateBasicPage()
    {
        var page = Page("基础");
        page.Name = "basicSettingsPage";
        page.Controls.Add(CreateBasicLayout());
        page.ClientSizeChanged += (_, _) => ReflowBasicLayout();
        return page;
    }

    private TabPage CreateMappingsPage()
    {
        var page = Page("动作映射");
        page.Name = "mappingSettingsPage";
        _mappingTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _mappingTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _mappingTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        int halfGutter = ReceiverUiLayoutMetrics.SettingsContentColumnGutter / 2;
        _mappingLeftColumn.Margin = new Padding(0, 0, halfGutter, 0);
        _mappingRightColumn.Margin = new Padding(halfGutter, 0, 0, 0);
        _mappingTable.Controls.Add(_mappingLeftColumn, 0, 0);
        _mappingTable.Controls.Add(_mappingRightColumn, 1, 0);
        page.Controls.Add(_mappingTable);
        return page;
    }

    private void Preview()
    {
        EnsureInitialized();
        if (!TryReadSettings(out RadialMenuSettings settings, showDialog: true)) return;
        _controller!.PreviewAt(Cursor.Position, settings);
    }

    private void ApplyAndSave() => TryApplyAndSave(showDialog: true);

    internal bool TryApplyAndSave(bool showDialog)
    {
        EnsureInitialized();
        if (!TryReadSettings(out RadialMenuSettings settings, showDialog)) return false;

        if (RadialMenuSettingsPersistence.TryApplyAndSave(
            _controller!,
            _store!,
            settings,
            _applySettings!,
            out string error))
        {
            _log!("[环形菜单] 设置已保存");
            if (showDialog)
            {
                MessageBox.Show(this, "环形菜单设置已应用并保存。",
                    "环形菜单", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }

        _log!($"[环形菜单] 设置保存失败：{error}");
        if (showDialog)
        {
            MessageBox.Show(this, CreateSaveFailureMessage(error),
                "环形菜单", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return false;
    }

    internal static string CreateSaveFailureMessage(string error) =>
        $"保存失败，设置未应用。请查看日志。\n\n{error}";

    internal bool TryReadSettingsForTesting(out RadialMenuSettings settings) =>
        TryReadSettings(out settings, showDialog: false);

    private bool TryReadSettings(out RadialMenuSettings settings, bool showDialog)
    {
        settings = _workingSettings with
        {
            VisualPackId = _visualPack.SelectedItem is RadialVisualPackCatalogEntry pack
                ? pack.Id
                : _populatedVisualPackId,
            ReceiverUiScalePercent = _receiverUiScale.SelectedItem is int receiverUiScale
                ? receiverUiScale
                : ReceiverUiScaling.DefaultScalePercent,
            ScalePercent = Decimal.ToInt32(_scale.Value),
            FontSize = (float)_fontSize.Value,
            DoubleTapWindowMs = Decimal.ToInt32(_doubleTapWindow.Value),
            SelectionDeadZone = Decimal.ToInt32(_selectionDeadZone.Value),
            MappingProfileId = _mappingProfileId
        };
        settings = settings.SetProfileMappings(_mappingProfileId, ReadMappings());

        if (settings.TryValidate(out string error))
        {
            _validationStatus.Text = string.Empty;
            return true;
        }

        _validationStatus.Text = error;
        if (showDialog)
        {
            MessageBox.Show(this, error, "环形菜单设置无效",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return false;
    }

    private void Populate(RadialMenuSettings settings)
    {
        _populating = true;
        try
        {
            _workingSettings = settings.NormalizeMappings();
            _populatedVisualPackId = settings.VisualPackId;
            _visualPack.SelectedItem = _visualPackCatalog!.ResolveSelection(
                settings.VisualPackId);
            _receiverUiScale.SelectedItem = ReceiverUiScaling.Normalize(
                settings.ReceiverUiScalePercent);
            _scale.Value = settings.ScalePercent;
            _fontSize.Value = (decimal)settings.FontSize;
            _doubleTapWindow.Value = settings.DoubleTapWindowMs;
            _selectionDeadZone.Value = settings.SelectionDeadZone;
            BindMappingsForSelectedVisualPack();
        }
        finally
        {
            _populating = false;
        }

        RefreshLivePreview();
    }

    private void SubscribeToLivePreviewChanges()
    {
        foreach (NumericUpDown editor in new[] { _scale, _fontSize })
        {
            editor.ValueChanged += (_, _) => RefreshLivePreview();
        }
        _visualPack.SelectedValueChanged += (_, _) =>
        {
            SwitchMappingProfile();
            RefreshLivePreview();
        };
    }

    private void RefreshLivePreview()
    {
        if (_populating || _controller?.IsPreviewActive != true) return;
        if (!TryReadSettings(out RadialMenuSettings settings, showDialog: false)) return;
        _controller.UpdatePreview(settings);
    }

    private RadialSlotMappings ReadMappings()
    {
        RadialSlotMapping[] mappings = _mappingEditors.Select(editor => editor.Read()).ToArray();
        return new RadialSlotMappings(mappings);
    }

    internal static int GetMappingRowCount(LayoutDefinition layoutDefinition)
    {
        ArgumentNullException.ThrowIfNull(layoutDefinition);
        return layoutDefinition.SlotCount;
    }

    private void SwitchMappingProfile()
    {
        if (_populating) return;
        if (_mappingEditors.Length > 0)
            _workingSettings = _workingSettings.SetProfileMappings(_mappingProfileId, ReadMappings());
        BindMappingsForSelectedVisualPack();
    }

    private void BindMappingsForSelectedVisualPack()
    {
        LayoutDefinition? layout =
            (_visualPack.SelectedItem as RadialVisualPackCatalogEntry)?.Definition.LayoutDefinition;
        if (layout != null)
        {
            _mappingProfileId = layout.ProfileId;
            RebuildMappingEditors(GetMappingRowCount(layout));
        }
        else
        {
            _mappingProfileId = LayoutProfileRegistry.Radial6ProfileId;
            RebuildMappingEditors(LayoutProfileRegistry.Radial6SlotCount);
        }

        RadialSlotMappings mappings = _workingSettings.GetProfileMappings(_mappingProfileId);
        for (int index = 0; index < _mappingEditors.Length; index++)
            _mappingEditors[index].Populate(mappings[index]);
    }

    private void RebuildMappingEditors(int slotCount)
    {
        _mappingTable.SuspendLayout();
        try
        {
            _mappingEditors = Enumerable.Range(1, slotCount)
                .Select(slot => new MappingEditor(slot))
                .ToArray();
            int splitIndex = GetMappingSplitIndex(slotCount);
            RebuildMappingColumn(_mappingLeftColumn, startIndex: 0, count: splitIndex);
            RebuildMappingColumn(
                _mappingRightColumn,
                startIndex: splitIndex,
                count: slotCount - splitIndex);
        }
        finally
        {
            _mappingTable.ResumeLayout(performLayout: true);
        }

        _hostUiScaling?.Apply(
            _hostUiScaling.CurrentScalePercent,
            preserveCenter: false);
    }

    internal static int GetMappingSplitIndex(int slotCount) =>
        ReceiverUiLayoutMetrics.GetSettingsMappingRowsPerColumn(slotCount);

    private void RebuildMappingColumn(
        TableLayoutPanel column,
        int startIndex,
        int count)
    {
        column.SuspendLayout();
        try
        {
            column.Controls.Clear();
            column.RowStyles.Clear();
            column.RowCount = count;
            for (int row = 0; row < count; row++)
            {
                int index = startIndex + row;
                MappingEditor editor = _mappingEditors[index];
                var slotLabel = new Label
                {
                    Name = $"slot{index + 1}Label",
                    Text = $"Slot {index + 1}",
                    Dock = DockStyle.Fill,
                    MinimumSize = new Size(0, ReceiverUiLayoutMetrics.SettingsMappingRowHeight),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = ThemeColors.TextSecondary
                };
                _hostUiScaling?.CaptureBaseline(slotLabel, inheritFormFont: true);
                _hostUiScaling?.CaptureBaseline(editor.KindEditor, inheritFormFont: true);
                _hostUiScaling?.CaptureBaseline(editor.DetailPanel, inheritFormFont: true);
                column.RowStyles.Add(new RowStyle(
                    SizeType.Absolute,
                    ReceiverUiLayoutMetrics.SettingsMappingRowHeight));
                column.Controls.Add(slotLabel, 0, row);
                column.Controls.Add(editor.KindEditor, 1, row);
                column.Controls.Add(editor.DetailPanel, 2, row);
            }
        }
        finally
        {
            column.ResumeLayout(performLayout: true);
        }
    }

    private static TabPage Page(string text) => new(text)
    {
        BackColor = ThemeColors.ContentPanel,
        ForeColor = ThemeColors.TextMain,
        Padding = new Padding(20, 6, 20, 6),
        AutoScroll = true
    };

    private Control CreateBasicLayout()
    {
        _basicLayout = new TableLayoutPanel
        {
            Name = "basicTwoColumnLayout",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 4, 12, 4)
        };
        _radialSettingsGroup = EditorGroup(
            "radialMenuSettingsGroup",
            "环形菜单",
            labelColumnPercent: RadialLabelColumnPercent,
            rows:
            [
                ("视觉主题", _visualPack),
                ("菜单大小 (%)", _scale),
                ("字体大小 (px)", _fontSize)
            ]);

        _basicRightSections = new TableLayoutPanel
        {
            Name = "basicRightSections",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        _interactionSettingsGroup = EditorGroup(
            "radialInteractionSettingsGroup",
            "操作",
            rows:
            [
                ("双击判定时间 (ms)", _doubleTapWindow),
                ("中心死区 (px)", _selectionDeadZone)
            ]);
        _receiverSettingsGroup = EditorGroup(
            "receiverInterfaceSettingsGroup",
            "接收器界面",
            rows:
            [
                ("接收器界面缩放", _receiverUiScale),
                ("开机启动", _startupToggle)
            ]);

        ApplyBasicLayout(compact: false);
        return _basicLayout;
    }

    private void ReflowBasicLayout()
    {
        if (_basicLayout?.Parent is not Control parent) return;
        int availableWidth = parent.ClientSize.Width - parent.Padding.Horizontal;
        if (availableWidth > 0) ReflowBasicLayout(availableWidth);
    }

    private void ReflowBasicLayout(int availableWidth)
    {
        if (_reflowingBasicLayout || _basicLayout == null) return;
        bool compact = availableWidth < GetRequiredWideBasicWidth();
        if (_basicLayoutCompact == compact) return;

        _reflowingBasicLayout = true;
        try
        {
            ApplyBasicLayout(compact);
        }
        finally
        {
            _reflowingBasicLayout = false;
        }
    }

    private int GetRequiredWideBasicWidth()
    {
        if (_basicLayout == null ||
            _radialSettingsGroup == null ||
            _interactionSettingsGroup == null ||
            _receiverSettingsGroup == null)
        {
            return 0;
        }

        int radialWidth = GetRequiredGroupWidth(
            _radialSettingsGroup,
            RadialLabelColumnPercent);
        int rightWidth = Math.Max(
            GetRequiredGroupWidth(_interactionSettingsGroup, DefaultLabelColumnPercent),
            GetRequiredGroupWidth(_receiverSettingsGroup, DefaultLabelColumnPercent));
        int gutter = ScaleLogical(ReceiverUiLayoutMetrics.SettingsContentColumnGutter);
        return _basicLayout.Padding.Horizontal + radialWidth + gutter + rightWidth;
    }

    private static int GetRequiredGroupWidth(GroupBox group, float labelColumnPercent)
    {
        TableLayoutPanel fields = group.Controls.OfType<TableLayoutPanel>().Single();
        int labelWidth = 0;
        int editorWidth = 0;
        for (int row = 0; row < fields.RowCount; row++)
        {
            if (fields.GetControlFromPosition(0, row) is Label label)
            {
                labelWidth = Math.Max(
                    labelWidth,
                    MeasureSingleLine(label.Text, label.Font) + label.Margin.Horizontal);
            }

            if (fields.GetControlFromPosition(1, row) is Control editor)
            {
                int requiredEditorWidth = editor.GetPreferredSize(Size.Empty).Width;
                if (editor is ComboBox combo)
                {
                    IEnumerable<string> displayNames = combo.Items.Cast<object>()
                        .Select(item => combo.GetItemText(item) ?? string.Empty)
                        .Append(combo.Text);
                    int textWidth = displayNames
                        .Select(name => MeasureSingleLine(name, combo.Font))
                        .DefaultIfEmpty(0)
                        .Max();
                    requiredEditorWidth = Math.Max(
                        requiredEditorWidth,
                        textWidth + SystemInformation.VerticalScrollBarWidth + 12);
                }

                editorWidth = Math.Max(
                    editorWidth,
                    requiredEditorWidth + editor.Margin.Horizontal);
            }
        }

        double labelShare = labelColumnPercent / 100d;
        double editorShare = 1d - labelShare;
        int requiredFieldsWidth = (int)Math.Ceiling(Math.Max(
            labelWidth / labelShare,
            editorWidth / editorShare));
        return group.Padding.Horizontal + requiredFieldsWidth + 2;
    }

    private static int MeasureSingleLine(string text, Font font) =>
        TextRenderer.MeasureText(
            text,
            font,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

    private void ApplyBasicLayout(bool compact)
    {
        if (_basicLayout == null ||
            _basicRightSections == null ||
            _radialSettingsGroup == null ||
            _interactionSettingsGroup == null ||
            _receiverSettingsGroup == null)
        {
            return;
        }

        int halfGutter = ScaleLogical(ReceiverUiLayoutMetrics.SettingsContentColumnGutter / 2);
        int compactGutter = 0;
        int defaultMargin = ScaleLogical(3);
        _basicLayout.SuspendLayout();
        _basicRightSections.SuspendLayout();
        try
        {
            _basicLayout.Controls.Clear();
            _basicLayout.ColumnStyles.Clear();
            _basicLayout.RowStyles.Clear();
            _basicRightSections.Controls.Clear();
            _basicRightSections.ColumnStyles.Clear();
            _basicRightSections.RowStyles.Clear();

            if (compact)
            {
                _basicLayout.Padding = new Padding(ScaleLogical(12), 0, ScaleLogical(12), 0);
                Padding compactGroupPadding = new(
                    ScaleLogical(GroupHorizontalPadding),
                    ScaleLogical(GroupTopPadding),
                    ScaleLogical(GroupHorizontalPadding),
                    ScaleLogical(CompactGroupBottomPadding));
                _radialSettingsGroup.Padding = compactGroupPadding;
                _interactionSettingsGroup.Padding = compactGroupPadding;
                _receiverSettingsGroup.Padding = compactGroupPadding;
                _basicLayout.ColumnCount = 1;
                _basicLayout.RowCount = 3;
                _basicLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (int row = 0; row < 3; row++)
                    _basicLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                _radialSettingsGroup.Margin = new Padding(0, 0, 0, compactGutter);
                _interactionSettingsGroup.Margin = new Padding(0, 0, 0, compactGutter);
                _receiverSettingsGroup.Margin = Padding.Empty;
                _basicLayout.Controls.Add(_radialSettingsGroup, 0, 0);
                _basicLayout.Controls.Add(_interactionSettingsGroup, 0, 1);
                _basicLayout.Controls.Add(_receiverSettingsGroup, 0, 2);
            }
            else
            {
                _basicLayout.Padding = new Padding(
                    ScaleLogical(12),
                    ScaleLogical(4),
                    ScaleLogical(12),
                    ScaleLogical(4));
                Padding wideGroupPadding = new(
                    ScaleLogical(GroupHorizontalPadding),
                    ScaleLogical(GroupTopPadding),
                    ScaleLogical(GroupHorizontalPadding),
                    ScaleLogical(GroupBottomPadding));
                _radialSettingsGroup.Padding = wideGroupPadding;
                _interactionSettingsGroup.Padding = wideGroupPadding;
                _receiverSettingsGroup.Padding = wideGroupPadding;
                _basicLayout.ColumnCount = ReceiverUiLayoutMetrics.SettingsContentColumnCount;
                _basicLayout.RowCount = 1;
                _basicLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                _basicLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                _basicLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                _basicRightSections.ColumnCount = 1;
                _basicRightSections.RowCount = 2;
                _basicRightSections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                _basicRightSections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _basicRightSections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _basicRightSections.Margin = new Padding(halfGutter, 0, 0, 0);
                _radialSettingsGroup.Margin = new Padding(0, 0, halfGutter, 0);
                _interactionSettingsGroup.Margin = new Padding(defaultMargin);
                _receiverSettingsGroup.Margin = new Padding(
                    defaultMargin,
                    halfGutter,
                    defaultMargin,
                    defaultMargin);
                _basicRightSections.Controls.Add(_interactionSettingsGroup, 0, 0);
                _basicRightSections.Controls.Add(_receiverSettingsGroup, 0, 1);
                _basicLayout.Controls.Add(_radialSettingsGroup, 0, 0);
                _basicLayout.Controls.Add(_basicRightSections, 1, 0);
            }

            _basicLayoutCompact = compact;
        }
        finally
        {
            _basicRightSections.ResumeLayout(performLayout: true);
            _basicLayout.ResumeLayout(performLayout: true);
        }
    }

    private int ScaleLogical(int baseline) =>
        _hostUiScaling?.ScaleLogical(baseline) ?? baseline;

    private static GroupBox EditorGroup(
        string name,
        string text,
        float labelColumnPercent = DefaultLabelColumnPercent,
        params (string Label, Control Editor)[] rows)
    {
        var group = new GroupBox
        {
            Name = name,
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(
                GroupHorizontalPadding,
                GroupTopPadding,
                GroupHorizontalPadding,
                GroupBottomPadding),
            ForeColor = ThemeColors.TextMain
        };
        TableLayoutPanel fields = EditorTable(
            $"{name}Fields",
            labelColumnPercent,
            rows);
        fields.Dock = DockStyle.Top;
        group.Controls.Add(fields);
        return group;
    }

    private static TableLayoutPanel EditorTable(
        string name,
        float labelColumnPercent,
        params (string Label, Control Editor)[] rows)
    {
        var table = new TableLayoutPanel
        {
            Name = name,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = rows.Length
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, labelColumnPercent));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - labelColumnPercent));

        for (int row = 0; row < rows.Length; row++)
        {
            int rowHeight = Math.Max(
                ReceiverUiLayoutMetrics.SettingsEditorRowHeight,
                rows[row].Editor.MinimumSize.Height + 14);
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
            table.Controls.Add(new Label
            {
                Text = rows[row].Label,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 16, 4),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ThemeColors.TextSecondary
            }, 0, row);
            rows[row].Editor.Dock = DockStyle.Fill;
            rows[row].Editor.Margin = new Padding(12, 7, 4, 7);
            table.Controls.Add(rows[row].Editor, 1, row);
        }

        return table;
    }

    private static TableLayoutPanel MappingColumnTable(string name)
    {
        var table = new TableLayoutPanel
        {
            Name = name,
            Dock = DockStyle.Fill,
            AutoSize = false,
            MinimumSize = new Size(
                0,
                ReceiverUiLayoutMetrics.SettingsMappingRowHeight *
                ReceiverUiLayoutMetrics.GetSettingsMappingRowsPerColumn(
                    LayoutProfileRegistry.Radial8SlotCount)),
            ColumnCount = 3
        };
        table.ColumnStyles.Add(new ColumnStyle(
            SizeType.Absolute,
            ReceiverUiLayoutMetrics.SettingsSlotLabelColumnWidth));
        table.ColumnStyles.Add(new ColumnStyle(
            SizeType.Absolute,
            ReceiverUiLayoutMetrics.SettingsMappingKindColumnWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static NumericUpDown Editor(
        string name,
        decimal minimum,
        decimal maximum,
        int decimalPlaces = 0,
        decimal increment = 1m) => new()
    {
        Name = name,
        Minimum = minimum,
        Maximum = maximum,
        DecimalPlaces = decimalPlaces,
        Increment = increment,
        TextAlign = HorizontalAlignment.Right,
        BackColor = ThemeColors.ControlDark,
        ForeColor = ThemeColors.TextMain,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static ComboBox VisualPackDropDown() => new()
    {
        Name = "visualPack",
        DropDownStyle = ComboBoxStyle.DropDownList,
        DisplayMember = nameof(RadialVisualPackCatalogEntry.Name),
        ValueMember = nameof(RadialVisualPackCatalogEntry.Id),
        BackColor = ThemeColors.ControlDark,
        ForeColor = ThemeColors.TextMain,
        FormattingEnabled = true
    };

    private static ComboBox ReceiverUiScaleDropDown()
    {
        var editor = new ComboBox
        {
            Name = "receiverUiScalePercent",
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ThemeColors.ControlDark,
            ForeColor = ThemeColors.TextMain,
            FormattingEnabled = true
        };
        editor.Items.AddRange(ReceiverUiScaling.Presets.Cast<object>().ToArray());
        editor.Format += (_, e) =>
        {
            if (e.ListItem is int scalePercent) e.Value = $"{scalePercent}%";
        };
        return editor;
    }

    private static Button ActionButton(string name, string text, Action action)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            AutoSize = true,
            Height = 32,
            MinimumSize = new Size(
                ReceiverUiLayoutMetrics.GetSettingsButtonMinimumWidth(text),
                36),
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(0, 0, ReceiverUiLayoutMetrics.SettingsButtonGap, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeColors.ControlDark,
            ForeColor = ThemeColors.TextMain
        };
        button.FlatAppearance.BorderColor = ThemeColors.BorderPurple;
        button.Click += (_, _) => action();
        return button;
    }

    private sealed class MappingEditor
    {
        private readonly Label _keyLabel = DetailLabel("主键");
        private readonly ComboBox _keyEditor = DropDown(KeyboardKeyCatalog.MainKeys.ToArray(), 90);
        private readonly CheckBox _ctrl = Modifier("Ctrl");
        private readonly CheckBox _alt = Modifier("Alt");
        private readonly CheckBox _shift = Modifier("Shift");
        private readonly CheckBox _win = Modifier("Win");
        private readonly ComboBox _ds4Editor = DropDown(RadialDs4ActionCatalog.Actions, 105);

        public MappingEditor(int slot)
        {
            KindEditor = DropDown(Enum.GetValues<RadialActionKind>(), 128);
            KindEditor.Name = $"slot{slot}ActionKind";
            KindEditor.Format += (_, e) =>
            {
                if (e.ListItem is RadialActionKind kind) e.Value = KindText(kind);
            };
            KindEditor.SelectedValueChanged += (_, _) => UpdateVisibility();

            _keyEditor.Name = $"slot{slot}MainKey";
            _ds4Editor.Name = $"slot{slot}Ds4Button";
            _ds4Editor.Format += (_, e) =>
            {
                if (e.ListItem is RadialDs4ActionMapping action) e.Value = action.DisplayName;
            };

            DetailPanel = new TableLayoutPanel
            {
                Name = $"slot{slot}DetailLayout",
                Dock = DockStyle.Fill,
                AutoSize = false,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                ColumnCount = 6,
                RowCount = 1,
                Padding = new Padding(4, 8, 0, 0),
                Margin = Padding.Empty
            };
            for (int column = 0; column < 5; column++)
                DetailPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            DetailPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            DetailPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _ctrl.Name = $"slot{slot}Ctrl";
            _alt.Name = $"slot{slot}Alt";
            _shift.Name = $"slot{slot}Shift";
            _win.Name = $"slot{slot}Win";
            _keyLabel.Name = $"slot{slot}MainKeyLabel";

            DetailPanel.Controls.Add(_ctrl, 0, 0);
            DetailPanel.Controls.Add(_alt, 1, 0);
            DetailPanel.Controls.Add(_shift, 2, 0);
            DetailPanel.Controls.Add(_win, 3, 0);
            DetailPanel.Controls.Add(_keyLabel, 4, 0);

            var valueHost = new Panel
            {
                Name = $"slot{slot}MappingValueHost",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            valueHost.Controls.Add(_keyEditor);
            valueHost.Controls.Add(_ds4Editor);
            DetailPanel.Controls.Add(valueHost, 5, 0);

            _keyEditor.Dock = DockStyle.Fill;
            _keyEditor.MinimumSize = new Size(90, 0);
            _keyEditor.Margin = new Padding(0, 2, 0, 2);
            _ds4Editor.Dock = DockStyle.Fill;
            _ds4Editor.MinimumSize = new Size(105, 0);
            _ds4Editor.Margin = new Padding(0, 2, 0, 2);
            KindEditor.Dock = DockStyle.Fill;
            KindEditor.Margin = new Padding(3, 12, 8, 8);
            UpdateVisibility();
        }

        public ComboBox KindEditor { get; }
        public TableLayoutPanel DetailPanel { get; }

        public RadialSlotMapping Read()
        {
            RadialActionKind kind = KindEditor.SelectedItem is RadialActionKind selected
                ? selected
                : RadialActionKind.None;
            KeyboardKey? key = _keyEditor.SelectedItem is KeyboardKey selectedKey
                ? selectedKey
                : null;
            return kind switch
            {
                RadialActionKind.KeyboardKey => new RadialSlotMapping
                {
                    Kind = kind,
                    Key = key
                },
                RadialActionKind.KeyboardShortcut => new RadialSlotMapping
                {
                    Kind = kind,
                    Key = key,
                    Ctrl = _ctrl.Checked,
                    Alt = _alt.Checked,
                    Shift = _shift.Checked,
                    Win = _win.Checked
                },
                RadialActionKind.Ds4Button => new RadialSlotMapping
                {
                    Kind = kind,
                    Ds4Button = (_ds4Editor.SelectedItem as RadialDs4ActionMapping)?.Id
                },
                _ => RadialSlotMapping.None
            };
        }

        public void Populate(RadialSlotMapping mapping)
        {
            _keyEditor.SelectedItem = mapping.Key is KeyboardKey key &&
                KeyboardKeyCatalog.IsMainKey(key)
                ? key
                : KeyboardKeyCatalog.MainKeys[0];
            _ctrl.Checked = mapping.Ctrl;
            _alt.Checked = mapping.Alt;
            _shift.Checked = mapping.Shift;
            _win.Checked = mapping.Win;
            _ds4Editor.SelectedItem = RadialDs4ActionCatalog.TryGet(
                mapping.Ds4Button,
                out RadialDs4ActionMapping ds4Mapping)
                ? ds4Mapping
                : RadialDs4ActionCatalog.Actions[0];
            KindEditor.SelectedItem = mapping.Kind;
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            RadialActionKind kind = KindEditor.SelectedItem is RadialActionKind selected
                ? selected
                : RadialActionKind.None;
            bool keyboard = kind is RadialActionKind.KeyboardKey or RadialActionKind.KeyboardShortcut;
            bool shortcut = kind == RadialActionKind.KeyboardShortcut;
            _ctrl.Visible = shortcut;
            _alt.Visible = shortcut;
            _shift.Visible = shortcut;
            _win.Visible = shortcut;
            _keyLabel.Visible = keyboard;
            _keyEditor.Visible = keyboard;
            _ds4Editor.Visible = kind == RadialActionKind.Ds4Button;
        }

        private static string KindText(RadialActionKind kind) => kind switch
        {
            RadialActionKind.None => "无",
            RadialActionKind.KeyboardKey => "键盘单键",
            RadialActionKind.KeyboardShortcut => "键盘组合键",
            RadialActionKind.Ds4Button => "DS4 按键",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private static ComboBox DropDown<T>(IEnumerable<T> items, int width)
        {
            var editor = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                BackColor = ThemeColors.ControlDark,
                ForeColor = ThemeColors.TextMain,
                FormattingEnabled = true
            };
            editor.Items.AddRange(items.Cast<object>().ToArray());
            return editor;
        }

        private static Label DetailLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ThemeColors.TextSecondary,
            Margin = new Padding(5, 5, 4, 0)
        };

        private static CheckBox Modifier(string text) => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = ThemeColors.TextMain,
            Margin = new Padding(0, 5, 4, 0)
        };
    }
}

/// <summary>
/// Compatibility host for tests and any out-of-process tooling that still constructs the
/// historical settings form. The receiver runtime hosts <see cref="RadialMenuSettingsControl"/>
/// directly and never opens this form.
/// </summary>
public sealed class RadialMenuSettingsForm : Form
{
    private readonly RadialMenuController _controller;
    private readonly ReceiverUiScaling _receiverUiScaling;

    public RadialMenuSettingsForm(
        RadialMenuController controller,
        RadialMenuSettingsStore store,
        Action<RadialMenuSettings> applySettings,
        Action<string> log,
        RadialVisualPackCatalog? visualPackCatalog = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        Text = "环形菜单设置";
        ClientSize = ReceiverUiLayoutMetrics.SettingsClientBaseline;
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.TextMain;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var settingsControl = new RadialMenuSettingsControl
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(settingsControl);

        _receiverUiScaling = new ReceiverUiScaling(this);
        settingsControl.AttachReceiverUiScaling(_receiverUiScaling);
        settingsControl.Initialize(controller, store, applySettings, log, visualPackCatalog);
        _receiverUiScaling.Apply(controller.ActiveSettings.ReceiverUiScalePercent);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.ClosePreview();
        _receiverUiScaling.Dispose();
        base.OnFormClosed(e);
    }
}
