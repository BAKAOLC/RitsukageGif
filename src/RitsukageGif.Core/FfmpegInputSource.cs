using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RitsukageGif.Core
{
    public sealed class FfmpegInputSource : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly FfmpegInputOptions _options;
        private readonly string _executable;
        private readonly Task _reader;
        private byte[]? _latest;
        private long _updated;
        private bool _completed;
        private bool _failed;
        private int _disposed;

        private FfmpegInputSource(string executable, FfmpegInputOptions options, int width, int height)
        {
            _executable = executable;
            _options = options;
            Width = width;
            Height = height;
            _reader = Task.Run(ReadAsync);
        }

        public int Width { get; }
        public int Height { get; }

        public bool NeedsReconnect
        {
            get
            {
                lock (_gate) return _completed && (_failed || _options.Live);
            }
        }

        public static async Task<FfmpegInputSource> CreateAsync(FfmpegInputOptions options,
            CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.Input);
            if (!double.IsFinite(options.PlaybackRate) || options.PlaybackRate is <= 0 or > 16
                                                       || !double.IsFinite(options.PositionSeconds) ||
                                                       options.PositionSeconds < 0
                                                       || !double.IsFinite(options.StaleAfterSeconds) ||
                                                       options.StaleAfterSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options));
            var executable = FfmpegEncoder.FindExecutable()
                             ?? throw new FfmpegDependencyException(UiText.Get("FfmpegMissing"));
            var width = options.Width;
            var height = options.Height;
            if (width == 0 && height == 0)
                (width, height) = await ProbeAsync(executable, options.Input, token);
            if (width is <= 0 or > 16384 || height is <= 0 or > 16384 || (long)width * height > 33554432)
                throw new ArgumentOutOfRangeException(nameof(options));
            var source = new FfmpegInputSource(executable, options, width, height);
            try
            {
                await source._firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                return source;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }

        public byte[]? Capture()
        {
            lock (_gate)
            {
                if (_disposed != 0 || _latest is null) return null;
                if (!_options.HoldLastFrame && (_completed
                                                || Stopwatch.GetElapsedTime(_updated).TotalSeconds >
                                                _options.StaleAfterSeconds)) return null;
                return (byte[])_latest.Clone();
            }
        }

        private async Task ReadAsync()
        {
            try
            {
                for (var attempt = 0;; attempt++)
                {
                    try
                    {
                        await ReadProcessAsync(_stop.Token);
                        if (!_options.Live) break;
                    }
                    catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
                    {
                        if (!_options.Live || attempt >= 3) throw;
                    }

                    if (attempt >= 3) break;
                    await Task.Delay(TimeSpan.FromSeconds(1 << attempt), _stop.Token);
                }

                _firstFrame.TrySetException(new EndOfStreamException());
            }
            catch (OperationCanceledException)
            {
                _firstFrame.TrySetCanceled();
            }
            catch (Exception error)
            {
                lock (_gate) _failed = true;
                _firstFrame.TrySetException(error);
            }
            finally
            {
                lock (_gate) _completed = true;
            }
        }

        private async Task ReadProcessAsync(CancellationToken token)
        {
            using var process = new Process();
            process.StartInfo = StartInfo(_executable);
            var args = process.StartInfo.ArgumentList;
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin" }) args.Add(argument);
            if (!_options.Live)
            {
                args.Add("-readrate");
                args.Add(_options.PlaybackRate.ToString(CultureInfo.InvariantCulture));
                if (_options.Loop)
                {
                    args.Add("-stream_loop");
                    args.Add("-1");
                }

                if (_options.PositionSeconds > 0)
                {
                    args.Add("-ss");
                    args.Add(_options.PositionSeconds.ToString(CultureInfo.InvariantCulture));
                }
            }

            foreach (var argument in new[]
                     {
                         "-i", _options.Input, "-map", "0:v:0", "-an", "-sn", "-dn",
                         "-vf", $"scale={Width}:{Height}", "-fps_mode", "passthrough", "-pix_fmt", "bgra",
                         "-f", "rawvideo", "pipe:1"
                     }) args.Add(argument);
            token.ThrowIfCancellationRequested();
            process.Start();
            await using var registration = token.Register(static state => Kill((Process)state!), process);
            var errors = DrainAsync(process.StandardError, token);
            var frame = new byte[checked(Width * Height * 4)];
            try
            {
                while (true)
                {
                    var read = 0;
                    while (read < frame.Length)
                    {
                        var count = await process.StandardOutput.BaseStream.ReadAsync(frame.AsMemory(read), token)
                            .AsTask().WaitAsync(TimeSpan.FromSeconds(Math.Max(5, _options.StaleAfterSeconds)), token);
                        if (count == 0) break;
                        read += count;
                    }

                    if (read == 0) break;
                    if (read != frame.Length) throw new EndOfStreamException();
                    lock (_gate)
                    {
                        _latest ??= new byte[frame.Length];
                        frame.CopyTo(_latest, 0);
                        _updated = Stopwatch.GetTimestamp();
                    }

                    _firstFrame.TrySetResult();
                }

                await process.WaitForExitAsync(token);
                var detail = await errors;
                if (process.ExitCode != 0) throw new IOException(detail);
            }
            finally
            {
                Kill(process);
                try
                {
                    await errors;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private static async Task<(int Width, int Height)> ProbeAsync(string executable, string input,
            CancellationToken token)
        {
            var probe = Path.Combine(Path.GetDirectoryName(executable)!,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (!File.Exists(probe)) throw new FfmpegDependencyException(UiText.Get("FfprobeMissing"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var process = new Process();
            process.StartInfo = StartInfo(probe);
            foreach (var argument in new[]
                     {
                         "-v", "error", "-select_streams", "v:0", "-show_entries",
                         "stream=width,height", "-of", "json", "-i", input
                     }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            await using var registration = timeout.Token.Register(static state => Kill((Process)state!), process);
            var errors = DrainAsync(process.StandardError, timeout.Token);
            try
            {
                var output = await DrainAsync(process.StandardOutput, timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var detail = await errors;
                if (process.ExitCode != 0) throw new IOException(detail);
                using var document = JsonDocument.Parse(output);
                var streams = document.RootElement.GetProperty("streams");
                if (streams.GetArrayLength() == 0) throw new InvalidDataException();
                return (streams[0].GetProperty("width").GetInt32(), streams[0].GetProperty("height").GetInt32());
            }
            finally
            {
                Kill(process);
                try
                {
                    await errors;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        internal static ProcessStartInfo StartInfo(string executable) => new(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };

        internal static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
        {
            var buffer = new char[1024];
            var result = new System.Text.StringBuilder();
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                result.Append(buffer, 0, count);
                if (result.Length > 8192) result.Remove(0, result.Length - 8192);
            }

            return result.ToString();
        }

        internal static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            // The worker owns its process. Dispose only after cancellation callbacks and reads finish.
            _ = _reader.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            lock (_gate) _latest = null;
        }
    }
}
