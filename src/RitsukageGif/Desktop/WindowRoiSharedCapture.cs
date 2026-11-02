using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowRoiSharedCapture : IDisposable
    {
        private static readonly Dictionary<WindowRoiCaptureIdentity, WindowRoiSharedCapture> Sources = [];
        private static readonly Lock RegistryGate = new();
        private static readonly long CaptureInterval = Stopwatch.Frequency / 60;
        private readonly SemaphoreSlim _gate = new(1);
        private readonly WindowRoiCaptureIdentity _key;
        private WindowContentCapture? _capture;
        private CapturedFrameSet? _frames;
        private PixelRect _sourceBounds, _coordinateBounds;
        private double? _nativePixelDensity;
        private long _nativeRevision = -2;
        private long _nextCaptureAt;
        private int _references;
        private long _revision;

        private WindowRoiSharedCapture(WindowRoiCaptureIdentity key)
        {
            _key = key;
        }

        public void Dispose()
        {
            lock (RegistryGate)
            {
                if (--_references != 0) return;
                Sources.Remove(_key);
            }

            _capture?.Dispose();
            _gate.Dispose();
        }

        internal bool Matches(WindowRoiPinDescriptor descriptor)
        {
            return _key == WindowRoiCaptureIdentity.From(descriptor);
        }

        internal static WindowRoiSharedCapture Acquire(WindowRoiPinDescriptor descriptor)
        {
            var key = WindowRoiCaptureIdentity.From(descriptor);
            lock (RegistryGate)
            {
                if (!Sources.TryGetValue(key, out var source))
                {
                    source = new(key);
                    Sources.Add(key, source);
                }

                source._references++;
                return source;
            }
        }

        internal BorrowedFrame Borrow(PixelRect bounds, CancellationToken token)
        {
            _gate.Wait(token);
            try
            {
                var geometryChanged = bounds.Width != _sourceBounds.Width || bounds.Height != _sourceBounds.Height ||
                                      (_key.Region is not null && bounds != _sourceBounds);
                if (_frames is null || geometryChanged || Stopwatch.GetTimestamp() >= _nextCaptureAt)
                {
                    _capture ??= new((nint)_key.Handle, _key.Cursor, 60, false, _key.Authorization);
                    if (_key.Region is { } relative)
                    {
                        var clipped = relative.Intersect(new(0, 0, bounds.Width, bounds.Height));
                        _capture.ConfigureNativeRegion(clipped with
                        {
                            X = clipped.X + bounds.X, Y = clipped.Y + bounds.Y,
                        });
                    }

                    var frames = _capture.CaptureFrames(token);
                    token.ThrowIfCancellationRequested();
                    var nativeRevision = _capture.FrameRevision;
                    if (_frames is null || nativeRevision < 0 || nativeRevision != _nativeRevision || geometryChanged)
                        _revision++;
                    var now = Stopwatch.GetTimestamp();
                    _nextCaptureAt = _frames is null ? now + CaptureInterval : NextCaptureAt(_nextCaptureAt, now);
                    _frames = frames;
                    _sourceBounds = bounds;
                    _coordinateBounds = _capture.CoordinateBounds ?? bounds;
                    _nativePixelDensity = _key.Region is null ? null : _capture.NativePixelDensity;
                    _nativeRevision = nativeRevision;
                }

                return new(this, _frames, _revision, _key.Region is null ? bounds : _coordinateBounds,
                    _nativePixelDensity);
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }

        private static long NextCaptureAt(long deadline, long now)
        {
            return deadline + ((now - deadline) / CaptureInterval + 1) * CaptureInterval;
        }

        internal readonly ref struct BorrowedFrame(
            WindowRoiSharedCapture owner,
            CapturedFrameSet frames,
            long revision,
            PixelRect coordinateBounds,
            double? nativePixelDensity)
        {
            internal CapturedFrameSet Frames { get; } = frames;
            internal long Revision { get; } = revision;
            internal PixelRect CoordinateBounds { get; } = coordinateBounds;
            internal double? NativePixelDensity { get; } = nativePixelDensity;

            public void Dispose()
            {
                owner._gate.Release();
            }
        }
    }
}
