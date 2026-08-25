using System.Runtime.InteropServices;
using System.Text;

namespace Helyx.Shared
{
    internal sealed class Win32Window : IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32Window(IntPtr handle) => Handle = handle;
    }

    internal static class NativeMethods
    {
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

        internal static void UseOwnConsoleIcon()
        {
            var window = GetConsoleWindow();

            if (window == IntPtr.Zero)
                return;

            var name = new StringBuilder(64);

            if (GetClassName(window, name, name.Capacity) == 0 || name.ToString() != "ConsoleWindowClass")
                return;

            GetWindowThreadProcessId(window, out var owner);

            if (owner != GetCurrentProcessId())
                return;

            if (Environment.ProcessPath is not { } path)
                return;

            ExtractIconEx(path, 0, out var large, out var small, 1);

            if (large != IntPtr.Zero)
                SendMessage(window, WM_SETICON, ICON_BIG, large);

            if (small != IntPtr.Zero)
                SendMessage(window, WM_SETICON, ICON_SMALL, small);
        }
    }
}
