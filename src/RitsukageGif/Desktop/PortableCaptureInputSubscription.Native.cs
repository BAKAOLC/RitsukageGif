using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PortableCaptureInputSubscription
    {
        private static (bool Held, bool Cancelled) ReadInitialState()
        {
            return OperatingSystem.IsMacOS()
                ? (CGEventSourceButtonState(1, 0) != 0, CGEventSourceKeyState(1, 53) != 0)
                : X11WindowSystem.CaptureRefreshInputState();
        }

        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static partial byte CGEventSourceButtonState(int source, uint button);

        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static partial byte CGEventSourceKeyState(int source, ushort key);
    }
}
