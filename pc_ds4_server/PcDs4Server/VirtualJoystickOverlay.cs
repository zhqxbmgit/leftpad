using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PcDs4Server;

public sealed class VirtualJoystickOverlay : Form, IJoystickOverlay
{
    private const byte AcSrcAlpha = 0x01;
    private const byte AcSrcOver = 0x00;
    private const int UlwAlpha = 0x00000002;

    public const int VisualRadius = 26;
    public const int BaseDiameter = VisualRadius * 2;
    public const int KnobDiameter = 26;
    public const double VisualScale = VisualRadius / VirtualJoystickController.JoystickRadius;
    public const int BaseFillAlpha = 155;
    public const int KnobAlpha = 191;
    public static readonly Color BaseFillColor = Color.FromArgb(BaseFillAlpha, 255, 255, 255);
    public static readonly Color KnobFillColor = Color.FromArgb(KnobAlpha, 255, 255, 255);

    private const int MarginSize = (KnobDiameter / 2) + 2;
    private static readonly int CanvasSize = BaseDiameter + (MarginSize * 2);

    private readonly object _stateLock = new();
    private double _knobX;
    private double _knobY;
    private bool _overlayVisible;

    public VirtualJoystickOverlay()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        ClientSize = new Size(CanvasSize, CanvasSize);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        TransparencyKey = Color.Empty;
        Opacity = 1.0;
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
                _ = Handle;
                RenderLayeredWindow();
                base.Show();
            }
            else
            {
                RenderLayeredWindow();
            }
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

        RunOnUiThread(RenderLayeredWindow);
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
        DrawOverlay(e.Graphics);
    }

    private void DrawOverlay(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        double knobX;
        double knobY;
        lock (_stateLock)
        {
            knobX = _knobX;
            knobY = _knobY;
        }

        var baseRect = new Rectangle(MarginSize, MarginSize, BaseDiameter, BaseDiameter);
        using var baseBrush = new SolidBrush(BaseFillColor);
        graphics.FillEllipse(baseBrush, baseRect);

        int center = CanvasSize / 2;
        double visualX = MapLogicalOffsetToVisual(knobX);
        double visualY = MapLogicalOffsetToVisual(knobY);
        var knobRect = new RectangleF(
            (float)(center + visualX - (KnobDiameter / 2.0)),
            (float)(center + visualY - (KnobDiameter / 2.0)),
            KnobDiameter,
            KnobDiameter);
        using var knobBrush = new SolidBrush(KnobFillColor);
        graphics.FillEllipse(knobBrush, knobRect);
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

    private void RenderLayeredWindow()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        using var bitmap = new Bitmap(CanvasSize, CanvasSize, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            DrawOverlay(graphics);
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr previousBitmap = SelectObject(memoryDc, bitmapHandle);

        try
        {
            var destination = new NativePoint(Left, Top);
            var source = new NativePoint(0, 0);
            var size = new NativeSize(CanvasSize, CanvasSize);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = byte.MaxValue,
                AlphaFormat = AcSrcAlpha
            };

            if (!UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    UlwAlpha))
            {
                throw new InvalidOperationException(
                    $"Failed to update MOVE overlay transparency (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            SelectObject(memoryDc, previousBitmap);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        IntPtr window,
        IntPtr destinationDc,
        ref NativePoint destination,
        ref NativeSize size,
        IntPtr sourceDc,
        ref NativePoint source,
        int colorKey,
        ref BlendFunction blend,
        int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr drawingObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr drawingObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int width, int height)
    {
        public int Width = width;
        public int Height = height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}
