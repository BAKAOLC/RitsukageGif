using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    internal static class HdrAnimationEncoder
    {
        public static Task EncodeAsync(IAsyncEnumerable<HdrAnimationFrame> frames, string output, int width, int height,
            CancellationToken token)
        {
            return Task.Run(async () =>
            {
                await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamingPngWriter(stream, width, height, token, hdr: true);
                byte[]? previous = null;
                CaptureAlphaMode? previousAlpha = null;
                var count = 0;
                await foreach (var frame in frames.WithCancellation(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (frame.Pixels.Length != checked(width * height * 8) || frame.DelayMs <= 0)
                        throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
                    var bounds = previousAlpha == frame.AlphaMode && previous is not null
                        ? ChangedBounds(previous, frame.Pixels, width, height, token)
                        : new(0, 0, width, height);
                    writer.Add(frame.Pixels, bounds, frame.DelayMs, frame.AlphaMode);
                    previous = frame.Pixels;
                    previousAlpha = frame.AlphaMode;
                    count++;
                }

                if (count == 0) throw new InvalidOperationException(UiText.Get("NoFrames"));
                writer.Complete();
            }, token);
        }

        private static PixelRect ChangedBounds(byte[] previous, byte[] current, int width, int height,
            CancellationToken token)
        {
            var before = MemoryMarshal.Cast<byte, ulong>(previous);
            var after = MemoryMarshal.Cast<byte, ulong>(current);
            var left = width;
            var right = -1;
            var top = height;
            var bottom = -1;
            for (var y = 0; y < height; y++)
            {
                if ((y & 31) == 0) token.ThrowIfCancellationRequested();
                var a = before.Slice(y * width, width);
                var b = after.Slice(y * width, width);
                if (a.SequenceEqual(b)) continue;
                top = Math.Min(top, y);
                bottom = y;
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

            return right < 0 ? default : new PixelRect(left, top, right - left + 1, bottom - top + 1);
        }
    }
}
