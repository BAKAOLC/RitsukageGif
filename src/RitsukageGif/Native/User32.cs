using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RitsukageGif.Native
{
    internal static class User32
    {
        private const string DllName = "user32.dll";

        [DllImport(DllName)]
        public extern static WindowStyles GetWindowLong(IntPtr hWnd, GetWindowLongValue nIndex);

        [DllImport(DllName)]
        public extern static bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport(DllName)]
        public extern static bool IsWindow(IntPtr hWnd);

        [DllImport(DllName)]
        public extern static IntPtr GetDesktopWindow();

        [DllImport(DllName)]
        public extern static IntPtr GetForegroundWindow();

        [DllImport(DllName)]
        public extern static bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport(DllName)]
        public extern static bool EnumChildWindows(IntPtr hWnd, EnumWindowsProc proc, IntPtr lParam);

        [DllImport(DllName)]
        public extern static int GetWindowText(IntPtr hWnd, [Out] StringBuilder lpString, int nMaxCount);

        [DllImport(DllName)]
        public extern static IntPtr GetWindow(IntPtr hWnd, GetWindowEnum uCmd);

        [DllImport(DllName)]
        public extern static int GetWindowTextLength(IntPtr hWnd);

        [DllImport(DllName)]
        public extern static bool IsWindowVisible(IntPtr hWnd);

        [DllImport(DllName)]
        public extern static bool IsIconic(IntPtr hWnd);

        [DllImport(DllName)]
        public extern static bool IsZoomed(IntPtr hWnd);

        [DllImport(DllName)]
        public extern static bool DestroyIcon(IntPtr hIcon);

        [DllImport(DllName)]
        public extern static IntPtr CopyIcon(IntPtr hIcon);

        [DllImport(DllName)]
        public extern static bool GetCursorInfo(ref CursorInfo pci);

        [DllImport(DllName)]
        public extern static bool GetIconInfo(IntPtr hIcon, out IconInfo piconinfo);
    }
}