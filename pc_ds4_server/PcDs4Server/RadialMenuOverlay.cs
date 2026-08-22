using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PcDs4Server;

public sealed class RadialMenuOverlay : Form, IRadialMenuOverlay
{
    private const byte AcSrcAlpha = 0x01;
    private const byte AcSrcOver = 0x00;
    private const int UlwAlpha = 0x00000002;

    private readonly object _stateLock = new();
    private readonly RadialVisualPackRuntime _visualPacks;
    private LayoutDefinition? _layoutDefinition;
    private int _selectedSlot;
    private bool _overlayVisible;
    private bool _assetErrorLogged;

    public RadialMenuOverlay(
        RadialVisualPackCatalog? catalog = null,
        Action<string>? log = null)
    {
        _visualPacks = new RadialVisualPackRuntime(
            catalog ?? new RadialVisualPackCatalog(),
            log);

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
        if (selectedSlot < 0) throw new ArgumentOutOfRangeException(nameof(selectedSlot));

        LayoutDefinition? boundLayout;
        lock (_stateLock)
        {
            boundLayout = _layoutDefinition;
        }
        LayoutDefinition? activeLayout = _visualPacks.Active?.LayoutDefinition ?? boundLayout;
        if (activeLayout != null) ValidateSelectedSlot(selectedSlot, activeLayout);

        RadialMenuRenderMetrics metrics = settings.CreateRenderMetrics();
        lock (_stateLock)
        {
            _selectedSlot = selectedSlot;
            _overlayVisible = true;
        }

        RunOnUiThread(() => ShowCore(screenPoint, settings, metrics, selectedSlot));
    }

    public new void Hide()
    {
        lock (_stateLock)
        {
            _overlayVisible = false;
        }

        RunOnUiThread(base.Hide);
    }

    public void PrepareVisualPack(RadialMenuSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RadialMenuRenderMetrics metrics = settings.CreateRenderMetrics();
        RunOnUiThread(() =>
        {
            RadialVisualPackSession? session = _visualPacks.Ensure(
                settings,
                metrics.CanvasSize);
            if (session == null && _visualPacks.Active == null)
                FailSafely(_visualPacks.LastError ?? "Radial visual pack is unavailable.");
            else if (session != null)
            {
                lock (_stateLock)
                {
                    _layoutDefinition = session.LayoutDefinition;
                }
            }
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        RadialVisualPackSession? session = _visualPacks.Active;
        if (session == null) return;

        int selectedSlot;
        lock (_stateLock)
        {
            selectedSlot = _selectedSlot;
        }
        DrawComposition(
            e.Graphics,
            session.AssetCache,
            session.DynamicContent,
            selectedSlot);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _visualPacks.Dispose();
        base.Dispose(disposing);
    }

    private void ShowCore(
        Point screenPoint,
        RadialMenuSettings settings,
        RadialMenuRenderMetrics metrics,
        int selectedSlot)
    {
        try
        {
            RadialVisualPackSession? session = _visualPacks.Ensure(
                settings,
                metrics.CanvasSize);
            if (session == null)
            {
                FailSafely(_visualPacks.LastError ?? "Radial visual pack is unavailable.");
                return;
            }

            LayoutDefinition layout = session.LayoutDefinition;
            ValidateSelectedSlot(selectedSlot, layout);
            lock (_stateLock)
            {
                _layoutDefinition = layout;
            }

            ClientSize = new Size(metrics.CanvasSize, metrics.CanvasSize);
            Location = new Point(
                screenPoint.X - (metrics.CanvasSize / 2),
                screenPoint.Y - (metrics.CanvasSize / 2));
            _ = Handle;
            RenderLayeredWindow(
                session.AssetCache,
                session.DynamicContent,
                selectedSlot);
            if (!Visible) base.Show();
        }
        catch (Exception exception) when (IsAssetOrRenderingException(exception))
        {
            FailSafely(exception.Message);
        }
    }

    private void FailSafely(string message)
    {
        if (!_assetErrorLogged)
        {
            _assetErrorLogged = true;
            Trace.TraceError($"[Radial visual pack] {message}");
        }

        lock (_stateLock)
        {
            _overlayVisible = false;
        }
        if (Visible) base.Hide();
    }

    private static void DrawComposition(
        Graphics graphics,
        RadialVisualPackCache cache,
        RadialDynamicContentCache dynamicContent,
        int selectedSlot)
    {
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(cache.ScaledBase, 0, 0);

        graphics.CompositingMode = CompositingMode.SourceOver;
        if (selectedSlot > 0)
            graphics.DrawImageUnscaled(cache.GetSelectedSlot(selectedSlot), 0, 0);

        graphics.DrawImageUnscaled(dynamicContent.Content, 0, 0);
    }

    private void RenderLayeredWindow(
        RadialVisualPackCache cache,
        RadialDynamicContentCache dynamicContent,
        int selectedSlot)
    {
        if (!IsHandleCreated || IsDisposed) return;

        using var bitmap = new Bitmap(
            cache.TargetSize,
            cache.TargetSize,
            PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            DrawComposition(graphics, cache, dynamicContent, selectedSlot);
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to acquire the screen DC.");

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmapHandle = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create a memory DC.");

            bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            previousBitmap = SelectObject(memoryDc, bitmapHandle);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to select the overlay bitmap.");

            var destination = new NativePoint(Left, Top);
            var source = new NativePoint(0, 0);
            var size = new NativeSize(cache.TargetSize, cache.TargetSize);
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
            if (memoryDc != IntPtr.Zero &&
                previousBitmap != IntPtr.Zero &&
                previousBitmap != new IntPtr(-1))
            {
                SelectObject(memoryDc, previousBitmap);
            }
            if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

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

    private static bool IsAssetException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or
        ArgumentException or ExternalException or OutOfMemoryException or NotSupportedException;

    private static bool IsAssetOrRenderingException(Exception exception) =>
        IsAssetException(exception) || exception is InvalidOperationException;

    internal static void ValidateSelectedSlot(int selectedSlot, LayoutDefinition layout)
    {
        if (selectedSlot < 0 || selectedSlot > layout.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(selectedSlot));
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
