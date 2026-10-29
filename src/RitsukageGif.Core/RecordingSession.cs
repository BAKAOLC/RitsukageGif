using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace RitsukageGif.Core
{
    public sealed class RecordingSession(Func<RecordingOptions, IFrameSource> sourceFactory)
    {
        private const long BacklogDiskLimit = 4L * 1024 * 1024 * 1024;
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Capture");

        public async Task RunAsync(RecordingOptions options, string outputPath,
            IProgress<RecordingProgress>? progress, CancellationToken stop, CancellationToken cancel)
        {
            options.Validate();
            Logger.ZLogInformation(
                $"Animation recording started: width={options.Width}, height={options.Height}, fps={options.Fps}, format={options.Format.ToString()}, externalEncoderRequested={options.UseFfmpeg}");
            var directory = Path.Combine(Path.GetTempPath(), "RitsukageGif", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var staging = Path.Combine(directory, "output." + options.Format.ToString().ToLowerInvariant());
            using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            var abortToken = abort.Token;
            using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(stop, abortToken);
            var captureToken = captureStop.Token;
            var live = !(options.UseFfmpeg && FfmpegEncoder.FindExecutable() is not null);
            var capacity = Math.Clamp((int)(64L * 1024 * 1024 / ((long)options.Width * options.Height * 4)), 1, 8);
            using var memorySlots = new SemaphoreSlim(capacity, capacity);
            var channel = Channel.CreateBounded<CapturedFrame>(new BoundedChannelOptions(100000)
                { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            var frames = new List<StoredFrame>();
            var capturedCount = 0;
            var encodedCount = 0;
            var capturedFps = 0.0;
            var elapsedSeconds = 0.0;
            var captureCompleted = 0;
            long diskBytes = 0;
            ExceptionDispatchInfo? captureFailure = null;
            var clock = Stopwatch.StartNew();
            long nextLogMilliseconds = 5000;

            var encodingProgress = progress is null
                ? null
                : new EncodingProgress(value =>
                {
                    Volatile.Write(ref encodedCount, value.Encoded);
                    Report(Volatile.Read(ref captureCompleted) == 0 ? RecordingStage.Live : RecordingStage.Encoding);
                });
            long stoppedAt = -1;
            await using var stopRegistration =
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
                            try
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(remaining), captureToken);
                            }
                            catch (OperationCanceledException) when (stop.IsCancellationRequested &&
                                                                     !abortToken.IsCancellationRequested)
                            {
                                break;
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
                        if (pixels.Length != checked(options.Width * options.Height * 4))
                            throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
                        var timestamp = clock.Elapsed.TotalMilliseconds;
                        if (double.IsNaN(origin))
                        {
                            origin = timestamp;
                            next = timestamp;
                            Logger.ZLogInformation(
                                $"Animation capture produced its first frame: width={options.Width}, height={options.Height}, liveEncoding={live}");
                        }

                        CapturedFrame frame;
                        // The producer is awaited in finally before the semaphore leaves its using scope.
                        // ReSharper disable once AccessToDisposedClosure
                        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
                        if (!memorySlots.Wait(0))
                        {
                            ReserveBacklogDisk(pixels.Length);
                            var path = Path.Combine(directory, $"capture-{sequence:D8}.bgra");
                            await File.WriteAllBytesAsync(path, pixels, abortToken);
                            frame = new(null, path, timestamp - origin);
                        }
                        else
                        {
                            frame = new(pixels, null, timestamp - origin);
                        }

                        sequence++;
                        Volatile.Write(ref elapsedSeconds, (timestamp - origin) / 1000);
                        Volatile.Write(ref capturedFps, sequence > 1 && timestamp > origin
                            ? (sequence - 1) * 1000 / (timestamp - origin)
                            : 0);
                        Volatile.Write(ref capturedCount, sequence);
                        if (!channel.Writer.TryWrite(frame))
                            throw new InvalidOperationException(UiText.Get("MemoryLimit"));
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
                    Volatile.Write(ref captureCompleted, 1);
                    Logger.ZLogInformation(
                        $"Animation capture stopped; draining encoder: captured={sequence}, elapsedSeconds={endTime / 1000}");
                    Report(RecordingStage.Encoding);
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
                    // ReSharper disable once MethodHasAsyncOverload
                    abort.Cancel();
                    throw;
                }
            }, CancellationToken.None);

            try
            {
                if (live)
                    await StreamingAnimationEncoder.EncodeAsync(ReadFrames(), staging, options, encodingProgress,
                        abortToken);
                else
                    await Task.Run(async () =>
                    {
                        await foreach (var raw in ReadFrames())
                        {
                            // Completed frame storage is limited by the volume, not the live backlog budget.
                            var path = Path.Combine(directory, $"{frames.Count:D8}.bmp");
                            await BitmapSpool.WriteAsync(path, raw.Pixels, options.Width, options.Height, abortToken);
                            frames.Add(new(path, raw.DelayMs));
                        }

                        await AnimationEncoder.EncodeAsync(frames, staging, options, encodingProgress, abortToken);
                    }, abortToken);

                cancel.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                File.Move(staging, outputPath, false);
                Logger.ZLogInformation(
                    $"Animation recording published: output={outputPath}, frames={capturedCount}, elapsedSeconds={elapsedSeconds}");
                Report(RecordingStage.Complete);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested &&
                                                     Volatile.Read(ref captureFailure) is not null)
            {
                var failure = captureFailure!.SourceException;
                Logger.ZLogError(failure,
                    $"Animation capture failed: exceptionType={failure.GetType().Name}, hResult={failure.HResult}");
                captureFailure!.Throw();
                throw;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                Logger.ZLogInformation(
                    $"Animation recording canceled: captured={capturedCount}, encoded={encodedCount}");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Animation recording failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                // ReSharper disable once MethodHasAsyncOverload
                abort.Cancel();
                try
                {
                    await producer;
                }
                catch (Exception producerFailure)
                {
                    // The channel reader has already observed this failure; preserve its stack trace.
                    Logger.ZLogDebug(
                        $"Animation capture producer teardown observed: exceptionType={producerFailure.GetType().Name}, hResult={producerFailure.HResult}");
                }

                while (channel.Reader.TryRead(out _))
                {
                }

                try
                {
                    Directory.Delete(directory, true);
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (IOException error)
                {
                    Logger.ZLogWarning(error,
                        $"Animation temporary storage cleanup deferred: directory={directory}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                }
                catch (UnauthorizedAccessException error)
                {
                    Logger.ZLogWarning(error,
                        $"Animation temporary storage cleanup denied: directory={directory}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                }
            }

            return;

            void ReserveBacklogDisk(long bytes)
            {
                if (Interlocked.Add(ref diskBytes, bytes) <= BacklogDiskLimit) return;
                Interlocked.Add(ref diskBytes, -bytes);
                throw new InvalidOperationException(UiText.Get("MemoryLimit"));
            }

            void Report(RecordingStage stage)
            {
                if (Logger.IsEnabled(LogLevel.Debug) &&
                    clock.ElapsedMilliseconds >= Volatile.Read(ref nextLogMilliseconds))
                {
                    var next = clock.ElapsedMilliseconds + 5000;
                    if (Interlocked.Exchange(ref nextLogMilliseconds, next) <= next - 5000)
                        Logger.ZLogDebug(
                            $"Animation recording progress: stage={stage.ToString()}, captured={Volatile.Read(ref capturedCount)}, encoded={Volatile.Read(ref encodedCount)}, elapsedSeconds={Volatile.Read(ref elapsedSeconds)}, diskBacklogBytes={Interlocked.Read(ref diskBytes)}");
                }

                progress?.Report(new(stage,
                    Volatile.Read(ref capturedCount), Volatile.Read(ref encodedCount),
                    Volatile.Read(ref capturedFps), Volatile.Read(ref elapsedSeconds)));
            }

            async Task<byte[]> LoadAsync(CapturedFrame frame)
            {
                if (frame.Pixels is { } pixels)
                {
                    // The encoder and its iterator finish before this method disposes the semaphore.
                    // ReSharper disable once AccessToDisposedClosure
                    memorySlots.Release();
                    return pixels;
                }

                var data = await File.ReadAllBytesAsync(frame.Path!, abortToken);
                File.Delete(frame.Path!);
                // Producer reservations and consumer releases deliberately share this atomic counter.
                // ReSharper disable once AccessToModifiedClosure
                Interlocked.Add(ref diskBytes, -data.Length);
                return data;
            }

            async IAsyncEnumerable<RawFrame> ReadFrames()
            {
                CapturedFrame? pending = null;
                await foreach (var frame in channel.Reader.ReadAllAsync(abortToken))
                {
                    if (pending is { } previous)
                        yield return new(await LoadAsync(previous),
                            Math.Max(1, (int)(Math.Round(frame.Time) - Math.Round(previous.Time))));
                    pending = frame;
                }

                await producer;
                if (pending is { } last)
                    yield return new(await LoadAsync(last),
                        Math.Max(1, (int)(Math.Round(endTime) - Math.Round(last.Time))));
            }
        }

        private readonly record struct CapturedFrame(byte[]? Pixels, string? Path, double Time);

        private sealed class EncodingProgress(Action<RecordingProgress> report) : IProgress<RecordingProgress>
        {
            public void Report(RecordingProgress value)
            {
                report(value);
            }
        }
    }
}
