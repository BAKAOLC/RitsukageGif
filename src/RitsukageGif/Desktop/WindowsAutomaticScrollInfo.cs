using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowsAutomaticScrollInfo
    {
        internal uint Size;
        internal uint Mask;
        internal int Minimum;
        internal int Maximum;
        internal uint Page;
        internal int Position;
        internal int TrackPosition;
    }
}
