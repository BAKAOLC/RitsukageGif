using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct FileIconShellInfo
    {
        internal nint Icon;
        internal int Index;
        internal uint Attributes;
        internal fixed char DisplayName[260];
        internal fixed char TypeName[80];
    }
}
