using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PasteFocusWindowInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint Active;
        internal nint Focus;
        internal nint Capture;
        internal nint MenuOwner;
        internal nint MoveSize;
        internal nint Caret;
        internal int CaretLeft;
        internal int CaretTop;
        internal int CaretRight;
        internal int CaretBottom;
    }
}
