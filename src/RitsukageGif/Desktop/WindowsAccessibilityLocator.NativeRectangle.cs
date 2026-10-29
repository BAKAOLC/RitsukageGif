using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowsAccessibilityLocator
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
