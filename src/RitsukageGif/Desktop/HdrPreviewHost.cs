using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class HdrPreviewHost : NativeControlHost, IDisposable
    {
        private static readonly ConcurrentDictionary<nuint, WeakReference<HdrPreviewHost>> Subscriptions = new();
        private static long _nextSubscription;
        private bool _failed;
        private PreviewFrame? _frame;
        private nint _monitor;
        private WindowsHdrPresenter? _presenter;
        private bool _refreshPending, _disposed;
        private nuint _subscription;
        private nint _window, _parent;

        public HdrPreviewHost()
        {
            IsHitTestVisible = false;
            IsVisible = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            IsVisible = false;
            _disposed = true;
            _frame?.Dispose();
            _frame = null;
            DestroyWindows();
        }

        public event Action<bool>? PresentationChanged;

        private void SetPresentation(bool visible)
        {
            if (IsVisible == visible) return;
            IsVisible = visible;
            PresentationChanged?.Invoke(visible);
        }

        public void SetFrame(PreviewFrame? frame)
        {
            var previous = _frame;
            _frame = frame?.HdrPixels is not null ? frame.Retain() : null;
            previous?.Dispose();
            Refresh();
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (!OperatingSystem.IsWindows()) return base.CreateNativeControlCore(parent);
            _parent = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? parent.Handle;
            _window = CreateWindowEx(0, "STATIC", "", 0x40000000 | 0x04000000, 0, 0, 1, 1, parent.Handle, 0, 0, 0);
            if (_window == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var data = (nuint)Interlocked.Increment(ref _nextSubscription);
            _subscription = data;
            Subscriptions[data] = new(this);
            if (SetWindowSubclass(_window, &WindowProcedure, data, data) == 0 ||
                SetWindowSubclass(_parent, &WindowProcedure, data, data) == 0)
            {
                DestroyWindows();
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            QueueRefresh();
            return new PlatformHandle(_window, "HWND");
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (OperatingSystem.IsWindows()) DestroyWindows();
            else base.DestroyNativeControlCore(control);
        }

        private void DestroyWindows()
        {
            _presenter?.Dispose();
            _presenter = null;
            if (_subscription != 0)
            {
                Subscriptions.TryRemove(_subscription, out _);
                if (_parent != 0 && RemoveWindowSubclass(_parent, &WindowProcedure, _subscription) == 0 &&
                    IsWindow(_parent) != 0)
                    Trace.TraceWarning("Unable to remove HDR preview parent subscription.");
                if (_window != 0 && RemoveWindowSubclass(_window, &WindowProcedure, _subscription) == 0 &&
                    IsWindow(_window) != 0)
                    Trace.TraceWarning("Unable to remove HDR preview window subscription.");
                _subscription = 0;
            }

            if (_window != 0 && DestroyWindow(_window) == 0 && IsWindow(_window) != 0)
                Trace.TraceWarning("Unable to destroy HDR preview window.");
            _window = _parent = 0;
            _monitor = 0;
        }

        private void QueueRefresh()
        {
            if (_refreshPending || _disposed) return;
            _refreshPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _refreshPending = false;
                if (!_disposed) Refresh();
            }, DispatcherPriority.Render);
        }

        private void Refresh()
        {
            if (_disposed) return;
            if (_window == 0 || _frame?.HdrPixels is not { } pixels || !OperatingSystem.IsWindows())
            {
                SetPresentation(false);
                return;
            }

            var monitor = WindowsDisplayColor.ForWindow(_parent);
            if (_monitor != monitor)
            {
                _monitor = monitor;
                _failed = false;
                _presenter?.Dispose();
                _presenter = null;
            }

            if (!WindowsDisplayColor.Read(monitor).Hdr || _failed)
            {
                SetPresentation(false);
                return;
            }

            try
            {
                _presenter ??= new(_window, _frame.Width, _frame.Height);
                _presenter.Present(pixels.AsSpan(0, checked(_frame.Width * _frame.Height * 8)), _frame.Width,
                    _frame.Height);
                SetPresentation(true);
            }
            catch (Exception exception) when (exception is ExternalException or NotSupportedException)
            {
                _presenter?.Dispose();
                _presenter = null;
                _failed = true;
                SetPresentation(false);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
        {
            if (Subscriptions.TryGetValue(data, out var reference) && reference.TryGetTarget(out var host))
            {
                if (window == host._window && message == 0x84) return -1;
                if (message is 0x1a or 0x7e or 0x320)
                {
                    WindowsDisplayColor.Invalidate();
                    host._failed = false;
                    host.QueueRefresh();
                }
                else if (window == host._parent && message is 0x47 or 0x2e0)
                {
                    host._failed = false;
                    host.QueueRefresh();
                }
            }

            return DefSubclassProc(window, message, wParam, lParam);
        }

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16,
            SetLastError = true)]
        private static partial nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

        [LibraryImport("user32.dll")]
        private static partial int DestroyWindow(nint window);

        [LibraryImport("user32.dll")]
        private static partial int IsWindow(nint window);

        [LibraryImport("comctl32.dll")]
        private static partial int SetWindowSubclass(nint window,
            delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);

        [LibraryImport("comctl32.dll")]
        private static partial int RemoveWindowSubclass(nint window,
            delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id);

        [LibraryImport("comctl32.dll")]
        private static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    }
}
