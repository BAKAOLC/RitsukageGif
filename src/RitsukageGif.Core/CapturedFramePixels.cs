using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    public static class CapturedFramePixels
    {
        public static byte[] ToStraightBgra(CapturedFrame frame, CancellationToken token = default)
        {
            if (frame.PixelFormat != CapturePixelFormat.Bgra8Srgb || !Enum.IsDefined(frame.AlphaMode) ||
                frame.Width <= 0 || frame.Height <= 0 || frame.Stride < (long)frame.Width * 4 ||
                frame.Pixels.Length < (long)frame.Stride * frame.Height)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            token.ThrowIfCancellationRequested();
            var rowSize = checked(frame.Width * 4);
            var length = checked(rowSize * frame.Height);
            if (frame.AlphaMode == CaptureAlphaMode.Straight && frame.Stride == rowSize &&
                MemoryMarshal.TryGetArray(frame.Pixels, out var segment) && segment.Offset == 0 &&
                segment.Array!.Length == length) return segment.Array;
            var result = new byte[length];
            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var row = result.AsSpan(y * rowSize, rowSize);
                frame.Pixels.Span.Slice(y * frame.Stride, rowSize).CopyTo(row);
                if (frame.AlphaMode == CaptureAlphaMode.Straight) continue;
                for (var x = 0; x < row.Length; x += 4)
                {
                    if (frame.AlphaMode == CaptureAlphaMode.Opaque)
                    {
                        row[x + 3] = 255;
                        continue;
                    }

                    var alpha = row[x + 3];
                    for (var channel = 0; channel < 3; channel++)
                        row[x + channel] = alpha == 0
                            ? (byte)0
                            : (byte)Math.Min(255, (row[x + channel] * 255 + alpha / 2) / alpha);
                }
            }

            return result;
        }
    }
}
