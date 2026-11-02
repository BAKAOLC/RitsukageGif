using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotDecorationPng
    {
        internal static byte[] ApplyShape(byte[] png, SelectionShape shape, PixelRect region,
            CancellationToken token)
        {
            var source = PngImageCodec.Decode(png, token);
            if (source.Depth < 8)
            {
                var samples = source.Samples;
                var metadata = source.Metadata;
                if (source.ColorType == 0)
                {
                    var maximum = (1 << source.Depth) - 1;
                    samples = [.. samples.Select(value => (byte)(value * 255 / maximum))];
                    metadata =
                    [
                        .. metadata.Select(chunk =>
                        {
                            if (chunk.Type is not ("tRNS" or "bKGD")) return chunk;
                            var value = BinaryPrimitives.ReadUInt16BigEndian(chunk.Data.Span);
                            ReadOnlyMemory<byte> expanded = (byte[])[0, (byte)(value * 255 / maximum)];
                            return (chunk.Type, expanded);
                        }),
                    ];
                }

                source = source with { Depth = 8, Samples = samples, Metadata = metadata };
            }

            var mask = SelectionShapeMask.Rasterize(shape, region, source.Width, source.Height, token);
            var bytes = source.Depth == 16 ? 2 : 1;
            var output = new byte[checked(source.Width * source.Height * 4 * bytes)];
            var palette = source.Metadata.FirstOrDefault(chunk => chunk.Type == "PLTE").Data;
            var transparent = source.Metadata.FirstOrDefault(chunk => chunk.Type == "tRNS").Data;
            for (var y = 0; y < source.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < source.Width; x++)
                {
                    var destination = output.AsSpan((y * source.Width + x) * 4 * bytes, 4 * bytes);
                    CopyOpaquePixel(source, x, y, destination, palette.Span);
                    var alpha = ReadPixel(source, x, y, palette.Span, transparent.Span).W;
                    WriteComponent(destination, 3, bytes, alpha * (mask[y * source.Width + x] / 255f));
                }
            }

            return PngImageCodec.Encode(source with
            {
                ColorType = 6, Samples = output, Metadata = OutputMetadata(source, token),
            }, token);
        }
    }
}
