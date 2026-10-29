using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GlobalMouseHookCallback(int code, nuint message, nint data);
}
