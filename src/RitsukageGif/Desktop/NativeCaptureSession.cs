using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class NativeCaptureSession : IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Capture.Native");
        private static readonly SemaphoreSlim SelectionLock = new(1, 1);
        private static readonly Lock SharedLock = new();
        private static NativeCaptureSession? _shared;
        private static long _sharedVersion;
        private readonly string _authorizationId = Guid.NewGuid().ToString("N");
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _fps;
        private readonly object _gate = new();
        private readonly Process _process;
        private readonly Task _reader, _errors;
        private readonly CancellationTokenSource _stop = new();
        private string _diagnostic = "";
        private bool _disposed;
        private Exception? _failure;
        private NativeCapturePacket? _packet;
        private CapturedFrameSet? _frames;
        private NativeCaptureGeometry? _geometry, _sourceGeometry;
        private NativeCaptureCapabilities? _capabilities;
        private NativeCaptureCursor? _pointer;
        private byte[]? _cursorPixels;
        private string? _sourceReason, _hdrReason, _pointerReason;
        private readonly SemaphoreSlim _commands = new(1, 1);
        private readonly Dictionary<long, TaskCompletionSource<NativeCaptureMetadata>> _replies = [];
        private long _nextRequest;
        private ulong _frameSequence;
        private readonly long _generation = Interlocked.Increment(ref _generations);
        private static long _generations;

        public static PixelRect? SourceBounds
        {
            get
            {
                lock (SharedLock)
                {
                    if (_shared is not { } session) return null;
                    lock (session._gate)
                        return session._frames is null ? null : session._sourceGeometry?.Bounds;
                }
            }
        }

        private NativeCaptureSession(string mode, bool cursor, int fps, PixelRect? monitor = null, nint window = 0,
            bool retainHdr = false, bool pointerInput = false, string? includedWindowTitle = null)
        {
            _cursor = cursor;
            _retainHdr = retainHdr;
            IsWindowSource = mode == "window";
            _fps = Math.Clamp(fps, 1, 120);
            var executable = Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Capture");
            if (!File.Exists(executable))
                throw new FileNotFoundException(UiText.Get("CaptureHelperMissing"), executable);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true, CreateNoWindow = true,
            };
            start.ArgumentList.Add(mode);
            Add("--fps", _fps);
            Add("--cursor", cursor ? 1 : 0);
            start.ArgumentList.Add("--color");
            start.ArgumentList.Add(retainHdr ? "hdr" : "sdr");
            Add("--generation", _generation);
            if (pointerInput)
            {
                start.ArgumentList.Add("--input");
                start.ArgumentList.Add("pointer");
            }

            if (monitor is { } bounds && OperatingSystem.IsMacOS())
                Add("--id", NativeDisplayCatalog.Resolve(bounds).DisplayId);
            if (mode == "screen" && includedWindowTitle is not null && OperatingSystem.IsMacOS())
            {
                start.ArgumentList.Add("--include-window-title");
                start.ArgumentList.Add(includedWindowTitle);
            }

            if (window != 0)
            {
                start.ArgumentList.Add("--id");
                start.ArgumentList.Add(((nuint)window).ToString(CultureInfo.InvariantCulture));
            }

            _process = Process.Start(start) ?? throw new IOException(UiText.Get("CaptureFailed"));
            Logger.ZLogDebug(
                $"Native capture started. Generation={_generation} Mode={mode} Cursor={cursor} Fps={_fps} RetainHdr={retainHdr} PointerInput={pointerInput}");
            _errors = ReadErrorsAsync();
            _reader = ReadFramesAsync();
            return;

            void Add(string name, long value)
            {
                start.ArgumentList.Add(name);
                start.ArgumentList.Add(value.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static bool SourceIsWindow
        {
            get
            {
                lock (SharedLock) return _shared?.IsWindowSource ?? false;
            }
        }

        public bool IsAvailable
        {
            get
            {
                lock (_gate) return !_disposed && _failure is null && !_process.HasExited;
            }
        }
        private readonly bool _cursor, _retainHdr;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ownerReleased, 1) != 0) return;
            ReleaseReference();
        }

        internal event Action? FrameAvailable;

        internal void ReleaseReference()
        {
            lock (_gate)
            {
                if (--_references != 0) return;
                _disposed = true;
            }

            Logger.ZLogDebug($"Native capture stopping. Generation={_generation}");
            _stop.Cancel();
            lock (_gate) _nextFrame.TrySetCanceled();
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
            catch (Win32Exception)
            {
            }

            _ = FinishAsync();
        }

        public static NativeCaptureSession CreateScreen(PixelRect monitor, bool cursor, int fps, bool retainHdr = false,
            string? includedWindowTitle = null)
        {
            return new("screen", cursor, fps, monitor, retainHdr: retainHdr, includedWindowTitle: includedWindowTitle);
        }

        public static NativeCaptureSession CreateWindow(nint window, bool cursor, int fps = 60, bool retainHdr = false)
        {
            return new("window", cursor, fps, window: window, retainHdr: retainHdr);
        }

        public static NativeCaptureCursorFrame? ReadSharedCursor()
        {
            lock (SharedLock)
            {
                if (_shared is not { } session) return null;
                return session.ReadCursor();
            }
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

        private CaptureSourceFrame Read(CancellationToken token = default)
        {
            AwaitFirstFrame(token);
            lock (_gate)
            {
                var value = CurrentFrames();
                return new(value.Sdr.Width, value.Sdr.Height,
                        [.. CapturedFramePixels.ToStraightBgra(value.Sdr, token)],
                        _geometry!.X, _geometry.Y)
                    { CoordinateBounds = _geometry.Bounds, PixelDensity = _geometry.BackingScale };
            }
        }

        private NativeCaptureSnapshot ReadSnapshot(bool requireHdr = false, CancellationToken token = default)
        {
            AwaitFirstFrame(token);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                var frames = CurrentFrames(requireHdr);
                return new(frames with { Sdr = Copy(frames.Sdr), Hdr = frames.Hdr is { } hdr ? Copy(hdr) : null },
                    _geometry!, _pointer, _cursorPixels is { } pixels ? (byte[])pixels.Clone() : null,
                    _capabilities, _cursor && _capabilities?.SeparateCursor != true);
            }
        }

        public void UseSnapshot(Action<NativeCaptureSnapshot> consume, bool requireHdr = false,
            CancellationToken token = default)
        {
            AwaitFirstFrame(token);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                consume(new(CurrentFrames(requireHdr), _geometry!, _pointer, _cursorPixels, _capabilities,
                    _cursor && _capabilities?.SeparateCursor != true));
            }
        }

        private static CapturedFrame Copy(CapturedFrame frame)
        {
            return frame with { Pixels = frame.Pixels.ToArray() };
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

        private CapturedFrameSet CurrentFrames(bool requireHdr = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_failure is not null) ExceptionDispatchInfo.Throw(_failure);
            if (_sourceReason is not null) throw new IOException(_sourceReason);
            if (requireHdr && _hdrReason is not null) throw new NotSupportedException(_hdrReason);
            return _frames ?? throw new CaptureSourceLostException();
        }

        private async Task ReadFramesAsync()
        {
            try
            {
                var reader = new NativeCapturePacketReader(_process.StandardOutput.BaseStream);
                while (!_stop.IsCancellationRequested)
                {
                    var packet = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
                    var retained = false;
                    try
                    {
                        lock (_gate)
                        {
                            var metadata = packet.Metadata;
                            if (metadata.Capabilities is { } capabilities) _capabilities = capabilities;
                            ObserveExclusionProof();
                            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                            switch (packet.Kind)
                            {
                                case NativeCapturePacketKind.Frame:
                                case NativeCapturePacketKind.Cursor:
                                    break;
                                case NativeCapturePacketKind.Reply:
                                {
                                    if (_replies.Remove(metadata.RequestId, out var reply))
                                        reply.TrySetResult(metadata);
                                    continue;
                                }

                                case NativeCapturePacketKind.Capabilities:
                                {
                                    if (metadata.SourceGeometry is { Width: > 0, Height: > 0 } refreshedSource)
                                        _sourceGeometry = refreshedSource;
                                    if (metadata.Reason == "source-suspended") _sourceReason = metadata.Reason;
                                    if (_retainHdr && metadata.Capabilities is
                                            { Hdr: false, Reason: { Length: > 0 } unavailable })
                                        _hdrReason = unavailable;
                                    if (_replies.Remove(metadata.RequestId, out var reply))
                                        reply.TrySetResult(metadata);
                                    continue;
                                }
                            }

                            if (metadata.Cursor is { } pointer)
                            {
                                _pointer = pointer;
                                if (!packet.Cursor.IsEmpty) _cursorPixels = packet.Cursor.ToArray();
                            }

                            if (packet.Kind == NativeCapturePacketKind.Cursor)
                            {
                                if (_replies.Remove(metadata.RequestId, out var reply)) reply.TrySetResult(metadata);
                                continue;
                            }

                            CapturedFrameSet frames;
                            try
                            {
                                frames = NativeCaptureFrameDecoder.Decode(packet, _stop.Token);
                            }
                            catch (NotSupportedException error)
                            {
                                _hdrReason = error.Message;
                                if (metadata.Sdr is not { } low)
                                {
                                    _sourceReason = error.Message;
                                    var previous = _packet;
                                    _packet = packet;
                                    _sourceGeometry = metadata.SourceGeometry;
                                    _geometry = metadata.Geometry;
                                    retained = true;
                                    previous?.Dispose();
                                    _first.TrySetResult();
                                    continue;
                                }

                                var sdr = new CapturedFrame(low.Width, low.Height, low.Stride,
                                    CapturePixelFormat.Bgra8Srgb, packet.Sdr,
                                    new(low.Color.Hdr, low.Color.SdrWhiteNits),
                                    low.Alpha switch
                                    {
                                        "opaque" => CaptureAlphaMode.Opaque,
                                        "premultiplied" => CaptureAlphaMode.Premultiplied,
                                        _ => CaptureAlphaMode.Straight,
                                    }, (long)packet.Sequence, []);
                                frames = new(sdr, null, Stopwatch.GetTimestamp());
                            }

                            if (frames.Hdr is not null) _hdrReason = null;
                            else if (metadata.Hdr is null && _retainHdr)
                                _hdrReason = _capabilities?.Reason ?? "The source did not provide an HDR frame.";
                            var old = _packet;
                            _packet = packet;
                            retained = true;
                            _frames = frames;
                            _geometry = metadata.Geometry;
                            _sourceGeometry = metadata.SourceGeometry;
                            _configurationId = metadata.ConfigurationId;
                            _frameSequence = packet.Sequence;
                            var arrived = _nextFrame;
                            _nextFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            arrived.TrySetResult();
                            _sourceReason = null;
                            old?.Dispose();
                            _first.TrySetResult();
                        }

                        FrameAvailable?.Invoke();
                    }
                    finally
                    {
                        if (!retained) packet.Dispose();
                    }
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException
                                              or InvalidOperationException or NotSupportedException)
            {
                try
                {
                    if (!_stop.IsCancellationRequested)
                        await _errors.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                }
                catch (Exception waitError) when (waitError is TimeoutException or OperationCanceledException)
                {
                }

                if (_stop.IsCancellationRequested)
                {
                    _first.TrySetCanceled(_stop.Token);
                    return;
                }

                var failure = ClassifyFailure(error);
                Logger.ZLogWarning(error, $"Native capture transport failed. Generation={_generation}");
                lock (_gate)
                {
                    _failure = failure;
                    foreach (var reply in _replies.Values) reply.TrySetException(failure);
                    _replies.Clear();
                }

                _first.TrySetException(failure);
                _exclusionProofReady.TrySetResult(default);
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
            if (code == "source-lost" || (code.Length == 0 && _first.Task.IsCompletedSuccessfully &&
                                          error is EndOfStreamException))
                return new CaptureSourceLostException(error);
            return new IOException(UiText.Get(code switch
            {
                "permission-denied" => "CapturePermissionDenied", "cancelled" => "CaptureSourceCancelled",
                "dependency-missing" => "CaptureDependencyMissing", "timeout" => "CaptureSourceTimeout",
                "unsupported" => "CapturePlatformUnsupported",
                _ => "CaptureFailed",
            }), error);
        }

        private async Task FinishAsync()
        {
            try
            {
                await Task.WhenAll(_reader, _errors);
            }
            finally
            {
                lock (_gate)
                {
                    _packet?.Dispose();
                    _packet = null;
                    foreach (var reply in _replies.Values) reply.TrySetCanceled();
                    _replies.Clear();
                }

                _process.Dispose();

                await _commands.WaitAsync().ConfigureAwait(false);
                _commands.Release();
                _commands.Dispose();
                await _configurationGate.WaitAsync().ConfigureAwait(false);
                _configurationGate.Release();
                _configurationGate.Dispose();
                _stop.Dispose();
            }
        }
    }
}
