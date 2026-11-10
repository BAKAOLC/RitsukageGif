namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class NativeCaptureSession
    {
        private int _references = 1;
        private int _ownerReleased;

        internal bool IsWindowSource { get; }

        internal PixelRect? NativeBounds
        {
            get
            {
                lock (_gate) return _frames is null ? null : _sourceGeometry?.Bounds;
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
