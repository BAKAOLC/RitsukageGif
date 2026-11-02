using System.Buffers.Binary;
using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class ImagePreviewResampler
    {
        internal static PngRaster SampleSdr(ReadOnlyMemory<byte> source, int sourceWidth, int sourceHeight,
            bool sixteenBit, Rect region, PixelSize size,
            IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> metadata, CancellationToken token)
        {
            var componentBytes = sixteenBit ? 2 : 1;
            var stride = checked(sourceWidth * componentBytes * 4);
            var output = new byte[checked(size.Width * size.Height * 8)];
            var footprintWidth = Math.Max(1, region.Width / size.Width);
            var footprintHeight = Math.Max(1, region.Height / size.Height);
            Span<double> sums = stackalloc double[4];
            for (var y = 0; y < size.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var top = Math.Max(0, region.Y + (y + .5) * region.Height / size.Height - footprintHeight / 2);
                var bottom = Math.Min(sourceHeight,
                    region.Y + (y + .5) * region.Height / size.Height + footprintHeight / 2);
                for (var x = 0; x < size.Width; x++)
                {
                    sums.Clear();
                    var left = Math.Max(0, region.X + (x + .5) * region.Width / size.Width - footprintWidth / 2);
                    var right = Math.Min(sourceWidth,
                        region.X + (x + .5) * region.Width / size.Width + footprintWidth / 2);
                    double weightSum = 0;
                    for (var sy = (int)top; sy < Math.Ceiling(bottom); sy++)
                    {
                        token.ThrowIfCancellationRequested();
                        var row = source.Span.Slice(sy * stride, stride);
                        var wy = Math.Min(sy + 1, bottom) - Math.Max(sy, top);
                        for (var sx = (int)left; sx < Math.Ceiling(right); sx++)
                        {
                            var weight = wy * (Math.Min(sx + 1, right) - Math.Max(sx, left));
                            var offset = sx * componentBytes * 4;
                            var alpha = sixteenBit
                                ? BinaryPrimitives.ReadUInt16BigEndian(row[(offset + 6)..]) / 65535d
                                : row[offset + 3] / 255d;
                            for (var channel = 0; channel < 3; channel++)
                            {
                                var value = sixteenBit
                                    ? BinaryPrimitives.ReadUInt16BigEndian(row[(offset + channel * 2)..]) / 65535d
                                    : row[offset + 2 - channel] / 255d;
                                sums[channel] += value * alpha * weight;
                            }

                            sums[3] += alpha * weight;
                            weightSum += weight;
                        }
                    }

                    var target = output.AsSpan((y * size.Width + x) * 8, 8);
                    for (var channel = 0; channel < 3; channel++)
                        BinaryPrimitives.WriteUInt16BigEndian(target[(channel * 2)..],
                            sums[3] > 0
                                ? (ushort)Math.Clamp(Math.Round(sums[channel] / sums[3] * 65535), 0, 65535)
                                : (ushort)0);
                    BinaryPrimitives.WriteUInt16BigEndian(target[6..],
                        (ushort)Math.Clamp(Math.Round(sums[3] / weightSum * 65535), 0, 65535));
                }
            }

            return new(size.Width, size.Height, 16, 6, output, metadata);
        }
    }
}
