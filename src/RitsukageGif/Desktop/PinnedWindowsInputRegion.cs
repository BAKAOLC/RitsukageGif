using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowsInputRegion(nint window) : IPinnedInputRegionBackend
    {
        private PinnedInputRegion? _applied;
        private bool _disposed;

        public bool ClipsVisualOutput => true;

        public void Apply(PinnedInputRegion coverage)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_applied?.HasSameShape(coverage) == true) return;
            var native = Create(coverage);
            var previous = _applied;
            _applied = coverage;
            if (SetWindowRgn(window, native, 1) != 0) return;
            var error = Marshal.GetLastPInvokeError();
            _applied = previous;
            _ = DeleteObject(native);
            throw new Win32Exception(error);
        }

        public void Reset()
        {
            if (_applied is null) return;
            var previous = _applied;
            _applied = null;
            if (IsWindow(window) == 0 || SetWindowRgn(window, 0, 1) != 0) return;
            _applied = previous;
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        public void Dispose()
        {
            if (_disposed) return;
            Reset();
            _disposed = true;
        }

        private static unsafe nint Create(PinnedInputRegion region)
        {
            nint native;
            if (region.Rectangles.IsEmpty)
            {
                native = CreateRectRgn(0, 0, 0, 0);
            }
            else
            {
                var data = PinnedInputRegionData.Pack(region);
                fixed (byte* pointer = data) native = ExtCreateRegion(0, (uint)data.Length, pointer);
            }

            if (native == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            return native;
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int SetWindowRgn(nint window, nint region, int redraw);

        [LibraryImport("user32.dll")]
        private static partial int IsWindow(nint window);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static unsafe partial nint ExtCreateRegion(nint transform, uint size, byte* data);

        // ReSharper disable once UnusedMethodReturnValue.Local
        [LibraryImport("gdi32.dll")]
        private static partial int DeleteObject(nint handle);
    }
}
