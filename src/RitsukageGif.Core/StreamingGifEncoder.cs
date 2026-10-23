using System.Runtime.InteropServices;
using ImageMagick;

namespace RitsukageGif.Core
{
    public static class StreamingGifEncoder
    {
        public static Task EncodeAsync(IAsyncEnumerable<RawFrame> frames, string output,
            RecordingOptions options, IProgress<RecordingProgress>? progress, CancellationToken token)
        {
            return Task.Run(async () =>
            {
                using var stream = File.Create(output);
                using var writer = new GifWriter(stream, options.Width, options.Height, options.Transparent);
                byte[]? previous = null;
                byte[]? lastEncoded = null;
                var lastBounds = new PixelRect(0, 0, options.Width, options.Height);
                var timing = new GifTiming(options.Fps);
                var count = 0;
                await foreach (var frame in frames.WithCancellation(token))
                {
                    token.ThrowIfCancellationRequested();
                    var delay = timing.Next(frame.DelayMs);
                    count++;
                    if (delay == 0)
                    {
                        progress?.Report(new RecordingProgress(RecordingStage.Live, count, count));
                        continue;
                    }

                    var bounds = ChangedBounds(previous, frame.Pixels, options.Width, options.Height);
                    if (options.Transparent && !bounds.IsEmpty)
                        bounds = new PixelRect(0, 0, options.Width, options.Height);
                    if (bounds.IsEmpty && lastEncoded is not null)
                    {
                        writer.Extend(lastEncoded, lastBounds, delay);
                    }
                    else
                    {
                        var pixels = Crop(frame.Pixels, options.Width, bounds);
                        using var image = new MagickImage(pixels,
                            new PixelReadSettings((uint)bounds.Width, (uint)bounds.Height, StorageType.Char,
                                PixelMapping.BGRA));
                        image.SetArtifact("dither:diffusion-amount",
                            options.Quality == GifQuality.Balanced ? "35%" : "100%");
                        image.Quantize(new QuantizeSettings
                        {
                            Colors = 256,
                            DitherMethod = options.Quality == GifQuality.Fast
                                ? DitherMethod.No
                                : DitherMethod.FloydSteinberg
                        });
                        lastEncoded = image.ToByteArray(MagickFormat.Gif);
                        lastBounds = bounds;
                        token.ThrowIfCancellationRequested();
                        writer.Add(lastEncoded, bounds, delay);
                    }

                    previous = frame.Pixels;
                    progress?.Report(new RecordingProgress(RecordingStage.Live, count, count));
                }

                if (count == 0) throw new InvalidOperationException(UiText.Get("NoFrames"));
                token.ThrowIfCancellationRequested();
                progress?.Report(new RecordingProgress(RecordingStage.Complete, count, count));
            }, token);
        }

        private static PixelRect ChangedBounds(byte[]? previous, byte[] current, int width, int height)
        {
            if (current.Length != checked(width * height * 4))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            if (previous is null) return new PixelRect(0, 0, width, height);
            var before = MemoryMarshal.Cast<byte, uint>(previous);
            var after = MemoryMarshal.Cast<byte, uint>(current);
            var left = width;
            var right = -1;
            var top = height;
            var bottom = -1;
            for (var y = 0; y < height; y++)
            {
                var a = before.Slice(y * width, width);
                var b = after.Slice(y * width, width);
                if (a.SequenceEqual(b)) continue;
                top = Math.Min(top, y);
                bottom = y;
                var x = 0;
                while (a[x] == b[x]) x++;
                left = Math.Min(left, x);
                x = width - 1;
                while (a[x] == b[x]) x--;
                right = Math.Max(right, x);
            }

            return right < 0 ? default : new PixelRect(left, top, right - left + 1, bottom - top + 1);
        }

        private static byte[] Crop(byte[] input, int width, PixelRect bounds)
        {
            if (bounds.X == 0 && bounds.Width == width && bounds.Y == 0 &&
                bounds.Height * width * 4 == input.Length) return input;
            var result = new byte[checked(bounds.Width * bounds.Height * 4)];
            for (var y = 0; y < bounds.Height; y++)
                input.AsSpan(((y + bounds.Y) * width + bounds.X) * 4, bounds.Width * 4)
                    .CopyTo(result.AsSpan(y * bounds.Width * 4));
            return result;
        }
    }
}
