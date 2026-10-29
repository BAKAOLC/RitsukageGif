using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct PasteInputData
    {
        [FieldOffset(0)] internal PasteKeyboardInput Keyboard;
        [FieldOffset(0)] internal PasteMouseInput Mouse;
    }
}
