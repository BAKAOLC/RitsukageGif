using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class CapturePointerMovement
    {
        internal static bool MoveMac(double x, double y)
        {
            return CgWarpMouseCursorPosition(new() { X = x, Y = y }) == 0;
        }

        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics",
            EntryPoint = "CGWarpMouseCursorPosition")]
        private static partial int CgWarpMouseCursorPosition(PortableMouseMacPoint point);
    }
}
