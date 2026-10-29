using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private static void SetBorderInputTransparent(nint window)
        {
            var style = GetBorderWindowStyle(window, -20);
            Marshal.SetLastPInvokeError(0);
            if (SetBorderWindowStyle(window, -20, style | 0x08000000 | 0x80 | 0x20) == 0 &&
                Marshal.GetLastPInvokeError() != 0)
                Trace.TraceWarning("Scrolling border input style: {0}", Marshal.GetLastPInvokeError());
        }

        private static void ClipBorder(nint window, int width, int height)
        {
            var region = CreateBorderRegion(0, 0, width, height);
            if (region == 0) return;
            if (SetBorderRegion(window, region, 1) != 0) return;
            _ = DeleteBorderRegion(region);
            Trace.TraceWarning("Scrolling border region could not be applied.");
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial nint GetBorderWindowStyle(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static partial nint SetBorderWindowStyle(nint window, int index, nint value);

        [LibraryImport("gdi32.dll", EntryPoint = "CreateRectRgn")]
        private static partial nint CreateBorderRegion(int left, int top, int right, int bottom);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowRgn")]
        private static partial int SetBorderRegion(nint window, nint region, int redraw);

        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteBorderRegion(nint region);
    }
}
