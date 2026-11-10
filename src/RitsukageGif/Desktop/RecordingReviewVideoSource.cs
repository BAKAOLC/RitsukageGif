using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Avalonia;

namespace RitsukageGif.Desktop

{
    internal sealed class RecordingReviewVideoSource(
        string path,
        FfmpegFileSource source,
        RecordingReviewVideoInfo info)
        : RecordingReviewSource

    {
        private FfmpegFileSource _source = source;

        internal override int Width => _source.Width;

        internal override int Height => _source.Height;

        internal override double Duration => info.Duration;
        internal override bool IsHdr => _source.IsHdrSource;
        internal override bool HasAudio => info.HasAudio;
        internal override int OriginalWidth => info.Width;
        internal override int OriginalHeight => info.Height;

        internal static async Task<RecordingReviewVideoSource> CreateAsync(string path, PixelSize viewport,
            CancellationToken token)

        {
            var info = await ProbeAsync(path, token);

            var scale = Math.Min(1,
                Math.Min(Math.Max(1, viewport.Width) / (double)info.Width,
                    Math.Max(1, viewport.Height) / (double)info.Height));

            var source = await FfmpegFileSource.CreateAsync(new(path)

            {
                Width = Math.Max(1, (int)Math.Round(info.Width * scale)),

                Height = Math.Max(1, (int)Math.Round(info.Height * scale)),
            }, token);

            return new(path, source, info);
        }

        internal override async Task SetViewportAsync(PixelSize viewport, CancellationToken token)

        {
            var scale = Math.Min(1, Math.Min(Math.Max(1, viewport.Width) / (double)info.Width,
                Math.Max(1, viewport.Height) / (double)info.Height));

            var width = Math.Max(1, (int)Math.Round(info.Width * scale));

            var height = Math.Max(1, (int)Math.Round(info.Height * scale));

            if (_source.Width == width && _source.Height == height) return;

            var replacement = await FfmpegFileSource.CreateAsync(new(path) { Width = width, Height = height }, token)
                .ConfigureAwait(false);
            var previous = _source;
            _source = replacement;
            await Task.Run(previous.Dispose, CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task<RecordingReviewVideoInfo> ProbeAsync(string path, CancellationToken token)

        {
            var executable = FfmpegEncoder.FindExecutable() ?? throw new IOException(UiText.Get("FfmpegMissing"));

            var probe = Path.Combine(Path.GetDirectoryName(executable)!,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

            var output = await RecordingReviewProcess.RunAsync(probe,
            [
                "-v", "error", "-show_entries", "stream=codec_type,width,height,duration:format=duration", "-of",
                "json", "-i", path,
            ], token);

            using var document = JsonDocument.Parse(output);

            var streams = document.RootElement.GetProperty("streams");

            var video = streams.EnumerateArray()
                .First(stream => stream.GetProperty("codec_type").GetString() == "video");

            var duration = document.RootElement.GetProperty("format").GetProperty("duration").GetString();

            return new(video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(),
                double.Parse(duration!, CultureInfo.InvariantCulture),
                streams.EnumerateArray().Any(stream => stream.GetProperty("codec_type").GetString() == "audio"));
        }

        internal override async Task<PreviewFrame> FrameAtAsync(double position, CancellationToken token)

        {
            var (sdr, hdrFrame, _) = await _source.FrameAtPlanesAsync(position, false, true, token)
                                     ?? throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            var length = checked(sdr.Width * sdr.Height * 4);
            var pixels = ArrayPool<byte>.Shared.Rent(length);
            byte[]? hdrPixels = null;
            try
            {
                for (var row = 0; row < sdr.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    sdr.Pixels.Span.Slice(row * sdr.Stride, sdr.Width * 4)
                        .CopyTo(pixels.AsSpan(row * sdr.Width * 4));
                }

                if (hdrFrame is not null)
                {
                    hdrPixels = ArrayPool<byte>.Shared.Rent(checked(hdrFrame.Width * hdrFrame.Height * 8));
                    for (var row = 0; row < hdrFrame.Height; row++)
                    {
                        token.ThrowIfCancellationRequested();
                        hdrFrame.Pixels.Span.Slice(row * hdrFrame.Stride, hdrFrame.Width * 8)
                            .CopyTo(hdrPixels.AsSpan(row * hdrFrame.Width * 8));
                    }
                }

                return new(pixels, sdr.Width, sdr.Height, 0, hdrPixels,
                    (hdrFrame?.ColorInfo.SdrWhiteNits ?? FrameColorInfo.ScRgbReferenceWhiteNits) /
                    FrameColorInfo.ScRgbReferenceWhiteNits, hdrFrame?.AlphaMode ?? CaptureAlphaMode.Opaque);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(pixels);
                if (hdrPixels is not null) ArrayPool<byte>.Shared.Return(hdrPixels);
                throw;
            }
        }

        public override async ValueTask DisposeAsync()

        {
            await Task.Run(_source.Dispose).ConfigureAwait(false);
        }
    }
}
