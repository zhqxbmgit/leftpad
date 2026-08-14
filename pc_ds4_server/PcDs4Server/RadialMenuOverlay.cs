using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PcDs4Server;

public sealed class RadialMenuOverlay : Form, IRadialMenuOverlay
{
    private const byte AcSrcAlpha = 0x01;
    private const byte AcSrcOver = 0x00;
    private const int UlwAlpha = 0x00000002;

    private const int PetalCount = 6;
    private const int DirectionAngle = 360 / PetalCount;
    private const float InnerSweepInset = 12f;

    private readonly object _stateLock = new();
    private RadialMenuSettings _currentSettings = RadialMenuSettings.Default;
    private int _selectedSlot;
    private bool _overlayVisible;

    public RadialMenuOverlay()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        ClientSize = new Size(
            RadialMenuSettings.Default.BaseCanvasSize,
            RadialMenuSettings.Default.BaseCanvasSize);
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

    public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (selectedSlot is < 0 or > PetalCount)
            throw new ArgumentOutOfRangeException(nameof(selectedSlot));
        RadialMenuRenderMetrics metrics = settings.CreateRenderMetrics();
        lock (_stateLock)
        {
            _currentSettings = settings with { };
            _selectedSlot = selectedSlot;
            _overlayVisible = true;
        }

