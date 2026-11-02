using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativeBarcodePosition
    {
        internal readonly NativeBarcodePoint TopLeft;
        internal readonly NativeBarcodePoint TopRight;
        internal readonly NativeBarcodePoint BottomRight;
        internal readonly NativeBarcodePoint BottomLeft;
    }
}
