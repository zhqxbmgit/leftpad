using System.Runtime.InteropServices;

namespace PcDs4Server;

public sealed class WindowsCursorPositionProvider : ICursorPositionProvider
{
    public bool TryGetPosition(out ScreenPoint position, out int win32Error)
    {
        if (GetCursorPos(out NativePoint point))
        {
            position = new ScreenPoint(point.X, point.Y);
            win32Error = 0;
            return true;
        }

        position = default;
        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
