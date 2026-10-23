using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    // Used only by the recording producer; consumers must not mutate cached frame buffers.
    internal sealed class SceneCaptureContext : IDisposable
    {
        private readonly Dictionary<PresetRuntime, PresetRuntimeSnapshot> _snapshots = [];
        private readonly Dictionary<nint, WindowTarget?> _targets = [];

        private readonly Dictionary<(PixelRect Area, int Width, int Height, bool Cursor, int Fps), byte[]?> _screens =
            [];

        private readonly Dictionary<(nint Handle, bool Cursor, int Fps), WindowEntry> _windows = [];
        private readonly HashSet<(nint Handle, bool Cursor, int Fps)> _unavailable = [];
        private readonly List<(nint Handle, bool Cursor, int Fps)> _expired = [];
        private DisplayTopology? _topology;
        private IReadOnlyList<WindowTarget> _windowList = [];
        private double _nextSearch;
        private double? _timeOrigin;
        private bool _runtimeClock;
        private readonly ExternalInputHub? _inputs;
        private double _liveTimestamp;

        private readonly Dictionary<(string Name, double Delay, double StaleAfter, bool Hold), ExternalInputFrame?>
            _inputFrames = [];

        private readonly Dictionary<string, MediaFrameSource> _media =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private bool _disposed;
        private long _mediaBytes;
        private readonly long _maxMediaBytes;
        private readonly int? _captureFps;
        private readonly Func<nint, bool, int, ISceneWindowSource> _windowFactory;

        public SceneCaptureContext(int? captureFps = null,
            Func<nint, bool, int, ISceneWindowSource>? windowFactory = null, long maxMediaBytes = 512L * 1024 * 1024,
            ExternalInputHub? inputs = null)
        {
            if (captureFps is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(captureFps));
            if (maxMediaBytes < 0 || maxMediaBytes > 512L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maxMediaBytes));
            _maxMediaBytes = maxMediaBytes;
            _inputs = inputs;
            _captureFps = captureFps;
            _windowFactory = windowFactory ?? ((handle, cursor, fps) => new WindowSource(handle, cursor, fps));
        }

        public double MotionTime { get; private set; } = PresetMotion.Now;
        public double ElapsedSeconds { get; private set; }

        public void BeginFrame(double elapsedSeconds)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            _timeOrigin ??= PresetMotion.Now;
            MotionTime = _timeOrigin.Value + elapsedSeconds;
            ElapsedSeconds = elapsedSeconds;
            _liveTimestamp = ExternalInputHub.Now;
            _inputFrames.Clear();
            _topology = null;
            _snapshots.Clear();
            _targets.Clear();
            _unavailable.Clear();
            _screens.Clear();
            foreach (var (key, entry) in _windows)
            {
                if (MotionTime - entry.LastUsed > 5) _expired.Add(key);
                else entry.HasFrame = false;
            }

            foreach (var key in _expired)
            {
                _windows[key].Capture.Dispose();
                _windows.Remove(key);
            }

            _expired.Clear();
        }

        public DisplayTopology GetTopology(Func<DisplayTopology> provider)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _topology ??= provider();
        }

        public ExternalInputFrame? ReadInput(PresetMediaSource media, bool? holdLast = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var hold = holdLast ?? media.HoldLast;
            var key = (media.File, media.Delay, media.StaleAfter, hold);
            if (!_inputFrames.TryGetValue(key, out var frame))
                _inputFrames[key] = frame = _inputs?.ReadFrame(media.File,
                    Math.Max(0, _liveTimestamp - media.Delay), media.StaleAfter, hold);
            return frame;
        }

        public PresetRuntimeSnapshot GetSnapshot(PresetRuntime runtime)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_snapshots.TryGetValue(runtime, out var snapshot))
            {
                if (!_runtimeClock)
                {
                    _timeOrigin = runtime.Now;
                    MotionTime = _timeOrigin.Value + ElapsedSeconds;
                    _runtimeClock = true;
                }

                runtime.SetTime(MotionTime);
                _snapshots.Add(runtime, snapshot = runtime.Snapshot);
            }

            return snapshot;
        }

        public IReadOnlyList<WindowTarget> GetWindows()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (MotionTime < _nextSearch) return _windowList;
            _windowList = WindowLocator.List();
            _nextSearch = MotionTime + 0.1;
            return _windowList;
        }

        public WindowTarget? Refresh(WindowTarget target)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_targets.TryGetValue(target.Handle, out var current))
                _targets.Add(target.Handle, current = WindowLocator.Refresh(target));
            return current;
        }

        public byte[]? CaptureScreen(PixelRect area, int width, int height, RecordingOptions options,
            Func<byte[]> capture)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = (area, width, height, options.Cursor, _captureFps ?? options.Fps);
            if (_screens.TryGetValue(key, out var pixels)) return pixels;
            try
            {
                pixels = capture();
            }
            catch (CaptureSourceLostException)
            {
                pixels = null;
            }

            _screens.Add(key, pixels);
            return pixels;
        }

        public (byte[]? Pixels, int Width, int Height) CaptureWindow(nint handle, RecordingOptions options,
            CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            var key = (handle, options.Cursor, _captureFps ?? options.Fps);
            if (_unavailable.Contains(key)) return (null, 0, 0);
            if (!_windows.TryGetValue(key, out var entry))
            {
                entry = new WindowEntry(_windowFactory(handle, options.Cursor, _captureFps ?? options.Fps));
                _windows.Add(key, entry);
            }

            entry.LastUsed = MotionTime;
            if (!entry.HasFrame)
            {
                try
                {
                    entry.Pixels = entry.Capture.Capture(token);
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80000013))
                {
                    entry.Capture.Dispose();
                    _windows.Remove(key);
                    _unavailable.Add(key);
                    return (null, 0, 0);
                }
                catch (CaptureSourceLostException)
                {
                    entry.Capture.Dispose();
                    _windows.Remove(key);
                    _unavailable.Add(key);
                    return (null, 0, 0);
                }

                entry.HasFrame = true;
            }

            return (entry.Pixels, entry.Capture.Width, entry.Capture.Height);
        }

        public MediaFrameSource GetMedia(string path, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_media.TryGetValue(path, out var source))
            {
                source = new MediaFrameSource(path, token, _maxMediaBytes - _mediaBytes);
                _media.Add(path, source);
                _mediaBytes += source.DecodedBytes;
            }

            return source;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _windows.Values) entry.Capture.Dispose();
            foreach (var source in _media.Values) source.Dispose();
            _media.Clear();
            _mediaBytes = 0;
            _windows.Clear();
            _screens.Clear();
            _snapshots.Clear();
            _inputFrames.Clear();
            _targets.Clear();
            _unavailable.Clear();
        }

        private sealed class WindowSource(nint handle, bool cursor, int fps) : ISceneWindowSource
        {
            private readonly WindowContentCapture _capture = new(handle, cursor, fps);
            public int Width => _capture.Width;
            public int Height => _capture.Height;
            public byte[]? Capture(CancellationToken token) => _capture.Capture(token);
            public void Dispose() => _capture.Dispose();
        }

        private sealed class WindowEntry(ISceneWindowSource capture)
        {
            public readonly ISceneWindowSource Capture = capture;
            public byte[]? Pixels;
            public bool HasFrame;
            public double LastUsed;
        }
    }
}
