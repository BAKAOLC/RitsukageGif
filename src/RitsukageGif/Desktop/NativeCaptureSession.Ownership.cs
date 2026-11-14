namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class NativeCaptureSession
    {
        private int _ownerReleased;
        private int _references = 1;

        internal bool IsWindowSource { get; }

        internal PixelRect? NativeBounds
        {
            get
            {
                lock (_gate) return _frames is null ? null : _sourceGeometry?.Bounds;
            }
        }

        internal double OutputScaling
        {
            get
            {
                lock (_gate)
                {
                    if (_sourceGeometry is not { PixelWidth: > 0, PixelHeight: > 0 } source) return 1;
                    if (_packet?.Metadata.Portal?.LogicalSize is not [> 0, > 0] logical) return 1;
                    var scale = (double)source.PixelWidth / logical[0];
                    // The portal may omit logical size or report it with compositor rounding.
                    return scale is > 1 and <= 16 && Math.Abs(source.PixelHeight - logical[1] * scale) <= scale
                        ? scale
                        : 1;
                }
            }
        }

        internal static double SharedOutputScaling
        {
            get
            {
                lock (SharedLock) return _shared?.OutputScaling ?? 1;
            }
        }

        internal IReadOnlyList<WindowTarget> GetAuthorizedWindows()
        {
            lock (_gate)
            {
                if (!IsWindowSource || !IsAvailable || _sourceGeometry is not { } source) return [];
                return
                [
                    new(-1, "", "", source.Bounds, source.Bounds)
                    {
                        NativeSourceIdentity = SourceIdentity(source),
                        NativeAuthorization = new(_authorizationId, SourceIdentity(source), source.Generation),
                    },
                ];
            }
        }

        internal NativeCaptureSessionReference Retain()
        {
            lock (_gate)
            {
                if (_disposed) throw new CaptureSourceLostException();
                _references++;
                return new(this);
            }
        }

        internal static NativeCaptureSessionReference RetainShared()
        {
            lock (SharedLock) return (_shared ?? throw new CaptureSourceLostException()).Retain();
        }

        internal static NativeCaptureSessionReference RetainAuthorizedWindow(NativeCaptureAuthorization? expected)
        {
            lock (SharedLock)
                return (_shared ?? throw new CaptureSourceLostException()).RetainWindow(expected);
        }

        internal NativeCaptureSessionReference RetainWindow(NativeCaptureAuthorization? expected)
        {
            lock (_gate)
            {
                if (_disposed) throw new CaptureSourceLostException();
                if (!IsWindowSource) throw new NotSupportedException(UiText.Get("PortalWindowMatchingUnavailable"));
                if (expected is not null)
                {
                    var source = _sourceGeometry;
                    if (source is null || _authorizationId != expected.AuthorizationId ||
                        source.Generation != expected.Generation || SourceIdentity(source) != expected.SourceIdentity)
                        throw new CaptureSourceLostException();
                }

                _references++;
                return new(this);
            }
        }
    }
}
