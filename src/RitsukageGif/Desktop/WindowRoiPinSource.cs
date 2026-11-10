using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class WindowRoiPinSource : IDisposable
    {
        private readonly WindowTarget _target;
        private readonly Func<HdrToneMapMode> _toneMapProvider;
        private WindowRoiSharedCapture? _capture;
        private readonly WindowRoiPinDescriptor _descriptor;
        private WindowRoiPinDescriptor? _lastDescriptor;
        private long _lastRevision = -2;
        private HdrToneMapMode _lastToneMap;
        private int _lastWidth, _lastHeight;

        internal WindowRoiPinSource(WindowRoiPinDescriptor descriptor, Func<HdrToneMapMode>? toneMapProvider = null)
        {
            if (!descriptor.IsValid) throw new ArgumentOutOfRangeException(nameof(descriptor));
            _descriptor = descriptor;
            _toneMapProvider = toneMapProvider ?? (() => HdrToneMapMode.HighlightCompression);
            _target = new((nint)descriptor.Handle, descriptor.Process, descriptor.Title, default, default)
            {
                NativeAuthorization = descriptor.NativeAuthorization,
                NativeSourceIdentity = descriptor.NativeAuthorization?.SourceIdentity,
            };
        }

        public void Dispose()
        {
            DisposeCapture();
        }

        internal static WindowRoiPinDescriptor Describe(WindowTarget target, PixelRect region, bool cursor)
        {
            var current = target.NativeAuthorization is { } authorization
                ? AuthorizedTarget(authorization) ?? throw new CaptureSourceLostException()
                : target;
            var bounds = OperatingSystem.IsLinux() ? current.Client : current.Bounds;
            var clipped = region.Intersect(bounds);
            if (clipped.IsEmpty)
                throw new ArgumentException(UiText.Get("PinnedLiveRegionUnavailable"), nameof(region));
            if (target.NativeAuthorization is { } native)
            {
                if (target.Handle != -1) throw new CaptureSourceLostException();
                return new(-1, 0, current.Process, current.Title,
                    clipped with { X = clipped.X - bounds.X, Y = clipped.Y - bounds.Y }, cursor,
                    NativeAuthorization: native);
            }

            var process = LiveWindowState(target.Handle).ProcessId;
            if (process == 0) throw new CaptureSourceLostException();
            return new(target.Handle, process, target.Process, target.Title,
                clipped with { X = clipped.X - bounds.X, Y = clipped.Y - bounds.Y }, cursor);
        }

        private static WindowTarget? AuthorizedTarget(NativeCaptureAuthorization authorization)
        {
            return NativeCaptureSession.AuthorizedWindows()
                .FirstOrDefault(target => target.Handle == -1 && target.NativeAuthorization == authorization);
        }

        internal (WindowRoiCaptureState State, WindowRoiPinFrame? Frame, WindowRoiPinDescriptor Descriptor)
            Capture(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var descriptor = _descriptor;
            WindowTarget? current;
            if (descriptor.NativeAuthorization is { } authorization)
            {
                current = AuthorizedTarget(authorization);
                if (current is null)
                {
                    DisposeCapture();
                    return (WindowRoiCaptureState.Closed, null, descriptor);
                }
            }
            else
            {
                var state = LiveWindowState(_target.Handle);
                if (!state.Exists || state.ProcessId != descriptor.ProcessId)
                {
                    DisposeCapture();
                    return (WindowRoiCaptureState.Closed, null, descriptor);
                }

                current = state.Visible ? WindowLocator.Refresh(_target) : null;
                if (current is null)
                {
                    DisposeCapture();
                    return (WindowRoiCaptureState.Minimized, null, descriptor);
                }
            }

            var bounds = OperatingSystem.IsLinux() ? current.Client : current.Bounds;
            var clipped = descriptor.RelativeRegion.Intersect(new(0, 0, bounds.Width, bounds.Height));
            if (clipped.IsEmpty)
            {
                DisposeCapture();
                return (WindowRoiCaptureState.RegionUnavailable, null, descriptor);
            }

            if (_capture is not null && !_capture.Matches(descriptor)) DisposeCapture();
            _capture ??= WindowRoiSharedCapture.Acquire(descriptor);
            try
            {
                using var captured = _capture.Borrow(bounds, token);
                var revision = captured.Revision;
                var mode = _toneMapProvider();
                if (revision == _lastRevision && descriptor == _lastDescriptor &&
                    bounds.Width == _lastWidth && bounds.Height == _lastHeight && mode == _lastToneMap)
                    return (WindowRoiCaptureState.Active, null, descriptor);
                var frame = CreateFrame(captured.Frames, bounds, descriptor, token, mode,
                    captured.CoordinateBounds, captured.NativePixelDensity ?? 1, captured.NativePixelDensity);
                _lastRevision = revision;
                _lastDescriptor = descriptor;
                _lastWidth = bounds.Width;
                _lastHeight = bounds.Height;
                _lastToneMap = mode;
                return (WindowRoiCaptureState.Active, frame, descriptor);
            }
            catch (CaptureSourceLostException)
            {
                token.ThrowIfCancellationRequested();
                DisposeCapture();
                var closed = descriptor.NativeAuthorization is { } native
                    ? AuthorizedTarget(native) is null
                    : !LiveWindowState(_target.Handle).Exists;
                return (closed ? WindowRoiCaptureState.Closed : WindowRoiCaptureState.RegionUnavailable,
                    null, descriptor);
            }
            catch (Exception error) when (descriptor.NativeAuthorization is not null &&
                                          error is IOException or ObjectDisposedException)
            {
                token.ThrowIfCancellationRequested();
                DisposeCapture();
                return (AuthorizedTarget(descriptor.NativeAuthorization!) is null
                    ? WindowRoiCaptureState.Closed
                    : WindowRoiCaptureState.RegionUnavailable, null, descriptor);
            }
        }

        private void DisposeCapture()
        {
            _capture?.Dispose();
            _capture = null;
            _lastRevision = -2;
        }

        private static uint ProcessForWindow(nint handle)
        {
            return GetWindowThreadProcessId(handle, out var process) != 0 ? process : 0;
        }

        private static (bool Exists, bool Visible, uint ProcessId) LiveWindowState(nint handle)
        {
            if (OperatingSystem.IsMacOS()) return MacWindowSystem.LiveWindowState(handle);
            if (OperatingSystem.IsLinux()) return X11WindowSystem.LiveWindowState(handle);
            return (WindowLocator.IsWindow(handle) != 0,
                NativeMethods.IsWindowVisible(handle) != 0 && NativeMethods.IsIconic(handle) == 0,
                ProcessForWindow(handle));
        }

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    }
}
