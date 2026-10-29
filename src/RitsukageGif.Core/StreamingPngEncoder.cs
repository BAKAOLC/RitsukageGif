using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    public static class StreamingPngEncoder
    {
        public static Task EncodeAsync(IAsyncEnumerable<RawFrame> frames, string output, RecordingOptions options,
            IProgress<RecordingProgress>? progress, CancellationToken token)
        {
            options.Validate();
            return Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                await using var stream = File.Create(output);
                using var writer = new StreamingPngWriter(stream, options.Width, options.Height, token);
                byte[]? previous = null;
                var count = 0;
                await foreach (var frame in frames.WithCancellation(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (frame.DelayMs <= 0 || frame.Pixels.Length != checked(options.Width * options.Height * 4))
                        throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
                    var bounds = ChangedBounds(previous, frame.Pixels, options.Width, options.Height, token);
                    writer.Add(frame.Pixels, bounds, frame.DelayMs);
                    previous = frame.Pixels;
                    count++;
                    progress?.Report(new(RecordingStage.Live, count, count));
                }

                if (count == 0) throw new InvalidOperationException(UiText.Get("NoFrames"));
                token.ThrowIfCancellationRequested();
                writer.Complete();
                progress?.Report(new(RecordingStage.Complete, count, count));
            }, token);
        }

        private static PixelRect ChangedBounds(byte[]? previous, byte[] current, int width, int height,
            CancellationToken token)
        {
            if (previous is null) return new(0, 0, width, height);
            var before = MemoryMarshal.Cast<byte, uint>(previous);
            var after = MemoryMarshal.Cast<byte, uint>(current);
            var left = width;
            var right = -1;
            var top = height;
            var bottom = -1;
            for (var y = 0; y < height; y++)
            {
                if ((y & 63) == 0) token.ThrowIfCancellationRequested();
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

            return right < 0 ? default : new PixelRect(left, top, right - left + 1, bottom - top + 1);
        }
    }
}
