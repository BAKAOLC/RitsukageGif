namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCaptureContext
    {
        private const int MaximumBufferedSources = 8;
        private readonly Dictionary<ScreenKey, BufferedScreen> _bufferedScreens = [];
        private readonly List<ScreenKey> _expiredScreens = [];
        private readonly Dictionary<ScreenKey, ScreenRetry> _screenRetries = [];

        private readonly record struct ScreenRetry(DisplayTopology Topology, long At);

        private readonly record struct ScreenKey(
            PixelRect Area,
            int Width,
            int Height,
            // Cursor visibility is part of cache identity even though no caller reads this property.
            // ReSharper disable once NotAccessedPositionalProperty.Local
            bool Cursor,
            int Fps,
            // Keep synchronous and buffered captures in separate cache entries.
            // ReSharper disable once NotAccessedPositionalProperty.Local
            bool Buffered);

        private sealed class BufferedScreen(ISceneWindowSource capture, DisplayTopology topology)
        {
            public ISceneWindowSource Capture { get; } = capture;
            public DisplayTopology Topology { get; } = topology;
            public double LastUsed;
        }

        public byte[]? CaptureScreen(PixelRect area, int width, int height, RecordingOptions options,
            Func<byte[]> capture) =>
            CaptureScreen(area, width, height, options, capture, static callback => callback());

        public byte[]? CaptureScreen<TState>(PixelRect area, int width, int height, RecordingOptions options,
            TState state, Func<TState, byte[]> capture, CancellationToken token = default, bool buffer = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            var buffered = buffer && _bufferSources && _topology is not null && !DesktopCapture.UsesSourceSelection;
            var key = new ScreenKey(area, width, height, options.Cursor, _captureFps ?? options.Fps, buffered);
            if (_screens.TryGetValue(key, out var pixels)) return pixels;
            try
            {
                pixels = buffered
                    ? CaptureBufferedScreen(key, options, _topology!, state, capture, token)
                    : capture(state);
            }
            catch (CaptureSourceLostException)
            {
                if (_bufferedScreens.Remove(key, out var lost))
                {
                    lost.Capture.Dispose();
                    _screenRetries[key] = new ScreenRetry(lost.Topology, Environment.TickCount64 + 500);
                }

                pixels = null;
            }

            _screens.Add(key, pixels);
            return pixels;
        }

        private byte[]? CaptureBufferedScreen<TState>(ScreenKey key, RecordingOptions options, DisplayTopology topology,
            TState state, Func<TState, byte[]> fallback, CancellationToken token)
        {
            if (_screenRetries.TryGetValue(key, out var retry))
            {
                if (ReferenceEquals(retry.Topology, topology) && Environment.TickCount64 < retry.At) return null;
                _screenRetries.Remove(key);
            }

            if (_bufferedScreens.TryGetValue(key, out var screen) && !ReferenceEquals(screen.Topology, topology))
            {
                screen.Capture.Dispose();
                _bufferedScreens.Remove(key);
                screen = null;
            }

            if (screen is null)
            {
                if (_bufferedScreens.Count + _screenRetries.Count >= MaximumBufferedSources) return fallback(state);
                var fixedOptions = options with { Region = key.Area, Divisor = 1, Fps = key.Fps };
                screen = new BufferedScreen(new BufferedSceneWindowSource(
                    () => _screenFactory(fixedOptions, topology, key.Width, key.Height), key.Fps), topology);
                _bufferedScreens.Add(key, screen);
            }

            screen.LastUsed = MotionTime;
            return screen.Capture.Capture(token);
        }

        private void ExpireScreens()
        {
            foreach (var (key, retry) in _screenRetries)
                if (Environment.TickCount64 >= retry.At)
                    _expiredScreens.Add(key);
            foreach (var key in _expiredScreens) _screenRetries.Remove(key);
            _expiredScreens.Clear();
            foreach (var (key, screen) in _bufferedScreens)
                if (MotionTime - screen.LastUsed > Math.Max(.5, 2.0 / key.Fps))
                    _expiredScreens.Add(key);
            foreach (var key in _expiredScreens)
            {
                _bufferedScreens[key].Capture.Dispose();
                _bufferedScreens.Remove(key);
            }

            _expiredScreens.Clear();
        }

        private sealed class ScreenSource(RecordingOptions options, DisplayTopology topology, int width, int height)
            : ISceneWindowSource
        {
            private readonly DesktopCapture _capture = new(options, topology.Monitors, width, height);
            public int Width => width;
            public int Height => height;
            public bool OwnsFrameBuffers => true;
            public byte[] Capture(CancellationToken token) => _capture.Capture(token);
            public void Dispose() => _capture.Dispose();
        }
    }
}
