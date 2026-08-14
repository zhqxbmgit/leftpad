namespace PcDs4Server;

public sealed class RadialMenuSettingsForm : Form
{
    private readonly RadialMenuController _controller;
    private readonly RadialMenuSettingsStore _store;
    private readonly Action<RadialMenuSettings> _applySettings;
    private readonly Action<string> _log;

    private readonly NumericUpDown _scale = Editor(60, 140);
    private readonly NumericUpDown _doubleTapWindow = Editor(
        RadialMenuSettings.MinimumDoubleTapWindowMs,
        RadialMenuSettings.MaximumDoubleTapWindowMs);
    private readonly NumericUpDown _fillAlpha = Editor(0, 255);
    private readonly NumericUpDown _borderAlpha = Editor(0, 255);
    private readonly NumericUpDown _textAlpha = Editor(0, 255);
    private readonly NumericUpDown _canvas = Editor(160, 800);
    private readonly NumericUpDown _hubRadius = Editor(1, 399);
    private readonly NumericUpDown _innerRadius = Editor(1, 399);
    private readonly NumericUpDown _outerRadius = Editor(2, 399);
    private readonly NumericUpDown _textRadius = Editor(1, 399);
    private readonly NumericUpDown _gap = Editor(0, 12, decimalPlaces: 1, increment: 0.5m);
    private readonly NumericUpDown _fontSize = Editor(6, 48, decimalPlaces: 1, increment: 0.5m);
    private readonly Label _validationStatus = new()
    {
        Dock = DockStyle.Bottom,
        Height = 34,
        Padding = new Padding(16, 6, 16, 0),
        ForeColor = ThemeColors.Error,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private bool _populating;

    public RadialMenuSettingsForm(
        RadialMenuController controller,
        RadialMenuSettingsStore store,
        Action<RadialMenuSettings> applySettings,
        Action<string> log)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _applySettings = applySettings ?? throw new ArgumentNullException(nameof(applySettings));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        Text = "环形菜单设置";
        ClientSize = new Size(520, 500);
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.TextMain;
        Font = new Font("Microsoft YaHei UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        tabs.TabPages.Add(CreateBasicPage());
        tabs.TabPages.Add(CreateAdvancedPage());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 82,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        buttons.Controls.Add(ActionButton("预览", Preview));
        buttons.Controls.Add(ActionButton("隐藏预览", _controller.ClosePreview));
        buttons.Controls.Add(ActionButton("应用并保存", ApplyAndSave));
        buttons.Controls.Add(ActionButton("恢复默认", () => Populate(RadialMenuSettings.Default)));
        buttons.Controls.Add(ActionButton("关闭", Close));

        Controls.Add(tabs);
        Controls.Add(_validationStatus);
        Controls.Add(buttons);
        Populate(_controller.ActiveSettings);
        SubscribeToLivePreviewChanges();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.ClosePreview();
        base.OnFormClosed(e);
    }

    private TabPage CreateBasicPage()
    {
        var page = Page("基础");
        page.Controls.Add(EditorTable(
            ("整体大小 (%)", _scale),
            ("环形菜单双击窗口 (ms)", _doubleTapWindow),
            ("花瓣透明度 (0-255)", _fillAlpha),
            ("边框透明度 (0-255)", _borderAlpha),
            ("文字透明度 (0-255)", _textAlpha)));
        return page;
    }

    private TabPage CreateAdvancedPage()
    {
        var page = Page("高级");
        page.Controls.Add(EditorTable(
            ("画布大小 (px)", _canvas),
            ("中心圆半径 (px)", _hubRadius),
            ("花瓣内半径 (px)", _innerRadius),
            ("花瓣外半径 (px)", _outerRadius),
            ("文字位置半径 (px)", _textRadius),
            ("花瓣间隔角度 (°)", _gap),
            ("字体大小 (px)", _fontSize)));
        return page;
    }

    private void Preview()
    {
        if (!TryReadSettings(out RadialMenuSettings settings, showDialog: true)) return;
        _controller.PreviewAt(Cursor.Position, settings);
    }

    private void ApplyAndSave()
    {
        if (!TryReadSettings(out RadialMenuSettings settings, showDialog: true)) return;

        _applySettings(settings);
        if (_store.TrySave(settings, out string error))
        {
            _log("[环形菜单] 设置已保存");
            MessageBox.Show(this, "环形菜单设置已应用并保存。",
                "环形菜单", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            _log($"[环形菜单] 设置保存失败：{error}");
            MessageBox.Show(this, $"设置已在本次运行中应用，但保存失败，请查看日志。\n\n{error}",
                "环形菜单", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool TryReadSettings(out RadialMenuSettings settings, bool showDialog)
    {
        settings = new RadialMenuSettings
        {
            ScalePercent = Decimal.ToInt32(_scale.Value),
            BaseCanvasSize = Decimal.ToInt32(_canvas.Value),
            HubRadius = Decimal.ToInt32(_hubRadius.Value),
            PetalInnerRadius = Decimal.ToInt32(_innerRadius.Value),
            PetalOuterRadius = Decimal.ToInt32(_outerRadius.Value),
            TextRadius = Decimal.ToInt32(_textRadius.Value),
            PetalGapDegrees = (float)_gap.Value,
            FontSize = (float)_fontSize.Value,
            FillAlpha = Decimal.ToInt32(_fillAlpha.Value),
            BorderAlpha = Decimal.ToInt32(_borderAlpha.Value),
            TextAlpha = Decimal.ToInt32(_textAlpha.Value),
            DoubleTapWindowMs = Decimal.ToInt32(_doubleTapWindow.Value)
        };

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
            _scale.Value = settings.ScalePercent;
            _doubleTapWindow.Value = settings.DoubleTapWindowMs;
            _canvas.Value = settings.BaseCanvasSize;
            _hubRadius.Value = settings.HubRadius;
            _innerRadius.Value = settings.PetalInnerRadius;
            _outerRadius.Value = settings.PetalOuterRadius;
            _textRadius.Value = settings.TextRadius;
            _gap.Value = (decimal)settings.PetalGapDegrees;
            _fontSize.Value = (decimal)settings.FontSize;
            _fillAlpha.Value = settings.FillAlpha;
            _borderAlpha.Value = settings.BorderAlpha;
            _textAlpha.Value = settings.TextAlpha;
        }
        finally
        {
            _populating = false;
        }

        RefreshLivePreview();
    }

    private void SubscribeToLivePreviewChanges()
    {
        foreach (NumericUpDown editor in new[]
        {
            _scale, _fillAlpha, _borderAlpha, _textAlpha, _canvas, _hubRadius,
            _innerRadius, _outerRadius, _textRadius, _gap, _fontSize
        })
        {
            editor.ValueChanged += (_, _) => RefreshLivePreview();
        }
    }

    private void RefreshLivePreview()
    {
        if (_populating || !_controller.IsPreviewActive) return;
        if (!TryReadSettings(out RadialMenuSettings settings, showDialog: false)) return;
        _controller.UpdatePreview(settings);
    }

    private static TabPage Page(string text) => new(text)
    {
        BackColor = ThemeColors.ContentPanel,
        ForeColor = ThemeColors.TextMain,
        Padding = new Padding(14)
    };

    private static TableLayoutPanel EditorTable(params (string Label, Control Editor)[] rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = rows.Length,
            Padding = new Padding(8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

        for (int row = 0; row < rows.Length; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.Controls.Add(new Label
            {
                Text = rows[row].Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ThemeColors.TextSecondary
            }, 0, row);
            rows[row].Editor.Dock = DockStyle.Fill;
            table.Controls.Add(rows[row].Editor, 1, row);
        }

        return table;
    }

    private static NumericUpDown Editor(
        decimal minimum,
        decimal maximum,
        int decimalPlaces = 0,
        decimal increment = 1m) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        DecimalPlaces = decimalPlaces,
        Increment = increment,
        TextAlign = HorizontalAlignment.Right,
        BackColor = ThemeColors.ControlDark,
        ForeColor = ThemeColors.TextMain,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeColors.ControlDark,
            ForeColor = ThemeColors.TextMain
        };
        button.FlatAppearance.BorderColor = ThemeColors.BorderPurple;
        button.Click += (_, _) => action();
        return button;
    }
}
