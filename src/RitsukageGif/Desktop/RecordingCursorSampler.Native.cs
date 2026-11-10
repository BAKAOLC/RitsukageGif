using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingCursorSampler
    {
        private static int MacButtons()
        {
            return (CGEventSourceButtonState(0, 0) ? 1 : 0) |
                   (CGEventSourceButtonState(0, 1) ? 2 : 0) |
                   (CGEventSourceButtonState(0, 2) ? 4 : 0);
        }

        // ReSharper disable once InconsistentNaming
        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool CGEventSourceButtonState(int state, uint button);
    }
}
