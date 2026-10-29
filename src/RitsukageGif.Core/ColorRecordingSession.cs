using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace RitsukageGif.Core
{
    public static class ColorRecordingSession
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Capture");

        public static Task<ColorRecordingResult> RunAsync(IColorFrameSource source, ColorRecordingOptions options,
            IProgress<RecordingProgress>? progress, CancellationToken stop, CancellationToken cancel)
        {
            return Task.Run(() => RunCoreAsync(source, options, progress, stop, cancel), cancel);
        }

        private static async Task<ColorRecordingResult> RunCoreAsync(IColorFrameSource source,
            ColorRecordingOptions options, IProgress<RecordingProgress>? progress, CancellationToken stop,
            CancellationToken cancel)
        {
            options.Sdr.Validate();
            Logger.ZLogInformation(
                $"HDR animation recording started: width={options.Sdr.Width}, height={options.Sdr.Height}, fps={options.Sdr.Fps}, includeSdr={options.SdrPath is not null}");
            if (options.SdrPath is not null && options.Sdr.UseFfmpeg)
                throw new NotSupportedException(UiText.Get("HdrRecordingExternalEncoderUnavailable"));
            if (!Enum.IsDefined(options.ToneMap)) throw new ArgumentOutOfRangeException(nameof(options));
            var hdrPath = Path.GetFullPath(options.HdrPath);
            var sdrPath = options.SdrPath is null ? null : Path.GetFullPath(options.SdrPath);
            if (sdrPath is not null && string.Equals(sdrPath, hdrPath, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
                throw new ArgumentException(UiText.Get("InvalidRecording"));
            if (File.Exists(hdrPath)) throw new IOException(hdrPath);
            if (sdrPath is not null && File.Exists(sdrPath)) throw new IOException(sdrPath);
            var frameBytes = checked((long)options.Sdr.Width * options.Sdr.Height * 12);
            // Pending capture, active encoders and their previous frame need room outside the queues.
            if (options.MaximumBufferedBytes / frameBytes < 5)
                throw new ArgumentException(UiText.Get("HdrRecordingMemoryLimit"));
            var capacity = (int)Math.Min(16, options.MaximumBufferedBytes / frameBytes - 4);
            var sdrQueue = CreateQueue<RawFrame>(capacity);
            var hdrQueue = CreateQueue<HdrAnimationFrame>(capacity);
            using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(stop, abort.Token);
            var hdrTemporary = TemporaryPath(hdrPath);
            var sdrTemporary = sdrPath is null ? null : TemporaryPath(sdrPath);
            long stoppedAt = 0;
            await using var stopRegistration = stop.Register(() =>
                // ReSharper disable once AccessToModifiedClosure
                Interlocked.CompareExchange(ref stoppedAt, Stopwatch.GetTimestamp(), 0));
            var hdrWorker = ObserveEncoderAsync(HdrAnimationEncoder.EncodeAsync(
                hdrQueue.Reader.ReadAllAsync(abort.Token),
                hdrTemporary, options.Sdr.Width, options.Sdr.Height, abort.Token), abort);
            var sdrWorker = sdrTemporary is null
                ? Task.CompletedTask
                : ObserveEncoderAsync(StreamingAnimationEncoder.EncodeAsync(sdrQueue.Reader.ReadAllAsync(abort.Token),
                    sdrTemporary, options.Sdr, null, abort.Token), abort);
            var publishedSdr = false;
            var publishedHdr = false;
            var count = 0;
            long nextLogAt = 0;
            try
            {
                CapturedFrameSet? pending = null;
                long startedAt = 0;
                var period = (double)Stopwatch.Frequency / options.Sdr.Fps;
                var next = (double)Stopwatch.GetTimestamp();
                while (!captureStop.IsCancellationRequested)
                {
                    var wait = (next - Stopwatch.GetTimestamp()) / Stopwatch.Frequency;
                    if (wait > 0)
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(wait), captureStop.Token);
                        }
                        catch (OperationCanceledException) when (captureStop.IsCancellationRequested)
                        {
                            break;
                        }

                    if (captureStop.IsCancellationRequested) break;
                    // Both streams accept the same capture or neither does. Slow encoding drops deadlines,
                    // never one color rendition; timestamps preserve real playback speed.
                    if (hdrQueue.Reader.Count < capacity && (sdrPath is null || sdrQueue.Reader.Count < capacity))
                    {
                        CapturedFrameSet current;
                        try
                        {
                            current = source.CaptureFrames(captureStop.Token);
                        }
                        catch (OperationCanceledException) when (captureStop.IsCancellationRequested)
                        {
                            break;
                        }

                        if (captureStop.IsCancellationRequested) break;
                        Validate(current, options.Sdr);
                        if (pending is null && !current.Hdr!.ColorInfo.IsHdrSource)
                            throw new InvalidOperationException(UiText.Get("HdrRecordingUnavailable"));
                        if (current.TimestampTicks <= 0 || current.TimestampTicks > Stopwatch.GetTimestamp() ||
                            (pending is not null && current.TimestampTicks < pending.TimestampTicks))
                            throw new InvalidOperationException(UiText.Get("HdrRecordingTimestampInvalid"));
                        if (pending is not null && current.TimestampTicks == pending.TimestampTicks)
                        {
                            if (current.Hdr!.Revision != pending.Hdr!.Revision)
                                throw new InvalidOperationException(UiText.Get("HdrRecordingTimestampInvalid"));
                            next = Math.Max(next + period, Stopwatch.GetTimestamp() + period);
                            continue;
                        }

                        if (sdrPath is not null && options.ToneMap != HdrToneMapMode.HighlightCompression)
                            current = current with
                            {
                                Sdr = HdrToneMapping.Map(current.Hdr!, options.ToneMap, abort.Token),
                            };
                        if (pending is not null)
                            await EnqueueAsync(pending,
                                Delay(pending.TimestampTicks, current.TimestampTicks, startedAt),
                                sdrPath is not null, sdrQueue.Writer, hdrQueue.Writer, abort.Token);
                        else startedAt = current.TimestampTicks;
                        pending = current;
                        count++;
                        if (count == 1)
                            Logger.ZLogInformation(
                                $"HDR animation capture produced its first frame: width={current.Hdr!.Width}, height={current.Hdr.Height}, paperWhiteNits={current.Hdr.ColorInfo.SdrWhiteNits}");
                        if (Logger.IsEnabled(LogLevel.Debug) && current.TimestampTicks >= nextLogAt)
                        {
                            nextLogAt = current.TimestampTicks + Stopwatch.Frequency * 5;
                            Logger.ZLogDebug(
                                $"HDR animation recording progress: captured={count}, queuedHdr={hdrQueue.Reader.Count}, queuedSdr={sdrQueue.Reader.Count}");
                        }

                        progress?.Report(new(RecordingStage.Recording, count, 0,
                            ElapsedSeconds: (current.TimestampTicks - startedAt) / (double)Stopwatch.Frequency));
                    }

                    var now = Stopwatch.GetTimestamp();
                    next = Math.Max(next + period, next + Math.Ceiling((now - next) / period) * period);
                }

                cancel.ThrowIfCancellationRequested();
                if (abort.IsCancellationRequested) await Task.WhenAll(hdrWorker, sdrWorker);
                abort.Token.ThrowIfCancellationRequested();
                if (pending is null) throw new InvalidOperationException(UiText.Get("NoFrames"));
                var stopTicks = Interlocked.Read(ref stoppedAt);
                var end = Math.Max(pending.TimestampTicks + Stopwatch.Frequency / 1000,
                    stopTicks == 0 ? Stopwatch.GetTimestamp() : stopTicks);
                await EnqueueAsync(pending, Delay(pending.TimestampTicks, end, startedAt), sdrPath is not null,
                    sdrQueue.Writer, hdrQueue.Writer, abort.Token);
                hdrQueue.Writer.TryComplete();
                sdrQueue.Writer.TryComplete();
                Logger.ZLogInformation($"HDR animation capture stopped; draining encoders: captured={count}");
                await Task.WhenAll(hdrWorker, sdrWorker);
                cancel.ThrowIfCancellationRequested();
                // No-overwrite publication. Roll back only files this session successfully created.
                if (sdrTemporary is not null)
                {
                    File.Move(sdrTemporary, sdrPath!);
                    publishedSdr = true;
                }

                File.Move(hdrTemporary, hdrPath);
                publishedHdr = true;
                Logger.ZLogInformation(
                    $"HDR animation recording published: frames={count}, includeSdr={sdrPath is not null}, elapsedSeconds={(end - startedAt) / (double)Stopwatch.Frequency}");
                return new(sdrPath, hdrPath, count,
                    TimeSpan.FromSeconds((end - startedAt) / (double)Stopwatch.Frequency));
            }
            catch (Exception error)
            {
                if (error is OperationCanceledException && cancel.IsCancellationRequested)
                    Logger.ZLogInformation($"HDR animation recording canceled: captured={count}");
                else
                    Logger.ZLogError(error,
                        $"HDR animation recording failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                await abort.CancelAsync();
                if (publishedHdr) File.Delete(hdrPath);
                if (publishedSdr) File.Delete(sdrPath!);
                try
                {
                    await Task.WhenAll(hdrWorker, sdrWorker);
                }
                catch (Exception encoderError)
                {
                    if (error is OperationCanceledException && !cancel.IsCancellationRequested &&
                        encoderError is not OperationCanceledException)
                        ExceptionDispatchInfo.Capture(encoderError).Throw();
                }

                throw;
            }
            finally
            {
                hdrQueue.Writer.TryComplete();
                sdrQueue.Writer.TryComplete();
                await abort.CancelAsync();
                try
                {
                    await Task.WhenAll(hdrWorker, sdrWorker);
                }
                catch (Exception error)
                {
                    Logger.ZLogDebug(
                        $"HDR animation encoder teardown observed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                }

                File.Delete(hdrTemporary);
                if (sdrTemporary is not null) File.Delete(sdrTemporary);
            }
        }

        private static Channel<T> CreateQueue<T>(int capacity)
        {
            return Channel.CreateBounded<T>(
                new BoundedChannelOptions(capacity)
                    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        }

        private static async Task ObserveEncoderAsync(Task worker, CancellationTokenSource abort)
        {
            try
            {
                await worker;
            }
            catch
            {
                await abort.CancelAsync();
                throw;
            }
        }

        private static string TemporaryPath(string destination)
        {
            var directory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".partial");
        }

        private static int Delay(long from, long to, long origin)
        {
            return checked((int)Math.Max(1,
                Math.Round((to - origin) * 1000.0 / Stopwatch.Frequency) -
                Math.Round((from - origin) * 1000.0 / Stopwatch.Frequency)));
        }

        private static async ValueTask EnqueueAsync(CapturedFrameSet frames, int delay, bool includeSdr,
            ChannelWriter<RawFrame> sdr, ChannelWriter<HdrAnimationFrame> hdr, CancellationToken token)
        {
            await hdr.WriteAsync(new(Contiguous(frames.Hdr!, 8), delay, frames.Hdr!.AlphaMode), token);
            if (includeSdr)
                await sdr.WriteAsync(new(CapturedFramePixels.ToStraightBgra(frames.Sdr, token), delay), token);
        }

        private static byte[] Contiguous(CapturedFrame frame, int bytesPerPixel)
        {
            var rowSize = checked(frame.Width * bytesPerPixel);
            var length = checked(rowSize * frame.Height);
            if (frame.Stride == rowSize && MemoryMarshal.TryGetArray(frame.Pixels, out var segment) &&
                segment.Offset == 0 && segment.Array!.Length == length) return segment.Array;
            var result = new byte[length];
            for (var y = 0; y < frame.Height; y++)
                frame.Pixels.Span.Slice(y * frame.Stride, rowSize).CopyTo(result.AsSpan(y * rowSize, rowSize));
            return result;
        }

        private static void Validate(CapturedFrameSet frames, RecordingOptions options)
        {
            if (frames.Hdr is null ||
                frames.Hdr.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb)
                throw new InvalidOperationException(UiText.Get("HdrRecordingUnavailable"));
            ValidateFrame(frames.Hdr, options, 8);
            ValidateFrame(frames.Sdr, options, 4);
            if (frames.Sdr.PixelFormat != CapturePixelFormat.Bgra8Srgb ||
                frames.Sdr.Revision != frames.Hdr.Revision)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
        }

        private static void ValidateFrame(CapturedFrame frame, RecordingOptions options, int bytesPerPixel)
        {
            if (!Enum.IsDefined(frame.AlphaMode) || frame.Width != options.Width || frame.Height != options.Height ||
                frame.Stride < checked(frame.Width * bytesPerPixel) ||
                frame.Pixels.Length < checked(frame.Stride * frame.Height))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
        }
    }
}
