using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CutTool.Native
{
    internal static class NativeMethods
    {
        internal const int WM_HOTKEY = 0x0312;
        internal const int WM_APP_CAPTURE = 0x8001;
        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_SHIFT = 0x0004;
        internal const uint MOD_WIN = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        internal static void EnablePerMonitorDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); }
            catch { }
        }

        internal static Rectangle GetForegroundWindowBounds()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return Rectangle.Empty;

            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                return Rectangle.Empty;

            RECT rect;
            if (!GetWindowRect(window, out rect)) return Rectangle.Empty;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return Rectangle.Empty;
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }
}
