using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RitsukageGif.Core
{
    public sealed class VideoRecordingSession(
        Func<RecordingOptions, IFrameSource> sourceFactory,
        Func<IReadOnlyList<IAudioCaptureSource>>? audioFactory = null)
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Video");

        public async Task<bool> RunAsync(VideoRecordingOptions options, string outputPath,
            IProgress<RecordingProgress>? progress, CancellationToken stop, CancellationToken cancel)
        {
            options.Validate();
            Logger.ZLogInformation(
                $"Video recording started: container={options.Container.ToString()}, colorMode={options.ColorMode.ToString()}, width={options.Capture.Width}, height={options.Capture.Height}, fps={options.Capture.Fps}, configuredAudioInputs={options.AudioInputs.Count}");
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            cancel.ThrowIfCancellationRequested();
            if (stop.IsCancellationRequested) return false;
            var executable = FfmpegEncoder.FindExecutable()
                             ?? throw new FfmpegDependencyException(UiText.Get("FfmpegMissing"));
            if (options.ColorMode != OutputColorMode.Sdr)
            {
                var capabilities = await VideoRecordingCapabilities.ProbeAsync(cancel);
                if (!(options.Container == VideoContainer.Mp4 ? capabilities.Mp4Hdr : capabilities.WebMHdr))
                    throw new NotSupportedException(UiText.Get("VideoHdrOutputUnavailable"));
            }

            var destinations = options.GetOutputPaths(outputPath);
            var destination = destinations[0];
            var directory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(directory);
            var staging = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".recording");
            var hdrStaging = options.ColorMode == OutputColorMode.Both ? staging + ".hdr" : null;
            using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(stop, abort.Token);
            using var audioStop = CancellationTokenSource.CreateLinkedTokenSource(abort.Token);
            var audioSources = audioFactory?.Invoke() ?? [];
            var pipes = new List<VideoAudioPipe>();
            var audioTasks = new List<Task>();
            using var process = new Process();
            Task<string>? errors = null;
            Task<RecordingProgress>? pump = null;
            Task? audioObserver = null;
            IFrameSource? source = null;
            var started = false;
            try
            {
                if (audioSources.Count + options.AudioInputs.Count > 8)
                    throw new ArgumentOutOfRangeException(nameof(options));
                foreach (var audioSource in audioSources) pipes.Add(new(audioSource));
                var first = await Task.Run(() =>
                {
                    source = sourceFactory(options.Capture);
                    if (options.ColorMode != OutputColorMode.Sdr)
                    {
                        if (source is not IColorFrameSource color)
                            throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
                        source = new HdrVideoFrameSource(source, color, options);
                    }

                    return source.Capture(captureStop.Token);
                }, captureStop.Token);
                var frameBytes = checked(options.Capture.Width * options.Capture.Height *
                                         options.ColorMode switch
                                         {
                                             OutputColorMode.Sdr => 4, OutputColorMode.Hdr => 6,
                                             OutputColorMode.Both => 12,
                                             _ => throw new ArgumentOutOfRangeException(nameof(options)),
                                         });
                ValidateFrame(first, frameBytes);
                Logger.ZLogInformation(
                    $"Video capture produced its first frame: bytes={first.Length}, liveAudioSources={audioSources.Count}");
                process.StartInfo = BuildStartInfo(executable, options, pipes, staging, hdrStaging);
                cancel.ThrowIfCancellationRequested();
                var clock = Stopwatch.StartNew();
                process.Start();
                started = true;
                Logger.ZLogDebug($"Video encoder process started: processId={process.Id}");
                errors = ReadErrorsAsync(process.StandardError);
                // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                foreach (var pipe in pipes) audioTasks.Add(pipe.RunAsync(audioStop.Token));
                var input = process.StandardInput.BaseStream;
                var captureCancellation = captureStop.Token;
                var cancellation = abort.Token;
                var frameSource = source!;
                pump = Task.Run(() => WriteFramesAsync(input, frameSource, first, options.Capture, frameBytes,
                    clock, progress, stop, captureCancellation, cancellation), cancellation);
                audioObserver = ObserveAudioAsync(audioTasks, audioStop.Token);
                var exited = process.WaitForExitAsync(abort.Token);
                var winner = await Task.WhenAny(pump, audioObserver, exited);
                if (winner == audioObserver) await audioObserver;
                if (winner == exited && !pump.IsCompleted)
                {
                    await exited;
                    throw new InvalidOperationException(UiText.Format("FfmpegFailed", process.ExitCode, await errors));
                }

                var recorded = await pump;
                Logger.ZLogInformation(
                    $"Video capture stopped; finalizing encoder: captured={recorded.Captured}, encoded={recorded.Encoded}, elapsedSeconds={recorded.ElapsedSeconds}");
                process.StandardInput.Close();
                progress?.Report(recorded with { Stage = RecordingStage.Encoding });
                // -shortest closes live audio when the final video frame has been muxed.
                await exited.WaitAsync(TimeSpan.FromSeconds(30), cancel);
                await audioStop.CancelAsync();
                var error = await errors;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(UiText.Format("FfmpegFailed", process.ExitCode, error));
                cancel.ThrowIfCancellationRequested();
                if (!File.Exists(staging) || new FileInfo(staging).Length == 0 ||
                    (hdrStaging is not null && (!File.Exists(hdrStaging) || new FileInfo(hdrStaging).Length == 0)))
                    throw new InvalidDataException(UiText.Get("NoInputFrames"));
                if (audioSources.Count + options.AudioInputs.Count != 0)
                {
                    var duration = (double)recorded.Encoded / options.Capture.Fps;
                    await VideoMuxFinalizer.TrimAudioAsync(executable, staging, options.Container, duration, cancel);
                    if (hdrStaging is not null)
                        await VideoMuxFinalizer.TrimAudioAsync(executable, hdrStaging, options.Container, duration,
                            cancel);
                }

                cancel.ThrowIfCancellationRequested();
                VideoOutputTransaction.Publish(hdrStaging is null ? [staging] : [staging, hdrStaging],
                    destinations, options.Overwrite);
                progress?.Report(recorded with { Stage = RecordingStage.Complete });
                Logger.ZLogInformation(
                    $"Video recording published: outputs={destinations.Count}, frames={recorded.Encoded}, container={options.Container.ToString()}, colorMode={options.ColorMode.ToString()}");
                return true;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested && !cancel.IsCancellationRequested &&
                                                     !started)
            {
                // Stopping during source initialization produces no empty recording.
                Logger.ZLogInformation($"Video recording stopped before its first frame");
                return false;
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                Logger.ZLogInformation($"Video recording canceled");
                throw new OperationCanceledException(cancel);
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Video recording failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                await abort.CancelAsync();
                await audioStop.CancelAsync();
                if (started && !process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }

                if (pump is not null) await ObserveCleanupAsync(pump);
                source?.Dispose();
                foreach (var pipe in pipes) pipe.Dispose();
                foreach (var audioTask in audioTasks) await ObserveCleanupAsync(audioTask);
                if (audioObserver is not null) await ObserveCleanupAsync(audioObserver);
                foreach (var audioSource in audioSources) audioSource.Dispose();
                if (errors is not null) await errors;
                if (File.Exists(staging)) File.Delete(staging);
                if (hdrStaging is not null && File.Exists(hdrStaging)) File.Delete(hdrStaging);
            }
        }

        private static async Task ObserveCleanupAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception error)
            {
                Logger.ZLogDebug(
                    $"Video worker teardown observed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
            }
        }

        private static async Task ObserveAudioAsync(List<Task> tasks, CancellationToken token)
        {
            if (tasks.Count == 0)
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            else
            {
                var completed = await Task.WhenAny(tasks);
                await completed;
                token.ThrowIfCancellationRequested();
                throw new EndOfStreamException(UiText.Get("VideoAudioEnded"));
            }
        }

        private static async Task<string> ReadErrorsAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            var text = new StringBuilder();
            int count;
            while ((count = await reader.ReadAsync(buffer)) != 0)
            {
                text.Append(buffer, 0, count);
                if (text.Length > 16384) text.Remove(0, text.Length - 16384);
            }

            return text.ToString();
        }

        private static void ValidateFrame(byte[] pixels, int expectedLength)
        {
            if (pixels.Length != expectedLength)
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
        }

        private static async Task<RecordingProgress> WriteFramesAsync(Stream stream, IFrameSource source, byte[] first,
            RecordingOptions options, int frameBytes, Stopwatch clock, IProgress<RecordingProgress>? progress,
            CancellationToken stop, CancellationToken captureToken, CancellationToken cancel)
        {
            var previous = first;
            byte[]? spare = null;
            var written = 0;
            var captured = 1;
            var period = 1000.0 / options.Fps;
            long stoppedTicks = -1;
            await using var registration =
                // ReSharper disable once AccessToModifiedClosure
                stop.Register(() => Interlocked.CompareExchange(ref stoppedTicks, clock.ElapsedTicks, -1));
            await stream.WriteAsync(previous, cancel);
            written++;
            while (!stop.IsCancellationRequested)
            {
                var wait = written * period - clock.Elapsed.TotalMilliseconds;
                try
                {
                    if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), captureToken);
                    if (stop.IsCancellationRequested) break;
                    var next = source is HdrVideoFrameSource hdr
                        ? hdr.Capture(spare, captureToken)
                        : source.Capture(captureToken);
                    ValidateFrame(next, frameBytes);
                    if (stop.IsCancellationRequested) break;
                    var slot = (int)Math.Min(int.MaxValue - 1, Math.Floor(clock.Elapsed.TotalMilliseconds / period));
                    while (written < slot)
                    {
                        await stream.WriteAsync(previous, cancel);
                        written++;
                    }

                    if (source is HdrVideoFrameSource) spare = previous;
                    previous = next;
                    await stream.WriteAsync(previous, cancel);
                    written++;
                    captured++;
                    var elapsed = clock.Elapsed.TotalSeconds;
                    progress?.Report(new(RecordingStage.Live, captured, written,
                        elapsed > 0 ? captured / elapsed : 0, elapsed));
                }
                catch (OperationCanceledException) when
                    (stop.IsCancellationRequested && !cancel.IsCancellationRequested)
                {
                    break;
                }
            }

            cancel.ThrowIfCancellationRequested();
            var ticks = Interlocked.Read(ref stoppedTicks);
            var ended = ticks < 0 ? clock.Elapsed.TotalSeconds : ticks / (double)Stopwatch.Frequency;
            var finalCount = Math.Max(1, (long)Math.Ceiling(ended * options.Fps));
            while (written < finalCount)
            {
                await stream.WriteAsync(previous, cancel);
                written++;
            }

            return new(RecordingStage.Live, captured, written,
                ended > 0 ? captured / ended : 0, ended);
        }

        private static ProcessStartInfo BuildStartInfo(string executable, VideoRecordingOptions options,
            List<VideoAudioPipe> pipes, string output, string? hdrOutput)
        {
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true,
            };

            Add("-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-thread_queue_size", "8",
                "-f", "rawvideo", "-pixel_format", options.ColorMode == OutputColorMode.Sdr ? "bgra" : "rgb48le",
                "-video_size",
                $"{options.Capture.Width * (options.ColorMode == OutputColorMode.Both ? 2 : 1)}x{options.Capture.Height}",
                "-framerate", options.Capture.Fps.ToString(CultureInfo.InvariantCulture), "-i", "pipe:0");
            foreach (var pipe in pipes)
            {
                Add("-thread_queue_size", "64", "-f", pipe.Format.InputFormat, "-ar",
                    pipe.Format.SampleRate.ToString(CultureInfo.InvariantCulture), "-ac",
                    pipe.Format.Channels.ToString(CultureInfo.InvariantCulture));
                if (pipe.Format.ChannelMask != 0)
                    Add("-channel_layout", "0x" + pipe.Format.ChannelMask.ToString("X", CultureInfo.InvariantCulture));
                Add("-i", pipe.Path);
            }

            foreach (var audio in options.AudioInputs)
            {
                Add("-thread_queue_size", "64");
                if (!string.IsNullOrWhiteSpace(audio.Format)) Add("-f", audio.Format);
                Add("-i", audio.Input);
            }

            var audioCount = pipes.Count + options.AudioInputs.Count;
            var both = options.ColorMode == OutputColorMode.Both;
            var filters = new List<string>();
            if (both)
                filters.Add(
                    $"[0:v]split=2[hdrraw][sdrraw];[hdrraw]crop={options.Capture.Width}:{options.Capture.Height}:0:0[hdr];" +
                    $"[sdrraw]crop={options.Capture.Width}:{options.Capture.Height}:{options.Capture.Width}:0[sdr]");
            if (audioCount > 1)
            {
                var labels = string.Concat(Enumerable.Range(1, audioCount).Select(index => $"[{index}:a:0]"));
                filters.Add(
                    labels +
                    $"amix=inputs={audioCount}:duration=longest:normalize=1,aresample=48000:async=1000:first_pts=0,apad" +
                    (both ? ",asplit=2[asdr][ahdr]" : "[a]"));
            }

            if (filters.Count != 0) Add("-filter_complex", string.Join(';', filters));
            WriteOutput(output, options.ColorMode == OutputColorMode.Hdr, both ? "[sdr]" : "0:v:0",
                both ? "[asdr]" : "[a]");
            if (hdrOutput is not null) WriteOutput(hdrOutput, true, "[hdr]", "[ahdr]");

            return info;

            void WriteOutput(string path, bool hdr, string video, string audio)
            {
                Add("-map", video);
                var format = hdr ? "yuv420p10le" : "yuv420p";
                var matrix = hdr ? "bt2020" : "bt709";
                var filter =
                    $"pad=ceil(iw/2)*2:ceil(ih/2)*2,scale=in_range=full:out_range=limited:out_color_matrix={matrix},format={format},setparams=range=limited:color_primaries={(hdr ? "bt2020" : "bt709")}:color_trc={(hdr ? "smpte2084" : "iec61966-2-1")}:colorspace={(hdr ? "bt2020nc" : "bt709")}";
                // Cropped complex-graph outputs must keep their filters inside that graph.
                if (both)
                {
                    var inputLabel = hdr ? "[hdr]" : "[sdr]";
                    var outputLabel = hdr ? "[vhdr]" : "[vsdr]";
                    var filterIndex = info.ArgumentList.IndexOf("-filter_complex") + 1;
                    info.ArgumentList[filterIndex] += ";" + inputLabel + filter + outputLabel;
                    info.ArgumentList[^1] = outputLabel;
                }
                else
                {
                    Add("-vf", filter);
                }

                Add("-pix_fmt", format, "-color_range", "tv", "-color_primaries", hdr ? "bt2020" : "bt709",
                    "-color_trc", hdr ? "smpte2084" : "iec61966-2-1", "-colorspace", hdr ? "bt2020nc" : "bt709");
                switch (audioCount)
                {
                    case 1:
                        Add("-map", "1:a:0", "-af", "aresample=48000:async=1000:first_pts=0,apad");
                        break;
                    case > 1:
                        Add("-map", audio);
                        break;
                }

                if (audioCount > 0)
                    Add("-shortest", "-c:a", options.Container == VideoContainer.Mp4 ? "aac" : "libopus", "-b:a",
                        "192k");
                else Add("-an");
                Add("-c:v", options.Container == VideoContainer.Mp4 ? hdr ? "libx265" : "libx264" : "libvpx-vp9",
                    "-crf", options.Quality.ToString(CultureInfo.InvariantCulture));
                if (options.Container == VideoContainer.Mp4)
                {
                    Add("-preset", "veryfast", "-tune", "zerolatency", "-movflags", "+faststart");
                    if (hdr)
                        Add("-profile:v", "main10", "-tag:v", "hvc1", "-x265-params",
                            "hdr10=1:repeat-headers=1:log-level=error");
                    Add("-f", "mp4");
                }
                else
                {
                    Add("-b:v", "0", "-deadline", "realtime", "-cpu-used", "5", "-lag-in-frames", "0");
                    if (hdr) Add("-profile:v", "2");
                    Add("-f", "webm");
                }

                Add(path);
            }

            void Add(params string[] arguments)
            {
                foreach (var argument in arguments) info.ArgumentList.Add(argument);
            }
        }
    }
}
