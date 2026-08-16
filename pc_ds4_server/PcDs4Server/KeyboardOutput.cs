using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PcDs4Server;

public enum KeyboardKey : ushort
{
    None = 0x00,
    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,
    Space = 0x20,
    Enter = 0x0D,
    Escape = 0x1B,
    Tab = 0x09,
    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,
    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B,
    LeftWin = 0x5B,
    LeftShift = 0xA0,
    LeftControl = 0xA2,
    LeftAlt = 0xA4
}

public static class KeyboardKeyCatalog
{
    public static IReadOnlyList<KeyboardKey> MainKeys { get; } =
        Enum.GetValues<KeyboardKey>()
            .Where(IsMainKey)
            .ToArray();

    public static bool IsMainKey(KeyboardKey key) =>
        Enum.IsDefined(key) && key is not (
            KeyboardKey.None or
            KeyboardKey.LeftWin or
            KeyboardKey.LeftShift or
            KeyboardKey.LeftControl or
            KeyboardKey.LeftAlt);
}

public interface IKeyboardOutput
{
    void SetKeyState(KeyboardKey key, bool isPressed);
}

public sealed class SendInputKeyboardOutput : IKeyboardOutput
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    public void SetKeyState(KeyboardKey key, bool isPressed)
    {
        var input = new INPUT
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = (ushort)key,
                    Flags = isPressed ? 0u : KeyEventKeyUp
                }
            }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"SendInput failed for {key} {(isPressed ? "down" : "up")}.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}

public sealed class KeyboardOutputException : Exception
{
    public KeyboardOutputException(string message, Exception innerException)
        : base(message, innerException) { }
}
