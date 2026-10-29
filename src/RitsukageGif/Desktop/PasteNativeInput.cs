using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PasteNativeInput
    {
        internal uint Type;
        internal PasteInputData Data;
    }
}
