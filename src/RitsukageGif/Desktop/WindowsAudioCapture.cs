using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowsAudioCapture : IAudioCaptureSource
    {
        private readonly Lock _lifetime = new();

        private readonly Channel<(byte[] Data, int Length)> _queue = Channel.CreateBounded<(byte[], int)>(
            new BoundedChannelOptions(512)
            {
                SingleReader = false,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        private readonly TaskCompletionSource<AudioCaptureFormat> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        private int _consumerExited;
        private int _disposed;
        private long _framesWritten;
        private int _queuedBytes;
        private bool _resourcesDisposed;
        private int _running;
        private long _startTimestamp;

        private WindowsAudioCapture(bool loopback, string? deviceId)
        {
            _worker = Task.Factory.StartNew(() => Capture(loopback, deviceId), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private byte SilenceByte => Format is { BitsPerSample: 8, IsFloat: false } ? (byte)128 : (byte)0;

        public AudioCaptureFormat Format { get; private set; } = null!;

        public async Task RunAsync(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consume,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(consume);
            token.ThrowIfCancellationRequested();
            lock (_lifetime)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (Interlocked.Exchange(ref _running, 1) != 0)
                    throw new InvalidOperationException("Audio capture already started.");
            }

            var registration = token.Register(Cancel);
            try
            {
                _start.TrySetResult();
                await foreach (var packet in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                {
                    Interlocked.Add(ref _queuedBytes, -packet.Length);
                    try
                    {
                        await consume(packet.Data.AsMemory(0, packet.Length), _stop.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(packet.Data);
                    }
                }
            }
            finally
            {
                Cancel();
                await _worker.ConfigureAwait(false);
                Drain();
                await registration.DisposeAsync().ConfigureAwait(false);
                Volatile.Write(ref _consumerExited, 1);
                ReleaseResources();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Cancel();
            _worker.GetAwaiter().GetResult();
            Drain();
            ReleaseResources();
        }

        public static WindowsAudioCapture Create(bool loopback, string? deviceId = null)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var result = new WindowsAudioCapture(loopback, deviceId);
            try
            {
                result.Format = result._ready.Task.GetAwaiter().GetResult();
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        public static IReadOnlyList<AudioCaptureDevice> EnumerateDevices(bool loopback)
        {
            if (!OperatingSystem.IsWindows()) return [];
            return Task.Run(() => WindowsAudioCaptureDevice.Enumerate(loopback)).GetAwaiter().GetResult();
        }

        private void Capture(bool loopback, string? deviceId)
        {
            Exception? failure = null;
            try
            {
                using var signal = new AutoResetEvent(false);
                using var device = new WindowsAudioCaptureDevice(loopback, deviceId, signal);
                Format = device.Format;
                _ready.TrySetResult(Format);
                _start.Task.Wait(_stop.Token);
                _stop.Token.ThrowIfCancellationRequested();
                _startTimestamp = Now();
                device.Start();
                WaitHandle[] handles = [signal, _stop.Token.WaitHandle];
                var consume = Receive;
                while (!_stop.IsCancellationRequested)
                {
                    if (WaitHandle.WaitAny(handles, 100) == 1) break;
                    while (!_stop.IsCancellationRequested && device.Read(consume))
                    {
                    }

                    var elapsed = Now() - _startTimestamp;
                    var targetFrames = Math.Max(0, elapsed - 1_000_000) * Format.SampleRate / 10_000_000;
                    SilenceUntil(targetFrames);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                _ready.TrySetCanceled(_stop.Token);
            }
            catch (Exception exception)
            {
                failure = exception;
                _ready.TrySetException(exception);
            }
            finally
            {
                _queue.Writer.TryComplete(failure);
            }
        }

        private void Receive(nint data, uint frameCount, uint flags, ulong timestamp)
        {
            var frameBytes = Format.Channels * Format.BitsPerSample / 8;
            var startFrame = _framesWritten;
            if ((flags & 4) == 0 && timestamp <= long.MaxValue)
            {
                var relative = (long)timestamp - _startTimestamp;
                if (relative >= -10_000_000 && relative <= Now() - _startTimestamp + 2_500_000)
                    startFrame = (long)Math.Round(relative * (double)Format.SampleRate / 10_000_000);
            }

            SilenceUntil(Math.Max(0, startFrame));
            var skip = Math.Min(frameCount, Math.Max(0, _framesWritten - startFrame));
            var frames = checked((int)(frameCount - skip));
            if (frames == 0) return;
            var length = checked(frames * frameBytes);
            if (length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid audio packet length.");
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                if ((flags & 2) != 0) buffer.AsSpan(0, length).Fill(SilenceByte);
                else Marshal.Copy(data + checked((int)(skip * frameBytes)), buffer, 0, length);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }

            Enqueue(buffer, length);
            _framesWritten += frames;
        }

        private void SilenceUntil(long target)
        {
            var frameBytes = Format.Channels * Format.BitsPerSample / 8;
            while (_framesWritten < target && !_stop.IsCancellationRequested)
            {
                var frames = (int)Math.Min(target - _framesWritten, Format.SampleRate / 10);
                var length = frames * frameBytes;
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                buffer.AsSpan(0, length).Fill(SilenceByte);
                Enqueue(buffer, length);
                _framesWritten += frames;
            }
        }

        private void Enqueue(byte[] buffer, int length)
        {
            var queued = Interlocked.Add(ref _queuedBytes, length);
            if (queued <= 16 * 1024 * 1024 && _queue.Writer.TryWrite((buffer, length))) return;
            Interlocked.Add(ref _queuedBytes, -length);
            ArrayPool<byte>.Shared.Return(buffer);
            throw new IOException("Audio encoder cannot keep up with the capture device.");
        }

        private void Drain()
        {
            while (_queue.Reader.TryRead(out var packet))
            {
                Interlocked.Add(ref _queuedBytes, -packet.Length);
                ArrayPool<byte>.Shared.Return(packet.Data);
            }
        }

        private void Cancel()
        {
            lock (_lifetime)
                if (!_resourcesDisposed)
                    _stop.Cancel();
        }

        private void ReleaseResources()
        {
            lock (_lifetime)
            {
                if (_resourcesDisposed || Volatile.Read(ref _disposed) == 0
                                       || (Volatile.Read(ref _running) != 0 && Volatile.Read(ref _consumerExited) == 0))
                    return;
                _resourcesDisposed = true;
                _stop.Dispose();
            }
        }

        private static long Now()
        {
            return (long)(Stopwatch.GetTimestamp() * (10_000_000.0 / Stopwatch.Frequency));
        }
    }
}
