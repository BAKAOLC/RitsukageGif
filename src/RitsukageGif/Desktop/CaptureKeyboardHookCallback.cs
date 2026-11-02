using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint CaptureKeyboardHookCallback(int code, nuint message, nint data);
}
