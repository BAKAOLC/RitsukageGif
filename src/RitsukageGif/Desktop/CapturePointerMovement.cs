using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class CapturePointerMovement
    {
        internal static bool Move(PixelPoint point)
        {
            if (OperatingSystem.IsWindows()) return SetCursorPos(point.X, point.Y) != 0;
            if (!OperatingSystem.IsLinux() ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return false;
            var display = XOpenDisplay(0);
            if (display == 0) return false;
            try
            {
#pragma warning disable CA1806
                XWarpPointer(display, 0, XDefaultRootWindow(display), 0, 0, 0, 0, point.X, point.Y);
                XFlush(display);
#pragma warning restore CA1806
                return true;
            }
            finally
            {
#pragma warning disable CA1806
                XCloseDisplay(display);
#pragma warning restore CA1806
            }
        }

        [LibraryImport("user32.dll")]
        private static partial int SetCursorPos(int x, int y);

        [LibraryImport("libX11.so.6")]
        private static partial nint XOpenDisplay(nint name);

        [LibraryImport("libX11.so.6")]
        private static partial nint XDefaultRootWindow(nint display);

        [LibraryImport("libX11.so.6")]
        // ReSharper disable once UnusedMethodReturnValue.Local
        private static partial int XWarpPointer(nint display, nint source, nint destination, int sourceX,
            int sourceY, uint sourceWidth, uint sourceHeight, int destinationX, int destinationY);

        [LibraryImport("libX11.so.6")]
        // ReSharper disable once UnusedMethodReturnValue.Local
        private static partial int XFlush(nint display);

        [LibraryImport("libX11.so.6")]
        // ReSharper disable once UnusedMethodReturnValue.Local
        private static partial int XCloseDisplay(nint display);
    }
}
