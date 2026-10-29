using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class HdrOverlayWindow : IDisposable
    {
        private static readonly ConcurrentDictionary<nuint, WeakReference<HdrOverlayWindow>> Hosts = new();
        private static long _nextId;
        private readonly PixelRect _bounds;
        private readonly nuint _id;
        private readonly nint _overlay;
        private readonly Lock _renderLock = new();
        private PixelRect? _appliedClip;
        private PixelRect _appliedPatchBounds;
        private bool _attached;
        private volatile bool _disposed;
        private PixelRect _patchBounds;
        private CapturedFrame? _presentedFrame;
        private WindowsHdrPresenter? _presenter, _patchPresenter;
        private nint _previousOwner;
        private int _resetDevice;
        private nint _window, _patchWindow;

        public HdrOverlayWindow(nint overlay, PixelRect bounds)
        {
            Dispatcher.UIThread.VerifyAccess();
            _id = (nuint)Interlocked.Increment(ref _nextId);
            _overlay = overlay;
            _bounds = bounds;
            _window = CreateWindowEx(0x08000080, "STATIC", "", 0x80000000,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, 0, 0, 0, 0);
            if (_window == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (SetWindowSubclass(_window, &WindowProcedure, 1, 0) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                DestroyNativeWindow(_window);
                _window = 0;
                throw new Win32Exception(error);
            }

            Hosts[_id] = new(this);
            if (SetWindowSubclass(_overlay, &WindowProcedure, _id, _id) == 0)
            {
                Hosts.TryRemove(_id, out _);
                var error = Marshal.GetLastPInvokeError();
                DestroyNativeWindow(_window);
                _window = 0;
                throw new Win32Exception(error);
            }
        }

        public void Dispose()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            Hide();
            _disposed = true;
            Hosts.TryRemove(_id, out _);
            if (RemoveWindowSubclass(_overlay, &WindowProcedure, _id) == 0)
                Trace.TraceWarning("HDR overlay subclass removal failed.");
            var window = _window;
            _ = Task.Run(() =>
            {
                lock (_renderLock)
                {
                    _presenter?.Dispose();
                    _presenter = null;
                    _patchPresenter?.Dispose();
                    _patchPresenter = null;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (window != 0) DestroyNativeWindow(window);
                    _window = 0;
                });
            });
        }

        public event Action? DisplayChanged;

        public static bool SupportsTransparentInput(nint window)
        {
            return (GetWindowLongPtr(window, -20) & 0x80000) == 0;
        }

        public void InvalidateDevice()
        {
            Interlocked.Exchange(ref _resetDevice, 1);
        }

        public Task PresentAsync(CapturedFrame frame, CapturedFrame? windowFrame, PixelRect windowBounds,
            CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _patchBounds = _bounds.Intersect(windowBounds);
            if (windowFrame is not null && !_patchBounds.IsEmpty && _patchWindow == 0)
            {
                _patchWindow = CreateWindowEx(0x08000020, "STATIC", "", 0x40000000,
                    0, 0, 1, 1, _window, 0, 0, 0);
                if (_patchWindow == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (SetWindowSubclass(_patchWindow, &WindowProcedure, 1, 0) == 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    DestroyNativeWindow(_patchWindow);
                    _patchWindow = 0;
                    throw new Win32Exception(error);
                }
            }

            if (windowFrame is null && _patchWindow != 0) HideNativeWindow(_patchWindow);
            return Task.Run(() =>
            {
                lock (_renderLock)
                {
                    token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (Interlocked.Exchange(ref _resetDevice, 0) != 0)
                    {
                        _presenter?.Dispose();
                        _presenter = null;
                        _presentedFrame = null;
                        _patchPresenter?.Dispose();
                        _patchPresenter = null;
                    }

                    _presenter ??= new(_window, frame.Width, frame.Height);
                    if (!ReferenceEquals(_presentedFrame, frame))
                    {
                        _presenter.Present(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
                        _presentedFrame = frame;
                    }

                    if (windowFrame is not null && !_bounds.Intersect(windowBounds).IsEmpty)
                    {
                        var patch = HdrOverlayFrame.ComposeWindow(frame, _bounds, windowFrame, windowBounds, token);
                        _patchPresenter ??= new(_patchWindow, patch.Width, patch.Height);
                        _patchPresenter.Present(patch.Pixels.Span, patch.Width, patch.Height, patch.Stride);
                    }

                    token.ThrowIfCancellationRequested();
                }
            }, token);
        }

        public void SetSelection(PixelRect selection)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_patchWindow == 0 || _disposed) return;
            var clip = _patchBounds.Intersect(selection);
            if (_appliedClip == clip && _appliedPatchBounds == _patchBounds) return;
            _appliedClip = clip;
            _appliedPatchBounds = _patchBounds;
            if (clip.IsEmpty)
            {
                HideNativeWindow(_patchWindow);
                return;
            }

            var nativeRegion = CreateRectRgn(clip.X - _patchBounds.X, clip.Y - _patchBounds.Y,
                clip.Right - _patchBounds.X, clip.Bottom - _patchBounds.Y);
            if (nativeRegion == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (SetWindowRgn(_patchWindow, nativeRegion, 1) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (DeleteObject(nativeRegion) == 0)
                    Trace.TraceWarning("HDR overlay region cleanup failed.");
                throw new Win32Exception(error);
            }

            if (SetWindowPos(_patchWindow, 0, _patchBounds.X - _bounds.X, _patchBounds.Y - _bounds.Y,
                    _patchBounds.Width, _patchBounds.Height, 0x0054) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        public void Show()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            if (!_attached)
            {
                _previousOwner = GetWindowLongPtr(_overlay, -8);
                Marshal.SetLastPInvokeError(0);
                if (SetWindowLongPtr(_overlay, -8, _window) == 0 && Marshal.GetLastPInvokeError() != 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                _attached = true;
            }

            if (SetWindowPos(_window, -1, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, 0x0050) == 0 ||
                SetWindowPos(_overlay, -1, 0, 0, 0, 0, 0x0013) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        public void Hide()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_attached)
            {
                SetWindowLongPtr(_overlay, -8, _previousOwner);
                _attached = false;
            }

            if (_window != 0) HideNativeWindow(_window);
        }

        private static void DestroyNativeWindow(nint window)
        {
            if (DestroyWindow(window) == 0)
                Trace.TraceWarning("HDR overlay window cleanup failed.");
        }

        private static void HideNativeWindow(nint window)
        {
            // ShowWindow returns the previous visibility, not a success status.
#pragma warning disable CA1806
            ShowWindow(window, 0);
#pragma warning restore CA1806
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
        {
            if (data == 0)
            {
                switch (message)
                {
                    case 0x84: return -1;
                    case 0x21: return 3;
                }
            }
            else if (message is 0x1a or 0x7e or 0x320 or 0x2e0 &&
                     Hosts.TryGetValue(data, out var reference) && reference.TryGetTarget(out var host))
            {
                WindowsDisplayColor.Invalidate();
                Dispatcher.UIThread.Post(() =>
                {
                    if (!host._disposed) host.DisplayChanged?.Invoke();
                });
            }

            return DefSubclassProc(window, message, wParam, lParam);
        }

        [LibraryImport("comctl32.dll")]
        private static partial int SetWindowSubclass(nint window,
            delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);

        [LibraryImport("comctl32.dll")]
        private static partial int RemoveWindowSubclass(nint window,
            delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id);

        [LibraryImport("comctl32.dll")]
        private static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

        [LibraryImport("gdi32.dll")]
        private static partial int DeleteObject(nint value);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int SetWindowRgn(nint window, nint region, int redraw);

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16,
            SetLastError = true)]
        private static partial nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial nint GetWindowLongPtr(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static partial nint SetWindowLongPtr(nint window, int index, nint value);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height,
            uint flags);

        // ReSharper disable once UnusedMethodReturnValue.Local
        [LibraryImport("user32.dll")]
        private static partial int ShowWindow(nint window, int command);

        [LibraryImport("user32.dll")]
        private static partial int DestroyWindow(nint window);
    }
}
