using System.Drawing.Drawing2D;

namespace PcDs4Server;

public sealed class VirtualJoystickOverlay : Form, IJoystickOverlay
{
    public const int VisualRadius = 20;
    public const int BaseDiameter = VisualRadius * 2;
    public const int KnobDiameter = 11;
    public const double VisualScale = VisualRadius / VirtualJoystickController.JoystickRadius;

    private const int MarginSize = (KnobDiameter / 2) + 2;
    private static readonly int CanvasSize = BaseDiameter + (MarginSize * 2);

    private readonly object _stateLock = new();
    private double _knobX;
    private double _knobY;
    private bool _overlayVisible;

    public VirtualJoystickOverlay()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Magenta;
        ClientSize = new Size(CanvasSize, CanvasSize);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        TransparencyKey = Color.Magenta;
        Opacity = 0.72;
    }

    public bool IsVisible
    {
        get
        {
            lock (_stateLock)
            {
                return _overlayVisible;
            }
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExLayered = 0x00080000;
            const int WsExTransparent = 0x00000020;
            const int WsExNoActivate = 0x08000000;
            const int WsExToolWindow = 0x00000080;

            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    public void Show(ScreenPoint center)
    {
        lock (_stateLock)
        {
            _knobX = 0;
            _knobY = 0;
            _overlayVisible = true;
        }

        RunOnUiThread(() =>
        {
            Location = new Point(center.X - (CanvasSize / 2), center.Y - (CanvasSize / 2));
            if (!Visible)
            {
                base.Show();
            }
            Invalidate();
        });
    }

    public void UpdateKnob(double x, double y)
    {
        lock (_stateLock)
        {
            if (!_overlayVisible)
            {
                return;
            }

            _knobX = x;
            _knobY = y;
        }

        RunOnUiThread(Invalidate);
    }

    public new void Hide()
    {
        lock (_stateLock)
        {
            _overlayVisible = false;
            _knobX = 0;
            _knobY = 0;
        }

        RunOnUiThread(base.Hide);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        double knobX;
        double knobY;
        lock (_stateLock)
        {
            knobX = _knobX;
            knobY = _knobY;
        }

        var baseRect = new Rectangle(MarginSize, MarginSize, BaseDiameter, BaseDiameter);
        using var baseBrush = new SolidBrush(Color.FromArgb(55, 62, 82));
        using var basePen = new Pen(Color.FromArgb(210, 220, 255), 3f);
        e.Graphics.FillEllipse(baseBrush, baseRect);
        e.Graphics.DrawEllipse(basePen, baseRect);

        int center = CanvasSize / 2;
        double visualX = MapLogicalOffsetToVisual(knobX);
        double visualY = MapLogicalOffsetToVisual(knobY);
        var knobRect = new RectangleF(
            (float)(center + visualX - (KnobDiameter / 2.0)),
            (float)(center + visualY - (KnobDiameter / 2.0)),
            KnobDiameter,
            KnobDiameter);
        using var knobBrush = new SolidBrush(Color.FromArgb(230, 235, 255));
        using var knobPen = new Pen(Color.White, 2f);
        e.Graphics.FillEllipse(knobBrush, knobRect);
        e.Graphics.DrawEllipse(knobPen, knobRect);
    }

    public static double MapLogicalOffsetToVisual(double logicalOffset)
    {
        return logicalOffset * VisualScale;
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
