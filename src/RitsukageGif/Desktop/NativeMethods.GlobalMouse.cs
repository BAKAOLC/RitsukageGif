using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        internal static partial nint SetMouseHook(int hook, nint callback, nint module, uint thread);

        [LibraryImport("user32.dll", EntryPoint = "UnhookWindowsHookEx", SetLastError = true)]
        internal static partial int RemoveMouseHook(nint hook);

        [LibraryImport("user32.dll")]
        internal static partial nint CallNextHookEx(nint hook, int code, nuint message, nint data);

        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
        internal static partial nint MouseHookModule(nint name);

        [LibraryImport("user32.dll")]
        internal static partial int GetSystemMetrics(int index);
    }
}
