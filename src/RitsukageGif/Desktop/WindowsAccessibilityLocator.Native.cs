using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static unsafe partial class WindowsAccessibilityLocator
    {
        private static readonly Guid AutomationClass = new("e22ad333-b25f-460c-83d0-0581107395c9");
        private static readonly Guid AutomationInterface = new("34723aff-0c9d-49d0-9896-7ab52df8cd8a");

        private static int Configure(nint automation, int slot, uint value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, int>)(*(nint**)automation)[slot])(automation, value);
        }

        private static int ReadObject(nint owner, int slot, out nint value)
        {
            value = 0;
            fixed (nint* result = &value)
                return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)owner)[slot])(owner, result);
        }

        private static int ReadObject(nint owner, int slot, nint argument, out nint value)
        {
            value = 0;
            fixed (nint* result = &value)
                return ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(nint**)owner)[slot])(owner, argument,
                    result);
        }

        private static void Release(nint value)
        {
            if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)value)[2])(value);
        }

        private static bool TryReadBounds(nint element, PixelRect clip, out PixelRect bounds)
        {
            bounds = default;
            int offscreen;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)element)[38])(element, &offscreen) < 0 ||
                offscreen != 0) return false;
            NativeRectangle rectangle;
            if (((delegate* unmanaged[Stdcall]<nint, NativeRectangle*, int>)(*(nint**)element)[43])(element,
                    &rectangle) < 0)
                return false;
            var width = (long)rectangle.Right - rectangle.Left;
            var height = (long)rectangle.Bottom - rectangle.Top;
            if (width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue) return false;
            bounds = new PixelRect(rectangle.Left, rectangle.Top, (int)width, (int)height).Intersect(clip);
            return !bounds.IsEmpty;
        }

        private static nint ReadWindow(nint element)
        {
            nint window;
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)element)[36])(element, &window) >= 0
                ? window
                : 0;
        }

        [LibraryImport("ole32.dll")]
        private static partial int CoInitializeEx(nint reserved, uint flags);

        [LibraryImport("ole32.dll")]
        private static partial void CoUninitialize();

        [LibraryImport("ole32.dll")]
        private static partial int CoCreateInstance(in Guid classId, nint outer, uint context,
            in Guid interfaceId, out nint instance);

        [LibraryImport("user32.dll")]
        private static partial nint SetThreadDpiAwarenessContext(nint context);

        [LibraryImport("user32.dll")]
        private static partial int IsWindowVisible(nint window);

        [LibraryImport("user32.dll")]
        private static partial int IsIconic(nint window);

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    }
}
