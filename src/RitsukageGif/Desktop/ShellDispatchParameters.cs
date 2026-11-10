using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShellDispatchParameters
    {
        internal nint Arguments;
        internal nint NamedArguments;
        internal uint ArgumentCount;
        internal uint NamedArgumentCount;
    }
}
