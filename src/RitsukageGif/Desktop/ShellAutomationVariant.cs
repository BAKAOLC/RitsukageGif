using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct ShellAutomationVariant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal int Integer;
        [FieldOffset(8)] internal long Long;
        [FieldOffset(8)] internal nint Pointer;
    }
}
