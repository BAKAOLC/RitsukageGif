using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    public static partial class PngAnimation
    {
        public static PngRaster ReadHighPrecisionFrame(string path, int frameIndex, CancellationToken token = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
            byte[]? canvas = null;
            var index = 0;
            foreach (var (png, canvasWidth, canvasHeight, region, _, disposal, blend, poster) in ReadEncodedFrames(path,
                         token))
            {
                token.ThrowIfCancellationRequested();
                if (poster) continue;
                var source =
                    PngColorTransform.NormalizeAnnotationRaster(PngImageCodec.Decode(png, token), token);
                canvas ??= new byte[checked(canvasWidth * canvasHeight * 8)];
                byte[]? previous = null;
                if (disposal == 2 && index != frameIndex)
                {
                    previous = new byte[checked(region.Width * region.Height * 8)];
                    for (var y = 0; y < region.Height; y++)
                        Buffer.BlockCopy(canvas, ((region.Y + y) * canvasWidth + region.X) * 8,
                            previous, y * region.Width * 8, region.Width * 8);
                }

                for (var y = 0; y < region.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < region.Width; x++)
                    {
                        var pixel = source.Samples.AsSpan((y * region.Width + x) * 8, 8);
                        var target = canvas.AsSpan(((region.Y + y) * canvasWidth + region.X + x) * 8, 8);
                        if (blend == 0) pixel.CopyTo(target);
                        else BlendHighPrecisionFrame(pixel, target);
                    }
                }

                if (index++ == frameIndex)
                    return new(canvasWidth, canvasHeight, 16, 6, canvas, source.Metadata);
                if (disposal == 1)
                    for (var y = 0; y < region.Height; y++)
                        Array.Clear(canvas, ((region.Y + y) * canvasWidth + region.X) * 8, region.Width * 8);
                else if (previous is not null)
                    for (var y = 0; y < region.Height; y++)
                        Buffer.BlockCopy(previous, y * region.Width * 8, canvas,
                            ((region.Y + y) * canvasWidth + region.X) * 8, region.Width * 8);
            }

            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        private static void BlendHighPrecisionFrame(ReadOnlySpan<byte> source, Span<byte> target)
        {
            var alpha = BinaryPrimitives.ReadUInt16BigEndian(source[6..]);
            switch (alpha)
            {
                case 0: return;
                case ushort.MaxValue:
                    source.CopyTo(target);
                    return;
            }

            var front = (ulong)alpha * ushort.MaxValue;
            var back = (ulong)BinaryPrimitives.ReadUInt16BigEndian(target[6..]) * (uint)(ushort.MaxValue - alpha);
            var weight = front + back;
            for (var channel = 0; channel < 3; channel++)
                BinaryPrimitives.WriteUInt16BigEndian(target[(channel * 2)..],
                    (ushort)((BinaryPrimitives.ReadUInt16BigEndian(source[(channel * 2)..]) * front +
                              BinaryPrimitives.ReadUInt16BigEndian(target[(channel * 2)..]) * back + weight / 2) /
                             weight));
            BinaryPrimitives.WriteUInt16BigEndian(target[6..],
                (ushort)((weight + ushort.MaxValue / 2) / ushort.MaxValue));
        }
    }
}
