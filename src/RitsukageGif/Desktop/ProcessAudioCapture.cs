using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RitsukageGif.Desktop
{
    internal sealed class ProcessAudioCapture(ProcessStartInfo start, bool alignStart = false) : IAudioCaptureSource
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Lock _lifetime = new();
        private bool _disposed;
        private bool _finished;
        private int _running;

        public AudioCaptureFormat Format { get; } = new(48000, 2, 32, true);

        public void Dispose()
        {
            lock (_lifetime)
            {
                if (_disposed) return;
                _disposed = true;
                _stop.Cancel();
                if (_finished || _running == 0) _stop.Dispose();
            }
        }

        public async Task RunAsync(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consume,
            CancellationToken token)
        {
            lock (_lifetime)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (Interlocked.Exchange(ref _running, 1) != 0)
                    throw new InvalidOperationException("Audio capture already started.");
            }

            try
            {
                await CaptureAsync(consume, token);
            }
            finally
            {
                lock (_lifetime)
                {
                    _finished = true;
                    if (_disposed) _stop.Dispose();
                }
            }
        }

        private async Task CaptureAsync(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consume,
            CancellationToken token)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            var stop = cancellation.Token;
            stop.ThrowIfCancellationRequested();
            using var process = new Process();
            process.StartInfo = start;
            var clock = Stopwatch.StartNew();
            process.Start();
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await using var registration = stop.Register(() =>
            {
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    if (!process.HasExited)
                        // ReSharper disable once AccessToDisposedClosure
                        process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }
            });
            try
            {
                var buffer = new byte[38400];
                var input = process.StandardOutput.BaseStream;
                var first = true;
                int length;
                while ((length = await input.ReadAsync(buffer, stop)) != 0)
                {
                    if (first && alignStart)
                    {
                        var missingFrames = Math.Max(0,
                            (long)(clock.Elapsed.TotalSeconds * Format.SampleRate) - length / 8);
                        var silence = new byte[38400];
                        while (missingFrames > 0)
                        {
                            var frames = (int)Math.Min(missingFrames, silence.Length / 8);
                            await consume(silence.AsMemory(0, frames * 8), stop);
                            missingFrames -= frames;
                        }
                    }

                    first = false;
                    await consume(buffer.AsMemory(0, length), stop);
                }

                await process.WaitForExitAsync(stop);
                stop.ThrowIfCancellationRequested();
                throw new IOException(UiText.Get("VideoAudioEnded") + " " + (await errors).Trim());
            }
            finally
            {
                if (!process.HasExited)
                    // ReSharper disable once AccessToDisposedClosure
                    process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
                await errors;
            }
        }

        internal static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            return start;
        }

        [SuppressMessage("Design", "CA1068",
            Justification = "The cancellation token precedes the final variadic native process argument list.")]
        internal static string Query(string executable, CancellationToken token, bool waitForPermission,
            params string[] arguments)
        {
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(StartInfo(executable, arguments)) ??
                                throw new IOException(UiText.Get("VideoAudioUnavailable"));
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var registration = token.Register(() =>
            {
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    if (!process.HasExited)
                        // ReSharper disable once AccessToDisposedClosure
                        process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }
            });
            if (!process.WaitForExit(waitForPermission ? Timeout.Infinite : 10000))
            {
                process.Kill(true);
                process.WaitForExit();
                output.GetAwaiter().GetResult();
                errors.GetAwaiter().GetResult();
                throw new TimeoutException(UiText.Get("VideoAudioUnavailable"));
            }

            var result = output.GetAwaiter().GetResult();
            var error = errors.GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException(error.Trim());
            return result;
        }
    }
}
