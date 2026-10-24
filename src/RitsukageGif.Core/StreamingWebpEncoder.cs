using System.Runtime.InteropServices;
using ImageMagick;

namespace RitsukageGif.Core
{
    public static class StreamingWebpEncoder
    {
        public static Task EncodeAsync(IAsyncEnumerable<RawFrame> frames, string output,
            RecordingOptions options, IProgress<RecordingProgress>? progress, CancellationToken token)
        {
            return Task.Run(async () =>
            {
                options.Validate();
                if (options.Width > 16383 || options.Height > 16383)
                    throw new ArgumentException(UiText.Get("RecordingTooLarge"));
                using var stream = new FileStream(output, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                var writer = new WebpAnimationWriter(stream, options.Width, options.Height);
                byte[]? previous = null;
                byte[]? encoded = null;
                var lastBounds = default(PixelRect);
                var count = 0;
                var lastBlend = false;
                await foreach (var frame in frames.WithCancellation(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (frame.DelayMs < 0) throw new ArgumentOutOfRangeException(nameof(frames));
                    var bounds = ChangedBounds(previous, frame.Pixels, options.Width, options.Height, token);
                    if (bounds.IsEmpty && encoded is not null)
                    {
                        writer.Extend(encoded, lastBounds, frame.DelayMs, lastBlend);
                    }
                    else
                    {
                        var pixels = Crop(frame.Pixels, options.Width, bounds);
                        var blend = PrepareSparseDelta(pixels, previous, options.Width, bounds, token);
                        using var image = new MagickImage(pixels,
                            new PixelReadSettings((uint)bounds.Width, (uint)bounds.Height, StorageType.Char,
                                PixelMapping.BGRA));
                        image.Progress += (_, value) => value.Cancel = token.IsCancellationRequested;
                        image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                        image.Settings.SetDefine(MagickFormat.WebP, "exact", true);
                        image.Settings.SetDefine(MagickFormat.WebP, "method", 3);
                        try
                        {
                            encoded = image.ToByteArray(MagickFormat.WebP);
                        }
                        catch (MagickException) when (token.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(token);
                        }

                        token.ThrowIfCancellationRequested();
                        lastBounds = bounds;
                        lastBlend = blend;
                        writer.Add(encoded, bounds, frame.DelayMs, blend);
                    }

                    previous = frame.Pixels;
                    count++;
                    progress?.Report(new RecordingProgress(RecordingStage.Live, count, count));
                }

                token.ThrowIfCancellationRequested();
                if (count == 0) throw new InvalidOperationException(UiText.Get("NoFrames"));
                writer.Complete();
                progress?.Report(new RecordingProgress(RecordingStage.Complete, count, count));
            }, token);
        }

        private static PixelRect ChangedBounds(byte[]? previous, byte[] current, int width, int height,
            CancellationToken token)
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
                token.ThrowIfCancellationRequested();
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

            if (right < 0) return default;
            // ANMF stores offsets in units of two pixels. Expand toward the upper left.
            left &= ~1;
            top &= ~1;
            return new PixelRect(left, top, right - left + 1, bottom - top + 1);
        }

        private static bool PrepareSparseDelta(byte[] pixels, byte[]? previous, int width, PixelRect bounds,
            CancellationToken token)
        {
            if (previous is null) return false;
            // Transparent holes skip unchanged pixels. Only opaque replacements can blend exactly.
            var unchanged = 0;
            for (var y = 0; y < bounds.Height; y++)
            for (var x = 0; x < bounds.Width; x++)
            {
                if (x == 0) token.ThrowIfCancellationRequested();
                var at = (y * bounds.Width + x) * 4;
                var before = ((y + bounds.Y) * width + x + bounds.X) * 4;
                if (pixels.AsSpan(at, 4).SequenceEqual(previous.AsSpan(before, 4))) unchanged++;
                else if (pixels[at + 3] != 255) return false;
            }

            if (unchanged < bounds.Width * bounds.Height / 4) return false;
            for (var y = 0; y < bounds.Height; y++)
            for (var x = 0; x < bounds.Width; x++)
            {
                if (x == 0) token.ThrowIfCancellationRequested();
                var at = (y * bounds.Width + x) * 4;
                var before = ((y + bounds.Y) * width + x + bounds.X) * 4;
                if (pixels.AsSpan(at, 4).SequenceEqual(previous.AsSpan(before, 4))) pixels.AsSpan(at, 4).Clear();
            }

            return true;
        }

        private static byte[] Crop(byte[] input, int width, PixelRect bounds)
        {
            if (bounds.X == 0 && bounds.Width == width && bounds.Y == 0 &&
                bounds.Height * width * 4 == input.Length) return (byte[])input.Clone();
            var result = new byte[checked(bounds.Width * bounds.Height * 4)];
            for (var y = 0; y < bounds.Height; y++)
                input.AsSpan(((y + bounds.Y) * width + bounds.X) * 4, bounds.Width * 4)
                    .CopyTo(result.AsSpan(y * bounds.Width * 4));
            return result;
        }
    }
}
