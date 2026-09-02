using System.Drawing;

namespace PcDs4Server;

internal static class ReceiverUiLayoutMetrics
{
    public static readonly Size MainClientBaseline = new(2000, 800);
    public static readonly Size SettingsClientBaseline = new(1670, 700);

    public const int MainTopBarHeight = 44;
    public const int MainSidebarWidth = 210;
    public const int MainHorizontalPadding = 28;
    public const int MainBottomPadding = 24;
    public const int MainContentPadding = 32;
    public const int MainHeaderHeight = 76;
    public const int MainStatusCardsHeight = 118;
    public const int MainOutputAreaHeight = 210;
    public const int MainOutputControlsWidth = 400;
    public const int MainOutputModeComboBoxWidth = 170;
    public const int StatusCardGap = 16;
    public const int StatusCardCount = 4;

    public const int SettingsButtonAreaHeight = 92;
    public const int SettingsValidationAreaHeight = 34;
    public const int SettingsTabChromeAllowance = 34;
    public const int SettingsPageVerticalPadding = 12;
    public const int SettingsEditorTableVerticalPadding = 8;
    public const int SettingsMappingTableVerticalPadding = 8;
    public const int SettingsContentColumnCount = 2;
    public const int SettingsContentColumnGutter = 28;
    public const int SettingsBasicRowCount = 9;
    public const int SettingsBasicLeftRowCount = 5;
    public const int SettingsAdvancedRowCount = 8;
    public const int SettingsEditorRowHeight = 48;
    public const int SettingsMappingRowHeight = 58;
    public const int SettingsSlotLabelColumnWidth = 104;
    public const int SettingsMappingKindColumnWidth = 144;
    public const int SettingsButtonGap = 12;

    public static int MainInnerContentWidth =>
        MainClientBaseline.Width -
        MainSidebarWidth -
        (2 * MainHorizontalPadding) -
        (2 * MainContentPadding);

    public static Rectangle MainOutputControlsBounds => new(
        0,
        0,
        MainOutputControlsWidth,
        MainOutputAreaHeight);

    public static Rectangle MainKeyboardMappingsBounds => new(
        MainOutputControlsWidth,
        0,
        MainInnerContentWidth - MainOutputControlsWidth,
        MainOutputAreaHeight);

    public static IReadOnlyList<Rectangle> MainSectionBounds
    {
        get
        {
            int x = MainSidebarWidth + MainHorizontalPadding + MainContentPadding;
            int y = MainTopBarHeight + MainContentPadding;
            int width = MainInnerContentWidth;
            return new[]
            {
                new Rectangle(x, y, width, MainHeaderHeight),
                new Rectangle(x, y + MainHeaderHeight, width, MainStatusCardsHeight),
                new Rectangle(
                    x,
                    y + MainHeaderHeight + MainStatusCardsHeight,
                    width,
                    MainOutputAreaHeight)
            };
        }
    }

    public static Size SettingsEmbeddedViewportBaseline => new(
        MainClientBaseline.Width -
        MainSidebarWidth -
        (2 * MainHorizontalPadding) -
        (2 * MainContentPadding),
        MainClientBaseline.Height -
        MainTopBarHeight -
        MainBottomPadding -
        (2 * MainContentPadding) -
        MainHeaderHeight);

    public static int SettingsBasicContentHeight =>
        SettingsEditorTableVerticalPadding +
        (SettingsBasicLeftRowCount * SettingsEditorRowHeight);

    public static int SettingsPageReachableHeight =>
        SettingsEmbeddedViewportBaseline.Height -
        SettingsButtonAreaHeight -
        SettingsValidationAreaHeight -
        SettingsTabChromeAllowance -
        SettingsPageVerticalPadding;

    public static int SettingsBasicReachableHeight => SettingsPageReachableHeight;

    public static int SettingsAdvancedContentHeight =>
        SettingsEditorTableVerticalPadding +
        ((SettingsAdvancedRowCount / SettingsContentColumnCount) * SettingsEditorRowHeight);

    public static int GetSettingsMappingRowsPerColumn(int slotCount) =>
        (slotCount + SettingsContentColumnCount - 1) / SettingsContentColumnCount;

