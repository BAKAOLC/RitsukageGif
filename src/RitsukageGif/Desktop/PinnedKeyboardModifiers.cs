using System.Runtime.InteropServices;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedKeyboardModifiers
    {
        internal static KeyModifiers Read()
        {
            if (OperatingSystem.IsWindows())
            {
                var modifiers = KeyModifiers.None;
                if (NativeMethods.GetAsyncKeyState(0x11) < 0) modifiers |= KeyModifiers.Control;
                if (NativeMethods.GetAsyncKeyState(0x10) < 0) modifiers |= KeyModifiers.Shift;
                if (NativeMethods.GetAsyncKeyState(0x12) < 0) modifiers |= KeyModifiers.Alt;
                if (NativeMethods.GetAsyncKeyState(0x5b) < 0 || NativeMethods.GetAsyncKeyState(0x5c) < 0)
                    modifiers |= KeyModifiers.Meta;
                return modifiers;
            }

            if (OperatingSystem.IsMacOS())
            {
                var flags = CGEventSourceFlagsState(0);
                return ((flags & (1UL << 17)) != 0 ? KeyModifiers.Shift : 0) |
                       ((flags & (1UL << 18)) != 0 ? KeyModifiers.Control : 0) |
                       ((flags & (1UL << 19)) != 0 ? KeyModifiers.Alt : 0) |
                       ((flags & (1UL << 20)) != 0 ? KeyModifiers.Meta : 0);
            }

            return OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection
                ? X11WindowSystem.CurrentModifiers()
                : KeyModifiers.None;
        }

        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static partial ulong CGEventSourceFlagsState(int sourceState);
    }
}