        RunOnUiThread(() =>
        {
            ClientSize = new Size(metrics.CanvasSize, metrics.CanvasSize);
            Location = new Point(
                screenPoint.X - (metrics.CanvasSize / 2),
                screenPoint.Y - (metrics.CanvasSize / 2));
            _ = Handle;
            RenderLayeredWindow(settings, metrics, selectedSlot);
            if (!Visible) base.Show();
        });
    }

    public new void Hide()
    {
        lock (_stateLock)
        {
            _overlayVisible = false;
        }

        RunOnUiThread(base.Hide);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        RadialMenuSettings settings;
        int selectedSlot;
        lock (_stateLock)
        {
            settings = _currentSettings;
            selectedSlot = _selectedSlot;
        }
        DrawOverlay(e.Graphics, settings, settings.CreateRenderMetrics(), selectedSlot);
    }

    private static void DrawOverlay(
        Graphics graphics,
        RadialMenuSettings settings,
        RadialMenuRenderMetrics metrics,
        int selectedSlot)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        float center = metrics.CanvasSize / 2f;
        var centerPoint = new PointF(center, center);

        Color normalFill = Color.FromArgb(settings.FillAlpha, 27, 27, 36);
        Color normalBorder = Color.FromArgb(settings.BorderAlpha, 128, 105, 148);
        Color normalText = Color.FromArgb(settings.TextAlpha, 242, 243, 247);
        Color selectedFill = BlendColor(
            normalFill,
            Color.FromArgb(byte.MaxValue, 118, 80, 150),
            settings.HighlightAlpha);
        Color selectedBorder = BlendColor(
            normalBorder,
            Color.FromArgb(byte.MaxValue, 218, 180, 242),
            settings.HighlightAlpha);
        Color selectedText = BlendColor(
            normalText,
            Color.White,
            settings.HighlightAlpha);

        using var petalBrush = new SolidBrush(normalFill);
        using var selectedPetalBrush = new SolidBrush(selectedFill);
        float normalBorderWidth = Math.Max(0.5f, 1.2f * metrics.ScaleFactor);
        float selectedBorderWidth = normalBorderWidth +
            ((Math.Max(0.5f, 1.8f * metrics.ScaleFactor) - normalBorderWidth) *
             (settings.HighlightAlpha / (float)byte.MaxValue));
        using var petalPen = new Pen(normalBorder, normalBorderWidth)
        {
            LineJoin = LineJoin.Round
        };
        using var selectedPetalPen = new Pen(
            selectedBorder,
            selectedBorderWidth)
        {
            LineJoin = LineJoin.Round
        };
        using var textBrush = new SolidBrush(normalText);
        using var selectedTextBrush = new SolidBrush(selectedText);
        using var font = new Font("Segoe UI", metrics.FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        for (int index = 0; index < PetalCount; index++)
        {
            float centerAngle = -90 + (index * DirectionAngle);
            using GraphicsPath petal = CreatePetalPath(centerPoint, centerAngle, metrics);
            bool selected = selectedSlot == index + 1;
            graphics.FillPath(selected ? selectedPetalBrush : petalBrush, petal);
            graphics.DrawPath(selected ? selectedPetalPen : petalPen, petal);

            PointF textCenter = PointOnCircle(centerPoint, metrics.TextRadius, centerAngle);
            float textWidth = 32f * metrics.ScaleFactor;
            float textHeight = 26f * metrics.ScaleFactor;
            var textBounds = new RectangleF(
                textCenter.X - (textWidth / 2f),
                textCenter.Y - (textHeight / 2f),
                textWidth,
                textHeight);
            graphics.DrawString(
                (index + 1).ToString(),
                font,
                selected ? selectedTextBrush : textBrush,
                textBounds,
                format);
        }

        var hubBounds = CenteredCircle(centerPoint, metrics.HubRadius * 2f);
        using (var hubBrush = new SolidBrush(Color.FromArgb(228, 24, 24, 33)))
        using (var hubPen = new Pen(
            Color.FromArgb(126, 128, 105, 148),
            Math.Max(0.5f, 1.6f * metrics.ScaleFactor)))
        {
            graphics.FillEllipse(hubBrush, hubBounds);
            graphics.DrawEllipse(hubPen, hubBounds);
        }

        var centerDotBounds = CenteredCircle(centerPoint, 7f * metrics.ScaleFactor);
        using var centerDotBrush = new SolidBrush(Color.FromArgb(170, 224, 226, 232));
        graphics.FillEllipse(centerDotBrush, centerDotBounds);
    }

    private static GraphicsPath CreatePetalPath(
        PointF center,
        float centerAngle,
        RadialMenuRenderMetrics metrics)
    {
        float innerSweepAngle = Math.Max(1f, metrics.PetalSweepAngle - InnerSweepInset);
        float outerStartAngle = centerAngle - (metrics.PetalSweepAngle / 2f);
        float outerEndAngle = centerAngle + (metrics.PetalSweepAngle / 2f);
        float innerStartAngle = centerAngle - (innerSweepAngle / 2f);
        float innerEndAngle = centerAngle + (innerSweepAngle / 2f);

        RectangleF outerBounds = CenteredCircle(center, metrics.PetalOuterRadius * 2f);
        RectangleF innerBounds = CenteredCircle(center, metrics.PetalInnerRadius * 2f);
        PointF outerStart = PointOnCircle(center, metrics.PetalOuterRadius, outerStartAngle);
        PointF outerEnd = PointOnCircle(center, metrics.PetalOuterRadius, outerEndAngle);
        PointF innerStart = PointOnCircle(center, metrics.PetalInnerRadius, innerStartAngle);
        PointF innerEnd = PointOnCircle(center, metrics.PetalInnerRadius, innerEndAngle);

        PointF outerEndTangent = ClockwiseTangent(outerEndAngle);
        PointF innerEndTangent = CounterClockwiseTangent(innerEndAngle);
        PointF innerStartTangent = CounterClockwiseTangent(innerStartAngle);
        PointF outerStartTangent = ClockwiseTangent(outerStartAngle);

        var path = new GraphicsPath();
        path.AddArc(outerBounds, outerStartAngle, metrics.PetalSweepAngle);
        path.AddBezier(
            outerEnd,
            Offset(outerEnd, outerEndTangent, 3f * metrics.ScaleFactor),
            Offset(innerEnd, innerEndTangent, -4.4f * metrics.ScaleFactor),
            innerEnd);
        path.AddArc(innerBounds, innerEndAngle, -innerSweepAngle);
        path.AddBezier(
            innerStart,
            Offset(innerStart, innerStartTangent, 4.4f * metrics.ScaleFactor),
            Offset(outerStart, outerStartTangent, -3f * metrics.ScaleFactor),
            outerStart);
        path.CloseFigure();
        return path;
    }

    private static PointF PointOnCircle(PointF center, float radius, float angleDegrees)
    {
        double angle = angleDegrees * (Math.PI / 180.0);
        return new PointF(
            center.X + (float)(Math.Cos(angle) * radius),
            center.Y + (float)(Math.Sin(angle) * radius));
    }

    private static PointF ClockwiseTangent(float angleDegrees)
    {
        double angle = angleDegrees * (Math.PI / 180.0);
        return new PointF(-(float)Math.Sin(angle), (float)Math.Cos(angle));
    }

    private static PointF CounterClockwiseTangent(float angleDegrees)
    {
        PointF clockwise = ClockwiseTangent(angleDegrees);
        return new PointF(-clockwise.X, -clockwise.Y);
    }

    private static PointF Offset(PointF point, PointF direction, float distance)
    {
        return new PointF(
            point.X + (direction.X * distance),
            point.Y + (direction.Y * distance));
    }

    private static RectangleF CenteredCircle(PointF center, float diameter)
    {
        float radius = diameter / 2f;
        return new RectangleF(center.X - radius, center.Y - radius, diameter, diameter);
    }

    private static Color BlendColor(Color from, Color to, int strength)
    {
        if (strength <= 0) return from;
        if (strength >= byte.MaxValue) return to;

        double amount = strength / (double)byte.MaxValue;
        return Color.FromArgb(
            BlendChannel(from.A, to.A, amount),
            BlendChannel(from.R, to.R, amount),
            BlendChannel(from.G, to.G, amount),
            BlendChannel(from.B, to.B, amount));
    }

    private static int BlendChannel(int from, int to, double amount) =>
        (int)Math.Round(from + ((to - from) * amount), MidpointRounding.AwayFromZero);

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void RenderLayeredWindow(
        RadialMenuSettings settings,
        RadialMenuRenderMetrics metrics,
        int selectedSlot)
    {
        if (!IsHandleCreated || IsDisposed) return;

        using var bitmap = new Bitmap(
            metrics.CanvasSize,
            metrics.CanvasSize,
            PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            DrawOverlay(graphics, settings, metrics, selectedSlot);
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr previousBitmap = SelectObject(memoryDc, bitmapHandle);

        try
        {
            var destination = new NativePoint(Left, Top);
            var source = new NativePoint(0, 0);
            var size = new NativeSize(metrics.CanvasSize, metrics.CanvasSize);
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
                    $"Failed to update radial menu transparency (Win32 error {Marshal.GetLastWin32Error()}).");
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
