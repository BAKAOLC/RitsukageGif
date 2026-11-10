using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class RecordingPointerOwnership
    {
        internal static bool Owns(nint handle, IReadOnlyList<WindowTarget> windows, double cursorX, double cursorY)
        {
            if (handle == 0) return true;
            if (DesktopCapture.UsesSourceSelection) return handle == -1;
            if (OperatingSystem.IsWindows())
            {
                var native = WindowFromPoint(new() { X = (int)Math.Floor(cursorX), Y = (int)Math.Floor(cursorY) });
                return native == handle || GetAncestor(native, 2) == handle;
            }

            return WindowLocator.CandidateAtPoint(windows,
                new((int)Math.Floor(cursorX), (int)Math.Floor(cursorY)))?.Handle == handle;
        }

        [LibraryImport("user32.dll")]
        private static partial nint WindowFromPoint(NativeMethods.Point point);

        [LibraryImport("user32.dll")]
        private static partial nint GetAncestor(nint window, uint flags);
    }
}
