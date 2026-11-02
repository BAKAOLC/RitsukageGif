using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ForegroundHotkeyCallback(nint hook, uint eventType, nint window, int objectId,
        int childId, uint threadId, uint time);
}
