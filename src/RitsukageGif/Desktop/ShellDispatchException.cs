using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShellDispatchException
    {
        internal ushort Code;
        internal ushort Reserved;
        internal nint Source;
        internal nint Description;
        internal nint HelpFile;
        internal uint HelpContext;
        internal nint ReservedPointer;
        internal nint DeferredFill;
        internal int ErrorCode;
    }
}
