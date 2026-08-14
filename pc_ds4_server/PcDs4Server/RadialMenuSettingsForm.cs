namespace PcDs4Server;

public sealed class RadialMenuSettingsForm : Form
{
    private readonly RadialMenuController _controller;
    private readonly RadialMenuSettingsStore _store;
    private readonly Action<string> _log;

    private readonly NumericUpDown _scale = Editor(60, 140);
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

    public RadialMenuSettingsForm(
        RadialMenuController controller,
        RadialMenuSettingsStore store,
        Action<string> log)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        Text = "Radial Menu Settings";
        ClientSize = new Size(470, 500);
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.TextMain;
        Font = new Font("Segoe UI", 9f);
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
        buttons.Controls.Add(ActionButton("Preview", Preview));
        buttons.Controls.Add(ActionButton("Hide Preview", _controller.ClosePreview));
        buttons.Controls.Add(ActionButton("Apply && Save", ApplyAndSave));
        buttons.Controls.Add(ActionButton("Reset Defaults", () => Populate(RadialMenuSettings.Default)));
        buttons.Controls.Add(ActionButton("Close", Close));

        Controls.Add(tabs);
        Controls.Add(buttons);
        Populate(_controller.ActiveSettings);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.ClosePreview();
        base.OnFormClosed(e);
    }

    private TabPage CreateBasicPage()
    {
        var page = Page("Basic");
        page.Controls.Add(EditorTable(
            ("Overall Size (%)", _scale),
            ("Petal Fill Opacity (0-255)", _fillAlpha),
            ("Border Opacity (0-255)", _borderAlpha),
            ("Text Opacity (0-255)", _textAlpha)));
        return page;
    }

    private TabPage CreateAdvancedPage()
    {
        var page = Page("Advanced");
        page.Controls.Add(EditorTable(
            ("Base Canvas Size", _canvas),
            ("Hub Radius", _hubRadius),
            ("Petal Inner Radius", _innerRadius),
            ("Petal Outer Radius", _outerRadius),
            ("Text Radius", _textRadius),
            ("Petal Gap (degrees)", _gap),
            ("Font Size (px)", _fontSize)));
        return page;
    }

    private void Preview()
    {
        if (!TryReadSettings(out RadialMenuSettings settings)) return;
        _controller.PreviewAt(Cursor.Position, settings);
    }

    private void ApplyAndSave()
    {
        if (!TryReadSettings(out RadialMenuSettings settings)) return;

        _controller.ApplySettings(settings);
        if (_store.TrySave(settings, out string error))
        {
            _log("[RADIAL] Settings saved");
            MessageBox.Show(this, "Radial menu settings were applied and saved.",
                "Radial Menu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            _log($"[RADIAL] Settings save failed: {error}");
            MessageBox.Show(this, $"Settings were applied for this session but could not be saved.\n\n{error}",
                "Radial Menu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool TryReadSettings(out RadialMenuSettings settings)
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
            TextAlpha = Decimal.ToInt32(_textAlpha.Value)
        };

        if (settings.TryValidate(out string error)) return true;
        MessageBox.Show(this, error, "Invalid Radial Menu Settings",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void Populate(RadialMenuSettings settings)
    {
        _scale.Value = settings.ScalePercent;
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
