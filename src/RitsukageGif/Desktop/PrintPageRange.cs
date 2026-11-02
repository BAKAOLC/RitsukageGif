using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PrintPageRange
    {
        internal uint From;
        internal uint To;
    }
}
