using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        internal static unsafe partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles,
            uint milliseconds, uint wakeMask, uint flags);
    }
}
