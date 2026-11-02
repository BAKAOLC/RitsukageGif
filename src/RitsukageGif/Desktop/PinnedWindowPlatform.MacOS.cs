using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedWindowPlatform
    {
        [SupportedOSPlatform("macos")]
        private static nint GetMacWindow(Window window)
        {
            var handle = window.TryGetPlatformHandle();
            return handle is IMacOSTopLevelPlatformHandle mac ? mac.NSWindow :
                handle?.HandleDescriptor == "NSWindow" ? handle.Handle : 0;
        }

        [SupportedOSPlatform("macos")]
        private static void SetMacMouseThrough(Window window, bool enabled, ref nint originalStyle)
        {
            var handle = GetMacWindow(window);
            if (handle == 0) throw new PlatformNotSupportedException();
            if (enabled) originalStyle = GetIgnoresMouseEvents(handle, RegisterSelector("ignoresMouseEvents"));
            SetIgnoresMouseEvents(handle, RegisterSelector("setIgnoresMouseEvents:"),
                enabled ? (byte)1 : (byte)originalStyle);
        }

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint RegisterSelector(string name);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial byte GetIgnoresMouseEvents(nint target, nint selector);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial void SetIgnoresMouseEvents(nint target, nint selector, byte value);
    }
}
