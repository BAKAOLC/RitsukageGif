using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    internal static partial class User32
    {
        private const string DllName = "user32.dll";

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowRect(IntPtr hWnd, out RectStruct rect);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindow(IntPtr hWnd);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EnumChildWindows(IntPtr hWnd, EnumWindowsProc proc, IntPtr lParam);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(IntPtr hWnd);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DestroyIcon(IntPtr hIcon);

        [LibraryImport(DllName)]
        public static partial IntPtr CopyIcon(IntPtr hIcon);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetCursorInfo(ref CursorInfo pCursorInfo);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetIconInfo(IntPtr hIcon, out IconInfo pIconInfo);

        [LibraryImport(DllName)]
        public static partial IntPtr MonitorFromPoint(PointStruct pt, uint dwFlags);
    }
}