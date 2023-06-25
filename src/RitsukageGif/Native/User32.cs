using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RitsukageGif.Native
{
    internal static class User32
    {
        private const string DllName = "user32.dll";

        [DllImport(DllName)]
        public static extern WindowStyles GetWindowLong(IntPtr hWnd, GetWindowLongValue nIndex);

        [DllImport(DllName)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport(DllName)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport(DllName)]
        public static extern IntPtr GetDesktopWindow();

        [DllImport(DllName)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport(DllName)]
        public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport(DllName)]
        public static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowsProc proc, IntPtr lParam);

        [DllImport(DllName)]
        public static extern int GetWindowText(IntPtr hWnd, [Out] StringBuilder lpString, int nMaxCount);

        [DllImport(DllName)]
        public static extern IntPtr GetWindow(IntPtr hWnd, GetWindowEnum uCmd);

        [DllImport(DllName)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport(DllName)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport(DllName)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport(DllName)]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport(DllName)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport(DllName)]
        public static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport(DllName)]
        public static extern bool GetCursorInfo(ref CursorInfo pci);

        [DllImport(DllName)]
        public static extern bool GetIconInfo(IntPtr hIcon, out IconInfo piconinfo);
    }
}