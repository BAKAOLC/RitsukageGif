using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        async Task<IReadOnlyList<MediaInfo>> IStaticCaptureService.CaptureOutputsAsync(
            IReadOnlyList<StaticCaptureOutput> outputs, StaticCaptureOptions options, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(outputs);
            ArgumentNullException.ThrowIfNull(options);
            if (outputs.Count is < 1 or > 16)
                throw new ArgumentException(UiText.Get("ServiceLimitExceeded"), nameof(outputs));
            foreach (var output in outputs)
            {
                ArgumentNullException.ThrowIfNull(output);
                ArgumentNullException.ThrowIfNull(output.Image);
                ArgumentNullException.ThrowIfNull(output.HdrImage);
            }

            var requests = outputs.Select(output => output with { Path = Path.GetFullPath(output.Path) }).ToArray();
            var paths = new HashSet<string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
            foreach (var output in requests)
            {
                if (!paths.Add(output.Path) || File.Exists(output.Path) || Directory.Exists(output.Path))
                    throw new IOException(UiText.Get("StaticCaptureOutputExists"));
                if (output.Color is not (OutputColorMode.Sdr or OutputColorMode.Hdr) ||
                    output.Image.Overwrite || output.HdrImage.Overwrite)
                    throw new ArgumentException(UiText.Get("StaticCaptureBatchOptionsInvalid"), nameof(outputs));
            }

            var (plan, toneMap) = await Dispatcher.UIThread.InvokeAsync(() =>
                (PrepareStaticCapture(options, token), options.ToneMap ?? Settings.CaptureToneMap));
            await PrepareStaticSourceAsync(plan, token).ConfigureAwait(false);
            return await Task.Run(async () =>
            {
                var frames = CaptureStaticFrames(plan, requests.Any(output => output.Color == OutputColorMode.Hdr),
                    toneMap, token);
                return await SaveStaticOutputsAsync(frames, requests, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        private CapturedFrameSet CaptureStaticFrames(StaticCapturePlan plan, bool needsHdr, HdrToneMapMode toneMap,
            CancellationToken token)
        {
            if (!Enum.IsDefined(toneMap)) throw new ArgumentOutOfRangeException(nameof(toneMap));
            token.ThrowIfCancellationRequested();
            var width = Math.Max(1, checked((int)Math.Round(plan.Options.Width * (plan.ScalePercent / 100d))));
            var height = Math.Max(1, checked((int)Math.Round(plan.Options.Height * (plan.ScalePercent / 100d))));
            CapturedFrameSet frames;
            using (IFrameSource source = plan.Preset is { } preset
                       ? new SceneCapture(plan.Options, plan.Monitors, preset, plan.Runtime, inputs: _inputs)
                       : new DesktopCapture(plan.Options, plan.Monitors, width, height))
                if (source is IColorFrameSource colorSource)
                {
                    frames = colorSource.CaptureFrames(token);
                    if (needsHdr && frames.Hdr is null)
                        throw new NotSupportedException(UiText.Get("StaticCaptureHdrUnavailable"));
                }
                else
                {
                    if (needsHdr) throw new NotSupportedException(UiText.Get("StaticCaptureHdrUnavailable"));
                    var pixels = source.Capture(token);
                    var color = new FrameColorInfo(false);
                    frames = new(new(plan.Options.Width, plan.Options.Height, plan.Options.Width * 4,
                            CapturePixelFormat.Bgra8Srgb, pixels, color, CaptureAlphaMode.Straight, 0,
                            [new(new(0, 0, plan.Options.Width, plan.Options.Height), color)]), null,
                        Stopwatch.GetTimestamp());
                }

            if (frames.Sdr.Width != width || frames.Sdr.Height != height)
                frames = ResizeStaticFrames(frames, width, height, token);
            if (frames.Hdr is { } hdr && toneMap != HdrToneMapMode.HighlightCompression)
                frames = frames with { Sdr = HdrToneMapping.Map(hdr, toneMap, token) };
            return frames;
        }

        private static async Task PrepareStaticSourceAsync(StaticCapturePlan plan, CancellationToken token)
        {
            if (!DesktopCapture.UsesSourceSelection ||
                plan.Preset?.Layout?.Layers.All(layer => layer.Media is not null) == true) return;
            if (plan.Preset is not null)
                throw new PlatformNotSupportedException(UiText.Get("CapturePlatformUnsupported"));
            var frame = await DesktopCapture.PrepareSourceAsync(NativeCaptureSession.SourceIsWindow,
                plan.Options.Cursor, plan.Options.Fps, token).ConfigureAwait(false);
            var bounds = new PixelRect(0, 0, frame.Width, frame.Height);
            if (plan.Options.Region.Intersect(bounds) != plan.Options.Region) throw new CaptureSourceLostException();
        }

        private static CapturedFrameSet ResizeStaticFrames(CapturedFrameSet frames, int width, int height,
            CancellationToken token)
        {
            if (frames.Hdr is not null)
            {
                var region = new PixelRect(0, 0, frames.Sdr.Width, frames.Sdr.Height);
                return ColorFrameComposition.Compose([(region, frames)], region, width, height,
                        frames.Sdr.Revision, token) with
                    {
                        TimestampTicks = frames.TimestampTicks,
                    };
            }

            using var image = new MagickImage();
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            image.ReadPixels(CapturedFramePixels.ToStraightBgra(frames.Sdr, token), new PixelReadSettings(
                (uint)frames.Sdr.Width,
                (uint)frames.Sdr.Height, StorageType.Char, PixelMapping.BGRA));
            try
            {
                image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }

            token.ThrowIfCancellationRequested();
            var color = frames.Sdr.ColorInfo;
            return frames with
            {
                Sdr = frames.Sdr with
                {
                    Width = width, Height = height, Stride = width * 4,
                    Pixels = image.ToByteArray(MagickFormat.Bgra), AlphaMode = CaptureAlphaMode.Straight,
                    ColorRegions = [new(new(0, 0, width, height), color)],
                },
            };
        }

        private static async Task<IReadOnlyList<MediaInfo>> SaveStaticOutputsAsync(CapturedFrameSet frames,
            StaticCaptureOutput[] outputs, CancellationToken token)
        {
            var staged = new List<(string Temporary, string Destination, MediaInfo Info)>(outputs.Length);
            var temporaryPaths = new List<string>(outputs.Length);
            var committed = new List<string>(outputs.Length);
            var createdDirectories = new List<string>();
            var completed = false;
            Exception? failure = null;
            try
            {
                byte[]? sdr = null;
                foreach (var output in outputs)
                {
                    token.ThrowIfCancellationRequested();
                    var directory = Path.GetDirectoryName(output.Path)!;
                    if (!Directory.Exists(directory))
                    {
                        var missing = new Stack<string>();
                        for (var current = directory;
                             !Directory.Exists(current);
                             current = Path.GetDirectoryName(current)!)
                        {
                            if (string.IsNullOrEmpty(current)) throw new DirectoryNotFoundException(directory);
                            missing.Push(current);
                        }

                        Directory.CreateDirectory(directory);
                        createdDirectories.AddRange(missing);
                    }

                    var temporary = Path.Combine(directory,
                        $".{Path.GetFileNameWithoutExtension(output.Path)}.{Guid.NewGuid():N}.stage{Path.GetExtension(output.Path)}");
                    temporaryPaths.Add(temporary);
                    MediaInfo info;
                    if (output.Color == OutputColorMode.Hdr)
                    {
                        info = await HdrImageService.SaveAsync(frames.Hdr ??
                                                               throw new NotSupportedException(
                                                                   UiText.Get("StaticCaptureHdrUnavailable")),
                            temporary,
                            output.HdrImage, token).ConfigureAwait(false);
                    }
                    else
                    {
                        sdr ??= CapturedFramePixels.ToStraightBgra(frames.Sdr, token);
                        info = await new StaticImageService().SavePixelsAsync(sdr, frames.Sdr.Width, frames.Sdr.Height,
                            temporary, output.Image, token).ConfigureAwait(false);
                    }

                    staged.Add((temporary, output.Path, info with { Path = output.Path }));
                }

                foreach (var item in staged)
                {
                    token.ThrowIfCancellationRequested();
                    File.Move(item.Temporary, item.Destination, false);
                    committed.Add(item.Destination);
                }

                completed = true;
            }
            catch (Exception error)
            {
                failure = error;
            }

            var cleanupErrors = new List<Exception>();
            if (!completed)
                foreach (var path in committed)
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception error)
                    {
                        cleanupErrors.Add(error);
                    }

            foreach (var path in temporaryPaths)
                try
                {
                    File.Delete(path);
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }

            if (!completed)
                foreach (var directory in createdDirectories.AsEnumerable().Reverse())
                    try
                    {
                        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                            Directory.Delete(directory);
                    }
                    catch (Exception error)
                    {
                        cleanupErrors.Add(error);
                    }

            if (cleanupErrors.Count > 0)
            {
                if (failure is not null) cleanupErrors.Insert(0, failure);
                throw new AggregateException(cleanupErrors);
            }

            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return [.. staged.Select(item => item.Info)];
        }
    }
}
