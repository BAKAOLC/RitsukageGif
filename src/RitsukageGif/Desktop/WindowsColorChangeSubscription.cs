using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class WindowsColorChangeSubscription : IDisposable
    {
        private nint _window;

        public WindowsColorChangeSubscription(nint window)
        {
            if (SetWindowSubclass(window, &WindowProcedure, 0x524743, 0) != 0) _window = window;
        }

        public void Dispose()
        {
            if (_window == 0) return;
            RemoveWindowSubclass(_window, &WindowProcedure, 0x524743);
            _window = 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
        {
            if (message is 0x1a or 0x7e) WindowsDisplayColor.Invalidate();
            return DefSubclassProc(window, message, wParam, lParam);
        }

        [LibraryImport("comctl32.dll")]
        private static partial int SetWindowSubclass(nint window,
            delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);

        [LibraryImport("comctl32.dll")]
        private static partial void RemoveWindowSubclass(nint window,
            delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id);

        [LibraryImport("comctl32.dll")]
        private static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    }
}
