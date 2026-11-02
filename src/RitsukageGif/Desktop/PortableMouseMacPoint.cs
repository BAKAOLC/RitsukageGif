using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PortableMouseMacPoint
    {
        internal double X;
        internal double Y;
    }
}
