using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileIconMacRect
    {
        internal double X;
        internal double Y;
        internal double Width;
        internal double Height;
    }
}
