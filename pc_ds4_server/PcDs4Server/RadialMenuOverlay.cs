using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PcDs4Server;

public sealed class RadialMenuOverlay : Form, IRadialMenuOverlay, IRadialLayoutProvider,
    IRadialDpiProvider
{
    private const byte AcSrcAlpha = 0x01;
    private const byte AcSrcOver = 0x00;
    private const int UlwAlpha = 0x00000002;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private readonly object _stateLock = new();
    private readonly RadialVisualPackRuntime _visualPacks;
    private readonly Action<string> _log;
    private LayoutDefinition? _layoutDefinition;
    private RadialMenuSettings? _lastSettings;
    private Point? _lastScreenPoint;
    private DpiDiagnosticKey? _lastDpiDiagnostic;
    private int _selectedSlot;
    private int _activeDpi = RadialDpiScaling.DefaultDpi;
    private bool _overlayVisible;
    private bool _assetErrorLogged;

    public RadialMenuOverlay(
        RadialVisualPackCatalog? catalog = null,
        Action<string>? log = null)
    {
        _log = log ?? (message => Trace.TraceInformation(message));
        _visualPacks = new RadialVisualPackRuntime(
            catalog ?? new RadialVisualPackCatalog(),
            _log);

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
            _lastScreenPoint = screenPoint;
            _lastSettings = settings with { };
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
            int dpi = GetCurrentWindowDpi();
            int physicalSize = RadialDpiScaling.ToPhysicalPixels(metrics.CanvasSize, dpi);
            RadialVisualPackSession? session = _visualPacks.Ensure(
                settings,
                physicalSize);
            if (session == null && _visualPacks.Active == null)
                FailSafely(_visualPacks.LastError ?? "Radial visual pack is unavailable.");
            else if (session != null)
            {
                lock (_stateLock)
                {
                    _layoutDefinition = session.LayoutDefinition;
                    _activeDpi = dpi;
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

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        Point? screenPoint;
        RadialMenuSettings? settings;
        int selectedSlot;
        bool overlayVisible;
        lock (_stateLock)
        {
            _activeDpi = e.DeviceDpiNew;
            screenPoint = _lastScreenPoint;
            settings = _lastSettings;
            selectedSlot = _selectedSlot;
            overlayVisible = _overlayVisible;
        }

        if (!overlayVisible || screenPoint == null || settings == null) return;
        BeginInvoke(() => ShowCore(
            screenPoint.Value,
            settings,
            settings.CreateRenderMetrics(),
            selectedSlot));
    }

    private void ShowCore(
        Point screenPoint,
        RadialMenuSettings settings,
        RadialMenuRenderMetrics metrics,
        int selectedSlot)
    {
        try
        {
            _ = Handle;
            int dpi = GetDpiAt(screenPoint);
            int physicalSize = RadialDpiScaling.ToPhysicalPixels(metrics.CanvasSize, dpi);
            Point topLeft = RadialDpiScaling.CenterAt(screenPoint, physicalSize);
            ClientSize = new Size(physicalSize, physicalSize);
            Location = topLeft;

            int windowDpi = GetCurrentWindowDpi();
            if (windowDpi != dpi)
            {
                dpi = windowDpi;
                physicalSize = RadialDpiScaling.ToPhysicalPixels(metrics.CanvasSize, dpi);
                topLeft = RadialDpiScaling.CenterAt(screenPoint, physicalSize);
                ClientSize = new Size(physicalSize, physicalSize);
                Location = topLeft;
            }

            RadialVisualPackSession? session = _visualPacks.Ensure(
                settings,
                physicalSize);
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
                _activeDpi = dpi;
            }

            RenderLayeredWindow(
                session.AssetCache,
                session.DynamicContent,
                selectedSlot,
                settings,
                metrics,
                screenPoint,
                dpi,
                topLeft);
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
        int selectedSlot,
        RadialMenuSettings settings,
        RadialMenuRenderMetrics metrics,
        Point screenPoint,
        int dpi,
        Point topLeft)
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

            LogDpiDiagnostics(
                cache,
                dynamicContent,
                bitmap,
                settings,
                metrics,
                screenPoint,
                dpi,
                topLeft,
                size);
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

    public int ActiveDpi
    {
        get
        {
            lock (_stateLock)
            {
                return _activeDpi;
            }
        }
    }

    public LayoutDefinition? ActiveLayoutDefinition
    {
        get
        {
            lock (_stateLock)
            {
                return _visualPacks.Active?.LayoutDefinition ?? _layoutDefinition;
            }
        }
    }

    private void LogDpiDiagnostics(
        RadialVisualPackCache cache,
        RadialDynamicContentCache dynamicContent,
        Bitmap composition,
        RadialMenuSettings settings,
        RadialMenuRenderMetrics metrics,
        Point screenPoint,
        int dpi,
        Point topLeft,
        NativeSize updateSize)
    {
        var key = new DpiDiagnosticKey(
            cache.Plan.ThemeId,
            settings.ScalePercent,
            metrics.CanvasSize,
            cache.TargetSize,
            dpi);
        lock (_stateLock)
        {
            if (_lastDpiDiagnostic == key) return;
            _lastDpiDiagnostic = key;
        }

        Rectangle windowBounds = GetPhysicalWindowBounds(Handle);
        uint windowDpi = GetDpiForWindow(Handle);
        Size selectedSize = cache.GetSelectedSlot(1).Size;
        Point cursor = Cursor.Position;
        var physicalCenter = new Point(
            topLeft.X + (updateSize.Width / 2),
            topLeft.Y + (updateSize.Height / 2));
        bool pixelExact =
            composition.Size == cache.ScaledBase.Size &&
            composition.Size == selectedSize &&
            composition.Size == dynamicContent.Content.Size &&
            composition.Width == updateSize.Width &&
            composition.Height == updateSize.Height &&
            windowBounds.Size == composition.Size;

        _log(
            $"[环形菜单 DPI] pack={cache.Plan.ThemeId}; " +
            $"master={cache.Plan.LayoutDefinition.Canvas.Width}x" +
            $"{cache.Plan.LayoutDefinition.Canvas.Height}; " +
            $"scalePercent={settings.ScalePercent}; logicalTarget={metrics.CanvasSize}x" +
            $"{metrics.CanvasSize}; dpi={dpi}; deviceDpi={DeviceDpi}; " +
            $"windowDpi={windowDpi}; dpiScale={RadialDpiScaling.GetScale(dpi):0.###}; " +
            $"base={cache.ScaledBase.Width}x{cache.ScaledBase.Height}; " +
            $"selected={selectedSize.Width}x{selectedSize.Height}; " +
            $"dynamic={dynamicContent.Content.Width}x{dynamicContent.Content.Height}; " +
            $"final={composition.Width}x{composition.Height}; " +
            $"updateSize={updateSize.Width}x{updateSize.Height}; " +
            $"hwndBounds={windowBounds.Left},{windowBounds.Top}," +
            $"{windowBounds.Width}x{windowBounds.Height}; " +
            $"cursor={cursor.X},{cursor.Y}; anchor={screenPoint.X},{screenPoint.Y}; " +
            $"topLeft={topLeft.X},{topLeft.Y}; " +
            $"physicalCenter={physicalCenter.X},{physicalCenter.Y}; pixel1to1={pixelExact}");
    }

    private int GetCurrentWindowDpi()
    {
        _ = Handle;
        uint dpi = GetDpiForWindow(Handle);
        if (dpi > 0) return checked((int)dpi);
        dpi = GetDpiForSystem();
        return dpi > 0 ? checked((int)dpi) : RadialDpiScaling.DefaultDpi;
    }

    private static int GetDpiAt(Point point)
    {
        try
        {
            IntPtr monitor = MonitorFromPoint(
                new NativePoint(point.X, point.Y),
                MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, MonitorDpiType.Effective, out uint dpiX, out _) >= 0 &&
                dpiX > 0)
            {
                return checked((int)dpiX);
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Fall through to the process DPI when the monitor API is unavailable.
        }

        uint systemDpi = GetDpiForSystem();
        return systemDpi > 0
            ? checked((int)systemDpi)
            : RadialDpiScaling.DefaultDpi;
    }

    private static Rectangle GetPhysicalWindowBounds(IntPtr window)
    {
        if (!GetWindowRect(window, out NativeRect bounds)) return Rectangle.Empty;
        return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
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
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        MonitorDpiType dpiType,
        out uint dpiX,
        out uint dpiY);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum MonitorDpiType
    {
        Effective = 0
    }

    private readonly record struct DpiDiagnosticKey(
        string PackId,
        int ScalePercent,
        int LogicalSize,
        int PhysicalSize,
        int Dpi);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}
