using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PinnedPointerPosition
    {
        internal readonly double X;
        internal readonly double Y;
    }
}
