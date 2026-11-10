using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RecordingRunLoopSourceContext
    {
        internal nint Version;
        internal nint Info;
        internal nint Retain;
        internal nint Release;
        internal nint CopyDescription;
        internal nint Equal;
        internal nint Hash;
        internal nint Schedule;
        internal nint Cancel;
        internal nint Perform;
    }
}
