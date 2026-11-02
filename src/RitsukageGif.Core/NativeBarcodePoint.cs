using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativeBarcodePoint
    {
        internal readonly int X;
        internal readonly int Y;
    }
}
