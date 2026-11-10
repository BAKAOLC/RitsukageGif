using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct X11PollDescriptor
    {
        internal int FileDescriptor;
        internal short Events;
        internal short ReturnedEvents;
    }
}
