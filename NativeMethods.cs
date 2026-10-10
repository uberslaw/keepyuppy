using System.Runtime.InteropServices;
using FormsCursor = System.Windows.Forms.Cursor;

namespace KeepyUppy;

internal static class NativeMethods
{
    private const int InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    public static int InputSize => Marshal.SizeOf<INPUT>();

    public static bool NudgeMouse()
    {
        var before = FormsCursor.Position;
        var inputs = new[]
        {
            Move(1, 0),
            Move(-1, 0)
        };

        var sent = SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent != inputs.Length)
        {
            AppLog.Write($"SendInput returned {sent}, win32={Marshal.GetLastWin32Error()}, inputSize={InputSize}");
            return false;
        }

        var after = FormsCursor.Position;
        if (Math.Abs(after.X - before.X) > 2 || Math.Abs(after.Y - before.Y) > 2)
            FormsCursor.Position = before;

        return true;
    }

    public static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }

    public static bool SetAwakeExecutionState()
    {
        var result = SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
        if (result == 0)
            AppLog.Write("SetThreadExecutionState failed, win32=" + Marshal.GetLastWin32Error());

        return result != 0;
    }

    public static void ClearExecutionState()
    {
        SetThreadExecutionState(EsContinuous);
    }

    private static INPUT Move(int dx, int dy) => new()
    {
        type = InputMouse,
        mi = new MOUSEINPUT
        {
            dx = dx,
            dy = dy,
            dwFlags = MouseEventMove
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}
