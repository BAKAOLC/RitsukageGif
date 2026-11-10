using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RitsukageGif.Desktop
{
    // image2pipe carries the negotiated camera dimensions without probing or opening the device twice.
    internal sealed class RecordingCameraSource : IAsyncDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.RecordingCamera");
        private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _gate = new();
        private readonly Task _reader;
        private readonly CancellationTokenSource _stop;
        private int _disposed;
        private PreviewFrame? _latest;
        private long _updated;

        private RecordingCameraSource(string executable, RecordingCameraDevice device, CancellationToken token)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            _reader = Task.Run(() => ReadAsync(executable, device), CancellationToken.None);
        }

        private string? Failure { get; set; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                await _reader.ConfigureAwait(false);
                return;
            }

            await _stop.CancelAsync().ConfigureAwait(false);
            await _reader.ConfigureAwait(false);
            lock (_gate)
            {
                _latest?.Dispose();
                _latest = null;
            }

            _stop.Dispose();
        }

        internal event Action? FrameAvailable;

        internal static async Task<RecordingCameraSource> CreateAsync(RecordingCameraDevice device,
            CancellationToken token, CancellationToken startupToken = default)
        {
            var executable = FfmpegEncoder.FindExecutable()
                             ?? throw new IOException("FFmpeg is unavailable.");
            var source = new RecordingCameraSource(executable, device, token);
            try
            {
                await source._firstFrame.Task.WaitAsync(startupToken).ConfigureAwait(false);
                return source;
            }
            catch
            {
                await source.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal PreviewFrame? Read()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                if (Failure is { } failure) throw new IOException(failure);
                if (_latest is not null && Stopwatch.GetElapsedTime(_updated) > TimeSpan.FromSeconds(5))
                    throw new IOException("Camera stream is not producing frames.");
                return _latest?.Retain();
            }
        }

        private async Task ReadAsync(string executable, RecordingCameraDevice device)
        {
            var token = _stop.Token;
            try
            {
                using var process = new Process();
                process.StartInfo = RecordingCameraProcess.StartInfo(executable);
                foreach (var argument in new[]
                         {
                             "-hide_banner", "-loglevel", "error", "-nostdin", "-f", device.InputFormat, "-i",
                             device.Input, "-map", "0:v:0", "-an", "-sn", "-dn", "-c:v", "pam", "-pix_fmt", "rgba",
                             "-fps_mode", "passthrough", "-f", "image2pipe", "pipe:1",
                         })
                    process.StartInfo.ArgumentList.Add(argument);
                token.ThrowIfCancellationRequested();
                process.Start();
                Logger.ZLogInformation($"Camera reader opened: inputFormat={device.InputFormat}");
                // ReSharper disable once AccessToDisposedClosure
                await using var registration = token.Register(() => RecordingCameraProcess.Kill(process));
                var errors = RecordingCameraProcess.DrainAsync(process.StandardError, CancellationToken.None);
                try
                {
                    await using var stream = new BufferedStream(process.StandardOutput.BaseStream, 65536);
                    var lineBuffer = new byte[1];
                    while (await ReadFrameAsync(stream, lineBuffer, token).ConfigureAwait(false))
                        _firstFrame.TrySetResult();
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                    var detail = await errors.ConfigureAwait(false);
                    throw new IOException(process.ExitCode == 0
                        ? "Camera stream ended."
                        : $"Camera capture failed: {detail}");
                }
                finally
                {
                    RecordingCameraProcess.Kill(process);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    await errors.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _firstFrame.TrySetCanceled(token);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                _firstFrame.TrySetCanceled(token);
            }
            catch (Exception error)
            {
                Logger.ZLogError(
                    $"Camera reader failed: exceptionType={error.GetType().Name}, hResult={error.HResult}, stackTrace={error.StackTrace}");
                lock (_gate) Failure = error.Message;
                _firstFrame.TrySetException(error);
                FrameAvailable?.Invoke();
            }
        }

        private async Task<bool> ReadFrameAsync(Stream stream, byte[] lineBuffer, CancellationToken token)
        {
            var magic = await ReadLineAsync(stream, lineBuffer, token).ConfigureAwait(false);
            if (magic is null) return false;
            if (magic != "P7") throw new InvalidDataException("Invalid camera PAM frame.");
            var width = 0;
            var height = 0;
            var depth = 0;
            var maximum = 0;
            string? line;
            while ((line = await ReadLineAsync(stream, lineBuffer, token).ConfigureAwait(false)) != "ENDHDR")
            {
                if (line is null) throw new EndOfStreamException("Incomplete camera PAM header.");
                var split = line.IndexOf(' ');
                if (split < 0) continue;
                var key = line[..split];
                if (!int.TryParse(line.AsSpan(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var value)) continue;
                switch (key)
                {
                    case "WIDTH": width = value; break;
                    case "HEIGHT": height = value; break;
                    case "DEPTH": depth = value; break;
                    case "MAXVAL": maximum = value; break;
                }
            }

            if (width is <= 0 or > 16384 || height is <= 0 or > 16384 || (long)width * height > 33554432
                || depth != 4 || maximum != 255)
                throw new InvalidDataException("Unsupported camera PAM frame format.");
            var length = checked(width * height * 4);
            var pixels = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                await stream.ReadExactlyAsync(pixels.AsMemory(0, length), token).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                for (var index = 0; index < length; index += 4)
                    (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
                var frame = new PreviewFrame(pixels, width, height, 0);
                lock (_gate)
                {
                    _latest?.Dispose();
                    _latest = frame;
                    _updated = Stopwatch.GetTimestamp();
                }
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(pixels);
                throw;
            }

            FrameAvailable?.Invoke();
            return true;
        }

        private static async Task<string?> ReadLineAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            var line = new StringBuilder();
            while (await stream.ReadAsync(buffer, token).ConfigureAwait(false) > 0)
            {
                if (buffer[0] == '\n') return line.ToString();
                if (line.Length >= 1024) throw new InvalidDataException("Invalid camera PAM header length.");
                if (buffer[0] != '\r') line.Append((char)buffer[0]);
            }

            if (line.Length != 0) throw new EndOfStreamException("Incomplete camera PAM header.");
            return null;
        }
    }
}
