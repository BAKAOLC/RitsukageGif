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
                await using var stream = new FileStream(output, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
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
                        var blend = IsSparseDelta(frame.Pixels, previous, options.Width, bounds, token);
                        var pixels = Crop(frame.Pixels, blend ? previous : null, options.Width, bounds, token);
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
                    progress?.Report(new(RecordingStage.Live, count, count));
                }

                token.ThrowIfCancellationRequested();
                if (count == 0) throw new InvalidOperationException(UiText.Get("NoFrames"));
                writer.Complete();
                progress?.Report(new(RecordingStage.Complete, count, count));
            }, token);
        }

        private static PixelRect ChangedBounds(byte[]? previous, byte[] current, int width, int height,
            CancellationToken token)
        {
            if (current.Length != checked(width * height * 4))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            if (previous is null) return new(0, 0, width, height);
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
                // Scan individual pixels only when a row expands the accumulated rectangle.
                if (left > 0 && !a[..left].SequenceEqual(b[..left]))
                {
                    var x = 0;
                    while (a[x] == b[x]) x++;
                    left = x;
                }

                if (right < width - 1 && !a[(right + 1)..].SequenceEqual(b[(right + 1)..]))
                {
                    var x = width - 1;
                    while (a[x] == b[x]) x--;
                    right = x;
                }
            }

            if (right < 0) return default;
            // ANMF stores offsets in units of two pixels. Expand toward the upper left.
            left &= ~1;
            top &= ~1;
            return new(left, top, right - left + 1, bottom - top + 1);
        }

        private static bool IsSparseDelta(byte[] current, byte[]? previous, int width, PixelRect bounds,
            CancellationToken token)
        {
            if (previous is null) return false;
            // Transparent holes skip unchanged pixels. Only opaque replacements can blend exactly.
            var before = MemoryMarshal.Cast<byte, uint>(previous);
            var after = MemoryMarshal.Cast<byte, uint>(current);
            var unchanged = 0;
            for (var y = 0; y < bounds.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var offset = (y + bounds.Y) * width + bounds.X;
                var a = before.Slice(offset, bounds.Width);
                var b = after.Slice(offset, bounds.Width);
                for (var x = 0; x < b.Length; x++)
                    if (a[x] == b[x]) unchanged++;
                    else if (current[(offset + x) * 4 + 3] != 255) return false;
            }

            return unchanged >= bounds.Width * bounds.Height / 4;
        }

        private static byte[] Crop(byte[] input, byte[]? previous, int width, PixelRect bounds,
            CancellationToken token)
        {
            if (previous is null && bounds.X == 0 && bounds.Width == width && bounds.Y == 0 &&
                bounds.Height * width * 4 == input.Length) return input;
            var result = new byte[checked(bounds.Width * bounds.Height * 4)];
            var before = MemoryMarshal.Cast<byte, uint>(previous);
            var after = MemoryMarshal.Cast<byte, uint>(input);
            var output = MemoryMarshal.Cast<byte, uint>(result.AsSpan());
            for (var y = 0; y < bounds.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var offset = (y + bounds.Y) * width + bounds.X;
                var row = after.Slice(offset, bounds.Width);
                var target = output.Slice(y * bounds.Width, bounds.Width);
                if (previous is null)
                {
                    row.CopyTo(target);
                }
                else
                {
                    var old = before.Slice(offset, bounds.Width);
                    for (var x = 0; x < row.Length; x++) target[x] = row[x] == old[x] ? 0 : row[x];
                }
            }

            return result;
        }
    }
}