    public static int GetSettingsMappingDefaultContentHeight(int slotCount) =>
        SettingsMappingTableVerticalPadding +
        (GetSettingsMappingRowsPerColumn(slotCount) * SettingsMappingRowHeight);

    public static int GetSettingsButtonMinimumWidth(string text) => text switch
    {
        "预览" => 72,
        "隐藏预览" => 96,
        "应用并保存" => 112,
        "恢复默认" => 96,
        "关闭" => 72,
        _ => 72
    };

    public static IReadOnlyList<Rectangle> GetSettingsButtonBounds()
    {
        string[] labels = { "预览", "隐藏预览", "应用并保存", "恢复默认" };
        int x = 14;
        var bounds = new List<Rectangle>(labels.Length);
        foreach (string label in labels)
        {
            int width = GetSettingsButtonMinimumWidth(label);
            bounds.Add(new Rectangle(x, 14, width, 36));
            x += width + SettingsButtonGap;
        }
        return bounds;
    }

    public static IReadOnlyList<Rectangle> GetStatusCardBounds()
    {
        int totalGap = StatusCardGap * (StatusCardCount - 1);
        int cardWidth = (MainInnerContentWidth - totalGap) / StatusCardCount;
        return Enumerable.Range(0, StatusCardCount)
            .Select(index => new Rectangle(
                index * (cardWidth + StatusCardGap),
                0,
                cardWidth,
                MainStatusCardsHeight))
            .ToArray();
    }
}

/// <summary>
/// Applies the receiver desktop UI preference on top of the immutable, hand-authored
/// WinForms layout. This does not participate in radial-menu rendering or DPI backing.
/// </summary>
internal sealed class ReceiverUiScaling : IDisposable
{
    public const int DefaultScalePercent = 150;

    private static readonly int[] PresetValues = [100, 125, 150, 175, 200];

    private readonly Form _form;
    private readonly Func<Form, Rectangle> _workingAreaProvider;
    private readonly Size _baselineClientSize;
    private readonly Size _baselineMaximumSize;
    private readonly FontSpec _baselineFormFont;
    private readonly Dictionary<Control, ControlBaseline> _controlBaselines = new();
    private readonly Dictionary<RowStyle, float> _rowBaselines = new();
    private readonly Dictionary<ColumnStyle, float> _columnBaselines = new();
    private int _currentScalePercent = 100;
    private int _currentEffectiveScalePercent = 100;
    private int? _referenceDpi;
    private bool _disposed;

