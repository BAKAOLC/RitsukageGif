using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedGroupPointer
    {
        internal static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
                                            (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection);

        internal static Avalonia.PixelPoint? Read()
        {
            if (OperatingSystem.IsWindows())
            {
                if (NativeMethods.GetCursorPos(out var point) == 0) return null;
                return new(point.X, point.Y);
            }

            if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                return X11WindowSystem.CurrentPointer();
            if (!OperatingSystem.IsMacOS()) return null;
            var input = CGEventCreate(0);
            if (input == 0) return null;
            try
            {
                var point = CGEventGetLocation(input);
                return new((int)Math.Round(point.X), (int)Math.Round(point.Y));
            }
            finally
            {
                CFRelease(input);
            }
        }

        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static partial nint CGEventCreate(nint source);

        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static partial PinnedPointerPosition CGEventGetLocation(nint input);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static partial void CFRelease(nint value);
    }
}
