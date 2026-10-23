using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace RitsukageGif.Core
{
    public sealed class RecordingSession(Func<RecordingOptions, IFrameSource> sourceFactory)
    {
        private readonly record struct CapturedFrame(byte[]? Pixels, string? Path, double Time);

        private sealed class EncodingProgress(Action<RecordingProgress> report) : IProgress<RecordingProgress>
        {
            public void Report(RecordingProgress value) => report(value);
        }

        public async Task RunAsync(RecordingOptions options, string outputPath,
            IProgress<RecordingProgress>? progress, CancellationToken stop, CancellationToken cancel)
        {
            options.Validate();
            var directory = Path.Combine(Path.GetTempPath(), "RitsukageGif", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var staging = Path.Combine(directory, "output." + options.Format.ToString().ToLowerInvariant());
            using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            var abortToken = abort.Token;
            using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(stop, abortToken);
            var captureToken = captureStop.Token;
            var liveGif = options.Format == OutputFormat.Gif &&
                          !(options.UseFfmpeg && FfmpegEncoder.FindExecutable() is not null);
            var capacity = Math.Clamp((int)(64L * 1024 * 1024 / ((long)options.Width * options.Height * 4)), 1, 8);
            using var memorySlots = new SemaphoreSlim(capacity, capacity);
            var channel = liveGif
                ? Channel.CreateUnbounded<CapturedFrame>(new UnboundedChannelOptions
                    { SingleReader = true, SingleWriter = true })
                : Channel.CreateBounded<CapturedFrame>(new BoundedChannelOptions(capacity)
                    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            var frames = new List<StoredFrame>();
            var capturedCount = 0;
            var encodedCount = 0;
            var capturedFps = 0.0;
            var elapsedSeconds = 0.0;
            ExceptionDispatchInfo? captureFailure = null;

            void Report(RecordingStage stage) => progress?.Report(new RecordingProgress(stage,
                Volatile.Read(ref capturedCount), Volatile.Read(ref encodedCount),
                Volatile.Read(ref capturedFps), Volatile.Read(ref elapsedSeconds)));

            var encodingProgress = progress is null
                ? null
                : new EncodingProgress(value =>
                {
                    Volatile.Write(ref encodedCount, value.Encoded);
                    Report(value.Stage);
                });
            var clock = Stopwatch.StartNew();
            long stoppedAt = -1;
            using var stopRegistration =
                stop.Register(() => Interlocked.CompareExchange(ref stoppedAt, clock.ElapsedTicks, -1));
            var endTime = 0.0;
            var producer = Task.Run(async () =>
            {
                try
                {
                    using var source = sourceFactory(options);
                    var period = 1000.0 / options.Fps;
                    var next = clock.Elapsed.TotalMilliseconds;
                    var origin = double.NaN;
                    var sequence = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        abortToken.ThrowIfCancellationRequested();
                        var remaining = next - clock.Elapsed.TotalMilliseconds;
                        if (remaining > 0)
                        {
                            try
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(remaining), captureToken);
                            }
                            catch (OperationCanceledException) when (stop.IsCancellationRequested &&
                                                                     !abortToken.IsCancellationRequested)
                            {
                                break;
                            }
                        }

                        if (stop.IsCancellationRequested) break;
                        byte[] pixels;
                        try
                        {
                            pixels = source.Capture(captureToken);
                        }
                        catch (OperationCanceledException) when (stop.IsCancellationRequested &&
                                                                 !abortToken.IsCancellationRequested)
                        {
                            break;
                        }

                        abortToken.ThrowIfCancellationRequested();
                        if (stop.IsCancellationRequested) break;
                        var timestamp = clock.Elapsed.TotalMilliseconds;
                        if (double.IsNaN(origin))
                        {
                            origin = timestamp;
                            next = timestamp;
                        }

                        CapturedFrame frame;
                        // The producer is awaited in finally before the semaphore leaves its using scope.
                        // ReSharper disable once AccessToDisposedClosure
                        if (liveGif && !memorySlots.Wait(0))
                        {
                            var path = Path.Combine(directory, $"capture-{sequence:D8}.bgra");
                            await File.WriteAllBytesAsync(path, pixels, abortToken);
                            frame = new CapturedFrame(null, path, timestamp - origin);
                        }
                        else frame = new CapturedFrame(pixels, null, timestamp - origin);

                        sequence++;
                        Volatile.Write(ref elapsedSeconds, (timestamp - origin) / 1000);
                        Volatile.Write(ref capturedFps, sequence > 1 && timestamp > origin
                            ? (sequence - 1) * 1000 / (timestamp - origin)
                            : 0);
                        Volatile.Write(ref capturedCount, sequence);
                        await channel.Writer.WriteAsync(frame, abortToken);
                        Report(RecordingStage.Recording);
                        next += period;
                        var behind = clock.Elapsed.TotalMilliseconds - next;
                        if (behind >= period) next += Math.Floor(behind / period) * period;
                    }

                    var stopTicks = Interlocked.Read(ref stoppedAt);
                    var ended = stopTicks < 0
                        ? clock.Elapsed.TotalMilliseconds
                        : stopTicks * 1000.0 / Stopwatch.Frequency;
                    endTime = double.IsNaN(origin) ? 0 : Math.Max(0, ended - origin);
                    Volatile.Write(ref elapsedSeconds, endTime / 1000);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    // Publish the first producer failure atomically for the concurrent consumer's catch filter.
                    // ReSharper disable once AccessToModifiedClosure
                    Interlocked.CompareExchange(ref captureFailure, ExceptionDispatchInfo.Capture(ex), null);
                    channel.Writer.TryComplete(ex);
                    // The enclosing finally awaits this producer before disposing its cancellation source.
                    // ReSharper disable once AccessToDisposedClosure
                    abort.Cancel();
                    throw;
                }
            }, CancellationToken.None);
            try
            {
                if (liveGif)
                {
                    async IAsyncEnumerable<RawFrame> ReadLiveFrames()
                    {
                        (byte[] Pixels, double Time)? pending = null;
                        await foreach (var frame in channel.Reader.ReadAllAsync(abortToken))
                        {
                            var pixels = frame.Pixels;
                            if (pixels is null)
                            {
                                pixels = await File.ReadAllBytesAsync(frame.Path!, abortToken);
                                File.Delete(frame.Path!);
                            }
                            // Encoding and its frame iterator finish before the semaphore is disposed.
                            // ReSharper disable once AccessToDisposedClosure
                            else memorySlots.Release();

                            if (pending is { } previousFrame)
                                yield return new RawFrame(previousFrame.Pixels,
                                    Math.Max(1, (int)(Math.Round(frame.Time) - Math.Round(previousFrame.Time))));
                            pending = (pixels, frame.Time);
                        }

                        await producer;
                        if (pending is { } lastFrame)
                            yield return new RawFrame(lastFrame.Pixels,
                                Math.Max(1, (int)(Math.Round(endTime) - Math.Round(lastFrame.Time))));
                    }

                    await StreamingGifEncoder.EncodeAsync(ReadLiveFrames(), staging, options, encodingProgress,
                        abortToken);
                }
                else
                {
                    double previous = 0;
                    await foreach (var frame in channel.Reader.ReadAllAsync(abortToken))
                    {
                        if (frames.Count > 0)
                            frames[^1] = frames[^1] with
                            {
                                DelayMs = Math.Max(1, (int)(Math.Round(frame.Time) - Math.Round(previous)))
                            };
                        var path = Path.Combine(directory, $"{frames.Count:D8}.bmp");
                        await BitmapSpool.WriteAsync(path, frame.Pixels!, options.Width, options.Height, abortToken);
                        previous = frame.Time;
                        frames.Add(new StoredFrame(path, Math.Max(1, 1000 / options.Fps)));
                    }

                    await producer;
                    if (frames.Count == 0) throw new InvalidOperationException(UiText.Get("NoFrames"));
                    frames[^1] = frames[^1] with
                    {
                        DelayMs = Math.Max(1, (int)(Math.Round(endTime) - Math.Round(previous)))
                    };
                    await AnimationEncoder.EncodeAsync(frames, staging, options, encodingProgress, abortToken);
                }

                cancel.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                File.Move(staging, outputPath, false);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested &&
                                                     Volatile.Read(ref captureFailure) is not null)
            {
                captureFailure!.Throw();
                throw;
            }
            finally
            {
                abort.Cancel();
                try
                {
                    await producer;
                }
                catch (Exception producerFailure)
                {
                    // The channel reader has already observed this failure; preserve its stack trace.
                    Trace.TraceInformation("Capture producer stopped: {0}", producerFailure.Message);
                }

                while (channel.Reader.TryRead(out _))
                {
                }

                try
                {
                    Directory.Delete(directory, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
