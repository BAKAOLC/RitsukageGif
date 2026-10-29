using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace RitsukageGif.Desktop
{
    // Capture returns a stable buffer until the next Capture call, matching the native source contract.
    internal sealed class BufferedSceneWindowSource(Func<ISceneWindowSource> factory, int fps) : ISceneWindowSource
    {
        private readonly Lock _gate = new();
        private readonly TimeSpan _maximumAge = TimeSpan.FromSeconds(Math.Max(.25, 2.0 / fps));
        private readonly ManualResetEventSlim _ready = new();
        private readonly CancellationTokenSource _stop = new();
        private long _completedAt;
        private ExceptionDispatchInfo? _failure;
        private bool _hasFrame, _disposed;
        private byte[]? _latestPixels, _readPixels;
        private int _latestWidth, _latestHeight;
        private CancellationTokenRegistration _registration;
        private Task? _worker;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public byte[]? Capture(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (_worker is null)
            {
                _registration = token.Register(static state => ((BufferedSceneWindowSource)state!).CancelSource(),
                    this);
                _worker = Task.Factory.StartNew(Produce, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
                // First-frame readiness must precede the recording clock, not create an empty first frame.
                _ready.Wait(token);
            }

            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _failure?.Throw();
                if (_hasFrame)
                {
                    (_readPixels, _latestPixels) = (_latestPixels, _readPixels);
                    Width = _latestWidth;
                    Height = _latestHeight;
                    _hasFrame = false;
                }

                return _completedAt != 0 && Stopwatch.GetElapsedTime(_completedAt) <= _maximumAge
                    ? _readPixels
                    : null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelSource();
            _worker?.GetAwaiter().GetResult();
            _registration.Dispose();
            _ready.Dispose();
            _stop.Dispose();
            _latestPixels = _readPixels = null;
        }

        private void Produce()
        {
            try
            {
                using var source = factory();
                var token = _stop.Token;
                var period = TimeSpan.FromSeconds(1.0 / fps);
                long previousRevision = -1;
                while (!token.IsCancellationRequested)
                {
                    var started = Stopwatch.GetTimestamp();
                    var pixels = source.Capture(token);
                    token.ThrowIfCancellationRequested();
                    var revision = source.FrameRevision;
                    var width = source.Width;
                    var height = source.Height;
                    if (pixels is not null && (width <= 0 || height <= 0 ||
                                               pixels.Length != checked(width * height * 4)))
                        throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
                    lock (_gate)
                    {
                        if (pixels is null || revision < 0 || revision != previousRevision)
                        {
                            if (pixels is null || source.OwnsFrameBuffers)
                            {
                                _latestPixels = pixels;
                            }
                            else
                            {
                                if (_latestPixels?.Length != pixels.Length) _latestPixels = new byte[pixels.Length];
                                pixels.CopyTo(_latestPixels, 0);
                            }

                            _latestWidth = width;
                            _latestHeight = height;
                            _hasFrame = true;
                        }

                        previousRevision = pixels is null ? -1 : revision;
                        _completedAt = Stopwatch.GetTimestamp();
                    }

                    _ready.Set();
                    var delay = period - Stopwatch.GetElapsedTime(started);
                    if (delay > TimeSpan.Zero) token.WaitHandle.WaitOne(delay);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                lock (_gate) _failure = ExceptionDispatchInfo.Capture(error);
            }
            finally
            {
                _ready.Set();
            }
        }

        private void CancelSource()
        {
            try
            {
                _stop.Cancel();
            }
            catch (AggregateException error)
            {
                Trace.TraceWarning("Capture cancellation callback failed: {0}", error);
            }
        }
    }
}
