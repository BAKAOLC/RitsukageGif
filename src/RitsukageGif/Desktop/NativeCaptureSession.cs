using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class NativeCaptureSession : IDisposable
    {
        private static readonly SemaphoreSlim SelectionLock = new(1, 1);
        private static readonly object SharedLock = new();
        private static NativeCaptureSession? _shared;
        private static long _sharedVersion;
        private readonly object _gate = new();
        private readonly Process _process;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _reader, _errors;
        private byte[]? _pixels;
        private int _width, _height, _x, _y;
        private string _diagnostic = "";
        private Exception? _failure;
        private bool _disposed;
        private readonly bool _cursor, _window;
        private readonly int _fps;

        private NativeCaptureSession(string mode, bool cursor, int fps, PixelRect? monitor = null, nint window = 0)
        {
            _cursor = cursor;
            _window = mode == "window";
            _fps = Math.Clamp(fps, 1, 120);
            var executable = Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Capture");
            if (!File.Exists(executable))
                throw new FileNotFoundException(UiText.Get("CaptureHelperMissing"), executable);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true, CreateNoWindow = true
            };
            start.ArgumentList.Add(mode);
            Add("--fps", _fps);
            Add("--cursor", cursor ? 1 : 0);
            if (monitor is { } bounds)
            {
                Add("--x", bounds.X);
                Add("--y", bounds.Y);
                Add("--width", bounds.Width);
                Add("--height", bounds.Height);
            }

            if (window != 0)
            {
                start.ArgumentList.Add("--id");
                start.ArgumentList.Add(((nuint)window).ToString(CultureInfo.InvariantCulture));
            }

            _process = Process.Start(start) ?? throw new IOException(UiText.Get("CaptureFailed"));
            _errors = ReadErrorsAsync();
            _reader = ReadFramesAsync();
            return;

            void Add(string name, int value)
            {
                start.ArgumentList.Add(name);
                start.ArgumentList.Add(value.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static PixelRect? SourceBounds
        {
            get
            {
                lock (SharedLock)
                {
                    if (_shared is not { } session) return null;
                    lock (session._gate)
                        return session._pixels is null ? null : new PixelRect(0, 0, session._width, session._height);
                }
            }
        }

        public static bool SourceIsWindow
        {
            get
            {
                lock (SharedLock) return _shared?._window ?? false;
            }
        }

        public bool IsAvailable
        {
            get
            {
                lock (_gate) return !_disposed && _failure is null && !_process.HasExited;
            }
        }

        public static NativeCaptureSession CreateScreen(PixelRect monitor, bool cursor, int fps) =>
            new("screen", cursor, fps, monitor);

        public static NativeCaptureSession CreateWindow(nint window, bool cursor, int fps = 60) =>
            new("window", cursor, fps, window: window);

        public static CaptureSourceFrame ReadShared(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            NativeCaptureSession session;
            lock (SharedLock)
                session = _shared ?? throw new CaptureSourceLostException();
            return session.Read(token);
        }

        public static void UseSharedFrame(CaptureFrameConsumer consume, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            NativeCaptureSession session;
            lock (SharedLock)
                session = _shared ?? throw new CaptureSourceLostException();
            session.UseFrame(consume, token);
        }

        public static void CloseShared()
        {
            NativeCaptureSession? session;
            lock (SharedLock)
            {
                session = _shared;
                _shared = null;
                _sharedVersion++;
            }

            session?.Dispose();
        }

        public CaptureSourceFrame Read(CancellationToken token = default)
        {
            AwaitFirstFrame(token);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                var frame = CurrentFrame();
                return frame with { Pixels = (byte[])frame.Pixels.Clone() };
            }
        }

        public void UseFrame(CaptureFrameConsumer consume, CancellationToken token = default)
        {
            AwaitFirstFrame(token);
            // Only synchronous internal consumers may borrow the reader buffer. No pool/shared lock is held here.
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                var frame = CurrentFrame();
                consume(
                    new CaptureFrameHeader(frame.Width, frame.Height, frame.OriginX, frame.OriginY,
                        frame.Pixels.Length), frame.Pixels);
            }
        }

        private void AwaitFirstFrame(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_first.Task.IsCompleted)
            {
                _first.Task.GetAwaiter().GetResult();
                return;
            }

            using var linked = token.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token)
                : null;
            try
            {
                _first.Task.WaitAsync(TimeSpan.FromSeconds(35), linked?.Token ?? _stop.Token).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                throw new IOException(UiText.Get("CaptureSourceTimeout"));
            }
        }

        private CaptureSourceFrame CurrentFrame()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_failure is not null) ExceptionDispatchInfo.Throw(_failure);
            if (_pixels is null) throw new CaptureSourceLostException();
            return new CaptureSourceFrame(_width, _height, _pixels, _x, _y);
        }

        private async Task ReadFramesAsync()
        {
            var header = new byte[24];
            byte[]? next = null;
            try
            {
                var stream = _process.StandardOutput.BaseStream;
                while (!_stop.IsCancellationRequested)
                {
                    await stream.ReadExactlyAsync(header, _stop.Token);
                    var frame = CaptureFrameHeader.Parse(header);
                    if (next?.Length != frame.Length) next = new byte[frame.Length];
                    await stream.ReadExactlyAsync(next, _stop.Token);
                    lock (_gate)
                    {
                        (_pixels, next) = (next, _pixels);
                        _width = frame.Width;
                        _height = frame.Height;
                        _x = frame.OriginX;
                        _y = frame.OriginY;
                    }

                    _first.TrySetResult();
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException
                                              or InvalidOperationException)
            {
                if (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        await _errors.WaitAsync(TimeSpan.FromMilliseconds(250));
                    }
                    catch (Exception waitError) when (waitError is TimeoutException or OperationCanceledException)
                    {
                    }
                }

                if (_stop.IsCancellationRequested)
                {
                    _first.TrySetCanceled(_stop.Token);
                    return;
                }

                var failure = ClassifyFailure(error);
                lock (_gate) _failure = failure;
                _first.TrySetException(failure);
            }
        }

        private async Task ReadErrorsAsync()
        {
            var buffer = new char[512];
            try
            {
                int count;
                while ((count = await _process.StandardError.ReadAsync(buffer, _stop.Token)) > 0)
                    lock (_gate)
                        if (_diagnostic.Length < 2048)
                            _diagnostic += new string(buffer, 0, Math.Min(count, 2048 - _diagnostic.Length));
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        private Exception ClassifyFailure(Exception error)
        {
            string diagnostic;
            lock (_gate) diagnostic = _diagnostic;
            var code = diagnostic.Split([':', '\r', '\n'], 2)[0].Trim();
            if (code == "source-lost" || code.Length == 0 && _first.Task.IsCompletedSuccessfully &&
                error is EndOfStreamException)
                return new CaptureSourceLostException(error);
            return new IOException(UiText.Get(code switch
            {
                "permission-denied" => "CapturePermissionDenied", "cancelled" => "CaptureSourceCancelled",
                "dependency-missing" => "CaptureDependencyMissing", "timeout" => "CaptureSourceTimeout",
                "unsupported" => "CapturePlatformUnsupported",
                _ => "CaptureFailed"
            }), error);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }

            _stop.Cancel();
            try
            {
                _process.StandardInput.Close();
            }
            catch (IOException)
            {
            }

            try
            {
                if (!_process.HasExited) _process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            _ = FinishAsync();
        }

        private async Task FinishAsync()
        {
            try
            {
                await Task.WhenAll(_reader, _errors);
            }
            finally
            {
                _process.Dispose();
                _stop.Dispose();
            }
        }
    }
}