    public ReceiverUiScaling(
        Form form,
        Func<Form, Rectangle>? workingAreaProvider = null)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _workingAreaProvider = workingAreaProvider ??
            (static target => Screen.FromRectangle(target.Bounds).WorkingArea);
        _baselineClientSize = form.ClientSize;
        _baselineMaximumSize = form.MaximumSize;
        _baselineFormFont = FontSpec.From(form.Font);
        CaptureCurrentTree();
        if (form.IsHandleCreated)
            _referenceDpi = form.DeviceDpi;
        form.HandleCreated += HandleFormHandleCreated;
        form.DpiChanged += HandleFormDpiChanged;
    }

    public static IReadOnlyList<int> Presets => PresetValues;

    public int CurrentScalePercent => _currentScalePercent;

    public int CurrentEffectiveScalePercent => _currentEffectiveScalePercent;

    public static bool IsPreset(int scalePercent) =>
        Array.IndexOf(PresetValues, scalePercent) >= 0;

    public static int Normalize(int scalePercent) =>
        IsPreset(scalePercent) ? scalePercent : DefaultScalePercent;

    public static int GetEffectiveScalePercent(
        int userScalePercent,
        int referenceDpi,
        int currentDpi)
    {
        int safeReferenceDpi = referenceDpi > 0 ? referenceDpi : 96;
        int safeCurrentDpi = currentDpi > 0 ? currentDpi : safeReferenceDpi;
        return (int)Math.Round(
            userScalePercent * (safeCurrentDpi / (double)safeReferenceDpi),
            MidpointRounding.AwayFromZero);
    }

    public static int Scale(int baseline, int scalePercent) =>
        (int)Math.Round(baseline * (scalePercent / 100d), MidpointRounding.AwayFromZero);

    public static float Scale(float baseline, int scalePercent) =>
        baseline * (scalePercent / 100f);

    public static Size Scale(Size baseline, int scalePercent) => new(
        Scale(baseline.Width, scalePercent),
        Scale(baseline.Height, scalePercent));

    internal static Size ClampClientSizeToWorkingArea(
        Size desiredClientSize,
        Size nonClientSize,
        Rectangle workingArea)
    {
        if (workingArea.Width <= 0 || workingArea.Height <= 0)
            return desiredClientSize;

        return new Size(
            Math.Min(
                desiredClientSize.Width,
                Math.Max(1, workingArea.Width - Math.Max(0, nonClientSize.Width))),
            Math.Min(
                desiredClientSize.Height,
                Math.Max(1, workingArea.Height - Math.Max(0, nonClientSize.Height))));
    }

    internal static Rectangle ClampWindowBoundsToWorkingArea(
        Rectangle desiredBounds,
        Rectangle workingArea)
    {
        if (workingArea.Width <= 0 || workingArea.Height <= 0)
            return desiredBounds;

        int width = Math.Min(desiredBounds.Width, workingArea.Width);
        int height = Math.Min(desiredBounds.Height, workingArea.Height);
        int left = Math.Clamp(desiredBounds.Left, workingArea.Left, workingArea.Right - width);
        int top = Math.Clamp(desiredBounds.Top, workingArea.Top, workingArea.Bottom - height);
        return new Rectangle(left, top, width, height);
    }

    public int ScaleLogical(int baseline) => Scale(baseline, _currentEffectiveScalePercent);

    public void CaptureBaseline(Control control, bool inheritFormFont = false)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (inheritFormFont)
            control.Font = _baselineFormFont.Create(scalePercent: 100);
        CaptureControl(
            control,
            new HashSet<Control>(),
            new HashSet<RowStyle>(),
            new HashSet<ColumnStyle>());
    }

    /// <summary>
    /// Re-applies every metric from the original baseline. Calling this repeatedly never
    /// multiplies the already-scaled values. Newly-created mapping controls are captured
    /// as their 100% factory metrics before the current preference is applied to them.
    /// </summary>
    public void Apply(int scalePercent, bool preserveCenter = false)
    {
        if (!IsPreset(scalePercent))
            throw new ArgumentOutOfRangeException(nameof(scalePercent), scalePercent,
                "Receiver UI scale must be one of 100, 125, 150, 175, or 200 percent.");

        int effectiveScalePercent = _referenceDpi is int referenceDpi && _form.IsHandleCreated
            ? GetEffectiveScalePercent(scalePercent, referenceDpi, _form.DeviceDpi)
            : scalePercent;
        ApplyCore(scalePercent, effectiveScalePercent, preserveCenter);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _form.HandleCreated -= HandleFormHandleCreated;
        _form.DpiChanged -= HandleFormDpiChanged;
    }

    private void ApplyCore(
        int userScalePercent,
        int effectiveScalePercent,
        bool preserveCenter)
    {
        Point center = new(
            _form.Left + (_form.Width / 2),
            _form.Top + (_form.Height / 2));
        bool normalWindow = _form.WindowState == FormWindowState.Normal;
        bool reposition = preserveCenter && _form.Visible && normalWindow;
        Rectangle workingArea = _workingAreaProvider(_form);

        CaptureCurrentTree();
        _form.SuspendLayout();
        try
        {
            _currentScalePercent = userScalePercent;
            _currentEffectiveScalePercent = effectiveScalePercent;
            Size desiredClientSize = Scale(_baselineClientSize, effectiveScalePercent);
            Size currentNonClientSize = new(
                Math.Max(0, _form.Width - _form.ClientSize.Width),
                Math.Max(0, _form.Height - _form.ClientSize.Height));
            Size targetClientSize = ClampClientSizeToWorkingArea(
                desiredClientSize,
                currentNonClientSize,
                workingArea);
            _form.MinimumSize = new Size(
                targetClientSize.Width + currentNonClientSize.Width,
                targetClientSize.Height + currentNonClientSize.Height);
            _form.MaximumSize = _baselineMaximumSize.IsEmpty
                ? Size.Empty
                : Scale(_baselineMaximumSize, effectiveScalePercent);
            _form.ClientSize = targetClientSize;

            foreach (ControlBaseline baseline in _controlBaselines.Values)
                baseline.Apply(effectiveScalePercent);

            foreach ((RowStyle style, float baseline) in _rowBaselines)
            {
                if (style.SizeType == SizeType.Absolute)
                    style.Height = Scale(baseline, effectiveScalePercent);
            }

            foreach ((ColumnStyle style, float baseline) in _columnBaselines)
            {
                if (style.SizeType == SizeType.Absolute)
                    style.Width = Scale(baseline, effectiveScalePercent);
            }
        }
        finally
        {
            _form.ResumeLayout(performLayout: true);
        }

        if (normalWindow)
        {
            Rectangle desiredBounds = reposition
                ? new Rectangle(
                    center.X - (_form.Width / 2),
                    center.Y - (_form.Height / 2),
                    _form.Width,
                    _form.Height)
                : _form.Bounds;
            _form.Bounds = ClampWindowBoundsToWorkingArea(desiredBounds, workingArea);
        }

        _form.Invalidate(invalidateChildren: true);
    }

    private void HandleFormHandleCreated(object? sender, EventArgs e)
    {
        _referenceDpi ??= _form.DeviceDpi;
    }

    private void HandleFormDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        _referenceDpi ??= e.DeviceDpiOld > 0 ? e.DeviceDpiOld : _form.DeviceDpi;
        int effectiveScalePercent = GetEffectiveScalePercent(
            _currentScalePercent,
            _referenceDpi.Value,
            e.DeviceDpiNew);
        ApplyCore(_currentScalePercent, effectiveScalePercent, preserveCenter: false);
    }

    private void CaptureCurrentTree()
    {
        HashSet<Control> activeControls = new();
        HashSet<RowStyle> activeRows = new();
        HashSet<ColumnStyle> activeColumns = new();
        CaptureControl(_form, activeControls, activeRows, activeColumns);

        foreach (Control removed in _controlBaselines.Keys.Where(control => !activeControls.Contains(control)).ToArray())
            _controlBaselines.Remove(removed);
        foreach (RowStyle removed in _rowBaselines.Keys.Where(style => !activeRows.Contains(style)).ToArray())
            _rowBaselines.Remove(removed);
        foreach (ColumnStyle removed in _columnBaselines.Keys.Where(style => !activeColumns.Contains(style)).ToArray())
            _columnBaselines.Remove(removed);
    }

    private void CaptureControl(
        Control control,
        HashSet<Control> activeControls,
        HashSet<RowStyle> activeRows,
        HashSet<ColumnStyle> activeColumns)
    {
        activeControls.Add(control);
        if (!_controlBaselines.ContainsKey(control))
            _controlBaselines.Add(control, new ControlBaseline(control, control == _form));

        if (control is TableLayoutPanel table)
        {
            foreach (RowStyle style in table.RowStyles)
            {
                activeRows.Add(style);
                if (!_rowBaselines.ContainsKey(style))
                    _rowBaselines.Add(style, style.Height);
            }

            foreach (ColumnStyle style in table.ColumnStyles)
            {
                activeColumns.Add(style);
                if (!_columnBaselines.ContainsKey(style))
                    _columnBaselines.Add(style, style.Width);
            }
        }

        foreach (Control child in control.Controls)
            CaptureControl(child, activeControls, activeRows, activeColumns);
    }

    private sealed class ControlBaseline
    {
        private readonly Control _control;
        private readonly bool _isForm;
        private readonly Rectangle _bounds;
        private readonly Padding _padding;
        private readonly Padding _margin;
        private readonly Size _minimumSize;
        private readonly Size _maximumSize;
        private readonly bool _hasIndependentFont;
        private readonly FontSpec _font;
        private readonly Point? _tabPadding;
        private readonly int? _roundedPanelRadius;
        private readonly float? _roundedPanelBorderWidth;

        public ControlBaseline(Control control, bool isForm)
        {
            _control = control;
            _isForm = isForm;
            _bounds = control.Bounds;
            _padding = control.Padding;
            _margin = control.Margin;
            _minimumSize = control.MinimumSize;
            _maximumSize = control.MaximumSize;
            _hasIndependentFont = isForm ||
                control.Parent == null ||
                !control.Font.Equals(control.Parent.Font);
            _font = FontSpec.From(control.Font);
            _tabPadding = control is TabControl tab ? tab.Padding : null;
            _roundedPanelRadius = control is RoundedPanel rounded ? rounded.Radius : null;
            _roundedPanelBorderWidth = control is RoundedPanel roundedPanel
                ? roundedPanel.BorderWidth
                : null;
        }

        public void Apply(int scalePercent)
        {
            if (_isForm)
            {
                ApplyFont(scalePercent);
                return;
            }

            _control.Padding = Scale(_padding, scalePercent);
            _control.Margin = Scale(_margin, scalePercent);
            _control.MinimumSize = _minimumSize.IsEmpty
                ? Size.Empty
                : ReceiverUiScaling.Scale(_minimumSize, scalePercent);
            _control.MaximumSize = _maximumSize.IsEmpty
                ? Size.Empty
                : ReceiverUiScaling.Scale(_maximumSize, scalePercent);
            ApplyFont(scalePercent);

            if (_tabPadding is Point tabPadding && _control is TabControl tab)
            {
                tab.Padding = new Point(
                    ReceiverUiScaling.Scale(tabPadding.X, scalePercent),
                    ReceiverUiScaling.Scale(tabPadding.Y, scalePercent));
            }

            if (_roundedPanelRadius is int radius && _control is RoundedPanel rounded)
            {
                rounded.Radius = Math.Max(1, ReceiverUiScaling.Scale(radius, scalePercent));
                rounded.BorderWidth = Math.Max(1f,
                    ReceiverUiScaling.Scale(_roundedPanelBorderWidth ?? 1f, scalePercent));
            }

            ApplyBounds(scalePercent);
        }

        private void ApplyFont(int scalePercent)
        {
            if (!_hasIndependentFont) return;
            _control.Font = _font.Create(scalePercent);
        }

        private void ApplyBounds(int scalePercent)
        {
            Point location = new(
                ReceiverUiScaling.Scale(_bounds.X, scalePercent),
                ReceiverUiScaling.Scale(_bounds.Y, scalePercent));
            Size size = ReceiverUiScaling.Scale(_bounds.Size, scalePercent);

            if (_control.AutoSize)
            {
                if (_control.Dock == DockStyle.None &&
                    _control.Parent is not FlowLayoutPanel and not TableLayoutPanel)
                {
                    _control.Location = location;
                }
                return;
            }

            switch (_control.Dock)
            {
                case DockStyle.Top:
                case DockStyle.Bottom:
                    _control.Height = size.Height;
                    break;
                case DockStyle.Left:
                case DockStyle.Right:
                    _control.Width = size.Width;
                    break;
                case DockStyle.Fill:
                    break;
                default:
                    if (_control.Parent is FlowLayoutPanel or TableLayoutPanel)
                        _control.Size = size;
                    else
                        _control.Bounds = new Rectangle(location, size);
                    break;
            }
        }

        private static Padding Scale(Padding padding, int scalePercent) => new(
            ReceiverUiScaling.Scale(padding.Left, scalePercent),
            ReceiverUiScaling.Scale(padding.Top, scalePercent),
            ReceiverUiScaling.Scale(padding.Right, scalePercent),
            ReceiverUiScaling.Scale(padding.Bottom, scalePercent));
    }

    private readonly record struct FontSpec(
        string FamilyName,
        float Size,
        FontStyle Style,
        GraphicsUnit Unit,
        byte GdiCharSet,
        bool GdiVerticalFont)
    {
        public static FontSpec From(Font font) => new(
            font.FontFamily.Name,
            font.Size,
            font.Style,
            font.Unit,
            font.GdiCharSet,
            font.GdiVerticalFont);

        public Font Create(int scalePercent) => new(
            FamilyName,
            ReceiverUiScaling.Scale(Size, scalePercent),
            Style,
            Unit,
            GdiCharSet,
            GdiVerticalFont);
    }
}
