using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class DesktopCapture : IFrameSource, IColorFrameSource
    {
        private readonly Func<DisplayTopology>? _displayProvider;
        private readonly int _width, _height;
        private bool _disposed;
        private long _nextRetry;
        private RecordingOptions _options;
        private PortableDesktopCapture? _portable;
        private DisplayTopology _topology;
        private WindowsDesktopCapture? _windows;

        public DesktopCapture(RecordingOptions options, IEnumerable<PixelRect>? monitors = null,
            int? outputWidth = null, int? outputHeight = null)
            : this(options, new DisplayTopology(monitors ?? [options.Region]), outputWidth, outputHeight)
        {
        }

        public DesktopCapture(RecordingOptions options, Func<DisplayTopology> displayProvider,
            int? outputWidth = null, int? outputHeight = null)
            : this(options, displayProvider(), outputWidth, outputHeight)
        {
            _displayProvider = displayProvider;
        }

        private DesktopCapture(RecordingOptions options, DisplayTopology topology, int? outputWidth, int? outputHeight)
        {
            options.Validate();
            _options = options;
            _width = outputWidth ?? options.Width;
            _height = outputHeight ?? options.Height;
            if (_width <= 0 || _height <= 0 || (long)_width * _height * 4 > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            _topology = topology;
        }

        public static bool UsesSourceSelection => OperatingSystem.IsLinux() &&
                                                  !string.IsNullOrEmpty(
                                                      Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        public static PixelRect? SourceBounds => NativeCaptureSession.SourceBounds;

        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!OperatingSystem.IsWindows())
            {
                var pixels = Capture(token);
                var color = new FrameColorInfo(false);
                return new(new(_width, _height, _width * 4, CapturePixelFormat.Bgra8Srgb, pixels, color,
                        CaptureAlphaMode.Opaque, -1, [new(new(0, 0, _width, _height), color)]), null,
                    Stopwatch.GetTimestamp());
            }

            var topology = _displayProvider?.Invoke() ?? _topology;
            if (!ReferenceEquals(topology, _topology))
            {
                ReleaseSource();
                _topology = topology;
            }

            if (!topology.Covers(_options.Region)) throw new CaptureSourceLostException();
            _windows ??= new(_options, topology.Monitors, _width, _height);
            try
            {
                return _windows.CaptureFrames(token);
            }
            catch (CaptureSourceLostException)
            {
                ReleaseSource();
                throw;
            }
        }

        public byte[] Capture()
        {
            return Capture(CancellationToken.None);
        }

        public byte[] Capture(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var topology = _displayProvider?.Invoke() ?? _topology;
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(topology, _topology))
            {
                ReleaseSource();
                _topology = topology;
                _nextRetry = 0;
            }

            if ((!UsesSourceSelection && !topology.Covers(_options.Region)) || Environment.TickCount64 < _nextRetry)
                throw new CaptureSourceLostException();
            try
            {
                if (_windows is null && _portable is null)
                {
                    if (OperatingSystem.IsWindows())
                        _windows = new(_options, topology.Monitors, _width, _height);
                    else
                        _portable = new(_options, [.. topology.Monitors], _width, _height);
                }

                token.ThrowIfCancellationRequested();
                var pixels = _windows?.Capture(token) ?? _portable!.Capture(token);
                token.ThrowIfCancellationRequested();
                return pixels;
            }
            catch (CaptureSourceLostException)
            {
                token.ThrowIfCancellationRequested();
                ReleaseSource();
                _nextRetry = Environment.TickCount64 + 500;
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseSource();
        }

        public void Move(PixelRect region)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (region.IsEmpty) throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            if (region.Width != _options.Region.Width || region.Height != _options.Region.Height)
                ReleaseSource();
            else if (_windows is not null) _windows.Move(region);
            else _portable?.Move(region);
            _options = _options with { Region = region };
            _nextRetry = 0;
        }

        private void ReleaseSource()
        {
            _windows?.Dispose();
            _windows = null;
            _portable?.Dispose();
            _portable = null;
        }

        public static Task<NativeCaptureSelection> BeginSourceSelectionAsync(bool window = false,
            bool cursor = true, int fps = 60, CancellationToken token = default)
        {
            return NativeCaptureSession.BeginSelectionAsync(window, token, cursor, fps);
        }

        public static Task<CaptureSourceFrame> PrepareSourceAsync(bool window = false,
            bool cursor = true, int fps = 60, CancellationToken token = default)
        {
            return NativeCaptureSession.PrepareAsync(window, token, cursor, fps);
        }

        public static void CloseSharedSource()
        {
            NativeCaptureSession.CloseShared();
            NativeCaptureLease.CloseAll();
        }
    }
}
