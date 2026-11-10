using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal static class RecordingReviewExport
    {
        internal static async Task<IReadOnlyList<string>> WriteAsync(RecordingReviewDocument document, string output,
            RecordingReviewExportOptions options, CancellationToken token, IProgress<double>? progress = null)
        {
            Validate(document, options);
            token.ThrowIfCancellationRequested();
            progress?.Report(0);
            var outputs = GetOutputPaths(document, output, options.Format);
            var directory = Path.GetDirectoryName(outputs[0])!;
            Directory.CreateDirectory(directory);
            var workspace = Path.Combine(directory, ".recording-review-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            var cleanup = true;
            try
            {
                using var trace = RecordingInputTraceDocument.TryOpenForMedia(document.Paths[0]);
                var staged = new List<string>(outputs.Count);
                for (var index = 0; index < outputs.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var pathProgress =
                        new RecordingReviewProgressSlice(progress, .99 * index / outputs.Count, .99 / outputs.Count);
                    var framesDirectory = Path.Combine(workspace, index.ToString(CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(framesDirectory);
                    var hdrOutput = document.Colors == OutputColorMode.Hdr ||
                                    (document.Colors == OutputColorMode.Both && index == 1);
                    var sourceIndex = document.Colors == OutputColorMode.Both ? index : 0;
                    await using var source = await RecordingReviewSource.OpenAsync(document.Paths[sourceIndex],
                        document.Video,
                        new(int.MaxValue, int.MaxValue), token).ConfigureAwait(false);
                    if (hdrOutput && !source.IsHdr)
                        throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
                    var animationHdr = hdrOutput && options.Format != "mp4";
                    var temporary = Path.Combine(workspace,
                        "output-" + index + (animationHdr ? ".png" : "." + options.Format));
                    if (animationHdr)
                    {
                        await HdrAnimationEncoder.EncodeAsync(
                            RenderHdrFramesAsync(source, trace, document, options, token, pathProgress),
                            temporary, source.Width, source.Height, token).ConfigureAwait(false);
                    }
                    else
                    {
                        var frames = await RenderFramesAsync(source, trace, framesDirectory, document, options,
                                hdrOutput, token,
                                new RecordingReviewProgressSlice(pathProgress, 0, .2))
                            .ConfigureAwait(false);
                        if (options.Format == "mp4")
                        {
                            await EncodeVideoAsync(document.Paths[sourceIndex], document.Video, temporary,
                                framesDirectory,
                                frames, hdrOutput, document.FramesPerSecond, options,
                                new(pathProgress, .2, .8), token).ConfigureAwait(false);
                        }
                        else
                        {
                            using var animation =
                                AnimationEditDocument.AdoptTransfer(framesDirectory, new([.. frames], 0));
                            await animation.ExportAsync(temporary, new()
                            {
                                Format = options.Format, Quality = options.EncodingQuality, Colors = options.Colors,
                                Dither = options.Dither, Lossless = false, Overwrite = false,
                            }, new RecordingReviewProgressSlice(pathProgress, .2, .8), token).ConfigureAwait(false);
                        }
                    }

                    staged.Add(temporary);
                    pathProgress.Report(1);
                }

                token.ThrowIfCancellationRequested();
                Publish(staged, outputs, workspace, options.Overwrite, token, ref cleanup);
                progress?.Report(1);
                return outputs;
            }
            finally
            {
                if (cleanup && Directory.Exists(workspace)) Directory.Delete(workspace, true);
            }
        }

        internal static IReadOnlyList<string> GetOutputPaths(RecordingReviewDocument document, string output,
            string format)
        {
            output = Path.GetFullPath(output);
            var hdr = Path.Combine(Path.GetDirectoryName(output)!,
                Path.GetFileNameWithoutExtension(output) + ".hdr" + (format == "mp4" ? ".mp4" : ".png"));
            return document.Colors switch
            {
                OutputColorMode.Sdr => [output],
                OutputColorMode.Hdr => [output],
                OutputColorMode.Both => [output, hdr],
                _ => throw new ArgumentOutOfRangeException(nameof(document)),
            };
        }

        private static async Task<List<AnimationEditTransferFrame>> RenderFramesAsync(RecordingReviewSource source,
            RecordingInputTraceDocument? trace, string directory, RecordingReviewDocument document,
            RecordingReviewExportOptions options, bool hdr, CancellationToken token, IProgress<double>? progress)
        {
            var frames = new List<AnimationEditTransferFrame>();
            PreviewFrame? previous = null;
            try
            {
                await foreach (var (frame, delay) in RenderSamplesAsync(source, trace, document, options, token,
                                   progress).ConfigureAwait(false))
                {
                    if (previous is not null && previous.Pixels.AsSpan(0, checked(frame.Width * frame.Height * 4))
                            .SequenceEqual(frame.Pixels.AsSpan(0, checked(frame.Width * frame.Height * 4))))
                    {
                        frames[^1] = frames[^1] with { DelayMs = checked(frames[^1].DelayMs + delay) };
                        continue;
                    }

                    var name = frames.Count.ToString("D8", CultureInfo.InvariantCulture) + ".png";
                    await WriteFrameAsync(frame, Path.Combine(directory, name), hdr, token).ConfigureAwait(false);
                    frames.Add(new(name, frame.Width, frame.Height, delay));
                    if (options.Format == "mp4") continue;
                    previous?.Dispose();
                    previous = frame.Retain();
                }
            }
            finally
            {
                previous?.Dispose();
            }

            return frames;
        }

        private static async IAsyncEnumerable<HdrAnimationFrame> RenderHdrFramesAsync(RecordingReviewSource source,
            RecordingInputTraceDocument? trace, RecordingReviewDocument document, RecordingReviewExportOptions options,
            [EnumeratorCancellation] CancellationToken token, IProgress<double>? progress)
        {
            await foreach (var (frame, delay) in RenderSamplesAsync(source, trace, document, options, token, progress)
                               .ConfigureAwait(false))
            {
                if (frame.HdrPixels is not { } linear)
                    throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
                yield return new([.. linear.AsSpan(0, checked(frame.Width * frame.Height * 8))], delay,
                    frame.HdrAlphaMode);
            }
        }

        private static async IAsyncEnumerable<(PreviewFrame Frame, int Delay)> RenderSamplesAsync(
            RecordingReviewSource source,
            RecordingInputTraceDocument? trace, RecordingReviewDocument document, RecordingReviewExportOptions options,
            [EnumeratorCancellation] CancellationToken token, IProgress<double>? progress)
        {
            var range = RecordingReviewSampling.Range(document, options.Start, options.End);
            var start = (long)(options.Start * 1000);
            var end = (long)(options.End * 1000);
            var fps = options.Format == "gif" ? Math.Min(document.FramesPerSecond, 50) : document.FramesPerSecond;
            var count = Math.Max(1, (long)((end - start) / options.Speed * fps / 1000));
            var quantum = options.Format == "gif" ? 100 : 1000;
            var multiplier = options.Format == "gif" ? 10 : 1;
            var written = 0L;
            var pendingDelay = 0;
            var pendingIndex = 0L;
            PreviewFrame? pending = null;
            try
            {
                for (var index = 0L; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (options is { Format: "gif", Quality: RecordingReviewQuality.Economy } &&
                        (index & 1) != 0)
                        continue;
                    if (pending is not null)
                    {
                        yield return (pending, pendingDelay);
                        progress?.Report((pendingIndex + 1) / (double)count);
                        pending.Dispose();
                        pending = null;
                    }

                    var sourceIndex = range.Start + (count == 1
                        ? 0
                        : (long)Math.Floor((range.End - range.Start) * (double)index / (count - 1) + .5));
                    var position = sourceIndex / (double)document.FramesPerSecond;
                    using var original = await source.FrameAtAsync(position, token).ConfigureAwait(false);
                    pending = RecordingEffectsComposition.Render(original, TimeSpan.FromSeconds(position), trace,
                        options.InputEffects, sourceIndex, range, options.Speed, source.OriginalWidth,
                        options.ProgressEffects, document.FramesPerSecond, token);
                    var target = (long)Math.Floor((index + 1) * (double)quantum / fps + .5) * multiplier;
                    pendingDelay = checked((int)(target - written));
                    written = target;
                    pendingIndex = index;
                }

                var total = (long)Math.Floor(count * (double)quantum / fps + .5) * multiplier;
                pendingDelay = checked(pendingDelay + (int)(total - written));
                yield return (pending!, pendingDelay);
                progress?.Report(1);
            }
            finally
            {
                pending?.Dispose();
            }
        }

        private static async Task WriteFrameAsync(PreviewFrame frame, string path, bool hdr, CancellationToken token)
        {
            if (hdr)
            {
                if (frame.HdrPixels is not { } linear)
                    throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
                var captured = new CapturedFrame(frame.Width, frame.Height, frame.Width * 8,
                    CapturePixelFormat.Rgba16FloatScRgb, linear.AsMemory(0, checked(frame.Width * frame.Height * 8)),
                    new(true, frame.HdrReferenceWhiteLevel * FrameColorInfo.ScRgbReferenceWhiteNits),
                    frame.HdrAlphaMode, 0, []);
                await HdrImageService.SaveAsync(captured, path, new() { Overwrite = false }, token)
                    .ConfigureAwait(false);
                return;
            }

            await Task.Run(() =>
            {
                var captured = new CapturedFrame(frame.Width, frame.Height, frame.Width * 4,
                    CapturePixelFormat.Bgra8Srgb, frame.Pixels.AsMemory(0, checked(frame.Width * frame.Height * 4)),
                    new(false), CaptureAlphaMode.Premultiplied, 0, []);
                var straight = CapturedFramePixels.ToStraightBgra(captured, token);
                using var image = new MagickImage();
                image.ReadPixels(straight,
                    new PixelReadSettings((uint)frame.Width, (uint)frame.Height, StorageType.Char, PixelMapping.BGRA));
                image.ColorSpace = ColorSpace.sRGB;
                image.SetProfile(ColorProfiles.SRGB);
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                try
                {
                    image.Write(path, MagickFormat.Png32);
                    token.ThrowIfCancellationRequested();
                }
                catch (MagickException) when (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }
            }, token).ConfigureAwait(false);
        }

        private static async Task EncodeVideoAsync(string input, bool video, string output, string directory,
            List<AnimationEditTransferFrame> frames, bool hdr, int framesPerSecond,
            RecordingReviewExportOptions options,
            RecordingReviewProgressSlice? progress, CancellationToken token)
        {
            var executable = FfmpegEncoder.FindExecutable() ?? throw new IOException(UiText.Get("FfmpegMissing"));
            if (hdr && !(await VideoRecordingCapabilities.ProbeAsync(token).ConfigureAwait(false)).Mp4Hdr)
                throw new NotSupportedException(UiText.Get("VideoHdrOutputUnavailable"));
            var manifest = Path.Combine(directory, "frames.ffconcat");
            await using (var writer = new StreamWriter(manifest, false, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync("ffconcat version 1.0").ConfigureAwait(false);
                foreach (var frame in frames)
                {
                    token.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync("file '" + frame.FileName + "'").ConfigureAwait(false);
                    await writer.WriteLineAsync("option framerate 1000").ConfigureAwait(false);
                    await writer.WriteLineAsync("duration " + Number(frame.DelayMs / 1000d)).ConfigureAwait(false);
                }

                await writer.WriteLineAsync("file '" + frames[^1].FileName + "'").ConfigureAwait(false);
                await writer.WriteLineAsync("option framerate 1000").ConfigureAwait(false);
            }

            var mix = video ? options.AudioMix : [];
            if (video && mix.Count == 0) mix = await ProbeAudioMixAsync(executable, input, token).ConfigureAwait(false);
            var hasAudio = mix.Count > 0;
            List<string> arguments =
            [
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-stats_period", "0.1", "-progress", "pipe:1",
                "-nostats", "-f", "concat", "-safe", "0", "-i", manifest,
            ];
            if (hasAudio)
            {
                var start = (long)(options.Start * 1000) / 1000d;
                var end = (long)(options.End * 1000) / 1000d;
                arguments.AddRange(["-ss", Number(start), "-t", Number(end - start), "-i", input]);
            }

            var format = hdr ? "yuv420p10le" : "yuv420p";
            var matrix = hdr ? "bt2020" : "bt709";
            var primaries = hdr ? "bt2020" : "bt709";
            var transfer = hdr ? "smpte2084" : "iec61966-2-1";
            var colorSpace = hdr ? "bt2020nc" : "bt709";
            arguments.AddRange([
                "-map", "0:v:0", "-vf",
                $"pad=ceil(iw/2)*2:ceil(ih/2)*2,scale=in_range=full:out_range=limited:out_color_matrix={matrix},format={format},setparams=range=limited:color_primaries={primaries}:color_trc={transfer}:colorspace={colorSpace}",
                "-c:v", hdr ? "libx265" : "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-crf",
                options.VideoCrf.ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", format, "-color_range", "tv", "-color_primaries", primaries,
                "-color_trc", transfer, "-colorspace", colorSpace, "-movflags", "+faststart",
            ]);
            if (hdr)
                arguments.AddRange([
                    "-profile:v", "main10", "-tag:v", "hvc1", "-x265-params",
                    "hdr10=1:repeat-headers=1:log-level=error",
                ]);
            else arguments.AddRange(["-refs", "1", "-rc-lookahead", "0"]);
            if (hasAudio)
            {
                var filters = new List<string>();
                var remaining = options.Speed;
                while (remaining < .5)
                {
                    filters.Add("atempo=0.5");
                    remaining *= 2;
                }

                while (remaining > 2)
                {
                    filters.Add("atempo=2");
                    remaining /= 2;
                }

                if (!remaining.Equals(1d)) filters.Add("atempo=" + Number(remaining));
                filters.Add("apad");
                var tracks = mix.Select((track, index) =>
                    $"[1:{track.StreamIndex}]aresample=async=1:first_pts=0,volume={Number(track.Muted ? 0 : track.Gain)}[a{index}]");
                var labels = string.Concat(mix.Select((_, index) => $"[a{index}]"));
                var graph = string.Join(';', tracks) + ";" + labels +
                            $"amix=inputs={mix.Count}:normalize=0:duration=longest," + string.Join(',', filters) +
                            "[mix]";
                arguments.AddRange(["-filter_complex", graph, "-map", "[mix]", "-c:a", "aac", "-b:a", "192k"]);
            }
            else
            {
                arguments.Add("-an");
            }

            arguments.AddRange([
                "-r", framesPerSecond.ToString(CultureInfo.InvariantCulture), "-fps_mode", "cfr",
                "-frames:v", frames.Count.ToString(CultureInfo.InvariantCulture),
                "-t", Number(frames.Count / (double)framesPerSecond), "-f", "mp4", output,
            ]);
            var duration = frames.Count / (double)framesPerSecond;
            await RecordingReviewProcess.RunAsync(executable, arguments, token, line =>
            {
                const string prefix = "out_time_us=";
                if (line.StartsWith(prefix, StringComparison.Ordinal) &&
                    double.TryParse(line.AsSpan(prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var microseconds))
                    progress?.Report(Math.Clamp(microseconds / 1_000_000 / duration, 0, 1));
            }).ConfigureAwait(false);
        }

        private static async Task<IReadOnlyList<RecordingAudioTrackMix>> ProbeAudioMixAsync(string executable,
            string input,
            CancellationToken token)
        {
            var probe = Path.Combine(Path.GetDirectoryName(executable)!,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            var output = await RecordingReviewProcess.RunAsync(probe,
                    ["-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "json", input],
                    token)
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(output);
            return
            [
                .. document.RootElement.GetProperty("streams").EnumerateArray()
                    .Select(stream => new RecordingAudioTrackMix(stream.GetProperty("index").GetInt32(), false, 1)),
            ];
        }

        private static void Publish(List<string> staged, IReadOnlyList<string> outputs, string workspace,
            bool overwrite,
            CancellationToken token, ref bool cleanup)
        {
            var backups = new string?[outputs.Count];
            var published = 0;
            try
            {
                for (var index = 0; index < outputs.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!overwrite || !File.Exists(outputs[index])) continue;
                    backups[index] = Path.Combine(workspace, "backup-" + index);
                    File.Copy(outputs[index], backups[index]!);
                }

                for (var index = 0; index < outputs.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    File.Move(staged[index], outputs[index], overwrite);
                    published++;
                }

                token.ThrowIfCancellationRequested();
            }
            catch (Exception failure)
            {
                var failures = new List<Exception>();
                for (var index = published - 1; index >= 0; index--)
                    try
                    {
                        if (backups[index] is { } backup) File.Move(backup, outputs[index], true);
                        else File.Delete(outputs[index]);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        failures.Add(error);
                    }

                if (failures.Count > 0)
                {
                    cleanup = false;
                    throw new AggregateException("Recording output rollback failed; backups remain in " + workspace,
                        [failure, .. failures]);
                }

                throw;
            }
        }

        private static void Validate(RecordingReviewDocument document, RecordingReviewExportOptions options)
        {
            if (document.RecordedDuration.TotalMilliseconds < 1 || document.AcceptedCaptures < 1 ||
                document.FramesPerSecond is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(document));
            if (options.Format is not ("gif" or "webp" or "mp4") || !Enum.IsDefined(options.Quality) ||
                !double.IsFinite(options.Start) || !double.IsFinite(options.End) || options.Start < 0 ||
                options.End <= options.Start || options.End > document.RecordedDuration.TotalSeconds ||
                !double.IsFinite(options.Speed) || options.Speed <= 0 ||
                options.AudioMix.Any(track =>
                    track.StreamIndex < 0 || !double.IsFinite(track.Gain) || track.Gain is < 0 or > 5))
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
