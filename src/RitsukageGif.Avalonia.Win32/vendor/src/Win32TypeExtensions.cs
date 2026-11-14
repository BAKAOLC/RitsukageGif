using Avalonia.Win32.Interop;
using RECT = Windows.Win32.Foundation.RECT;

namespace Avalonia.Win32
{
    internal static class Win32TypeExtensions
    {
        public static PixelRect ToPixelRect(this RECT rect)
        {
            return new(rect.left, rect.top, rect.right - rect.left,
                rect.bottom - rect.top);
        }

        public static PixelRect ToPixelRect(this UnmanagedMethods.RECT rect)
        {
            return new(rect.left, rect.top, rect.right - rect.left,
                rect.bottom - rect.top);
        }
    }
}
