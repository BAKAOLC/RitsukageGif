using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PasteMouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }
}
