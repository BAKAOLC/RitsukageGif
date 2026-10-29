using System.Buffers.Binary;
using System.Numerics;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotDecorationPng
    {
        private static int Channels(byte colorType)
        {
            return colorType switch
            {
                0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4,
                _ => throw new NotSupportedException("Unsupported PNG color type."),
            };
        }

        private static Vector4 ReadPixel(PngRaster source, int x, int y, ReadOnlySpan<byte> palette,
            ReadOnlySpan<byte> transparency)
        {
            var channels = Channels(source.ColorType);
            var bytes = source.Depth == 16 ? 2 : 1;
            var pixel = source.Samples.AsSpan((y * source.Width + x) * channels * bytes, channels * bytes);
            if (source.ColorType == 3)
            {
                var index = pixel[0];
                if (palette.Length is < 3 or > 768 || palette.Length % 3 != 0 || index * 3 + 2 >= palette.Length)
                    throw new InvalidDataException("Invalid PNG palette.");
                return new(palette[index * 3] / 255f, palette[index * 3 + 1] / 255f,
                    palette[index * 3 + 2] / 255f, index < transparency.Length ? transparency[index] / 255f : 1);
            }

            var red = ReadComponent(pixel, 0, bytes);
            var green = channels < 3 ? red : ReadComponent(pixel, 1, bytes);
            var blue = channels < 3 ? red : ReadComponent(pixel, 2, bytes);
            var maximum = bytes == 1 ? 255 : 65535;
            var alpha = source.ColorType is 4 or 6 ? ReadComponent(pixel, channels - 1, bytes) : maximum;
            if (!transparency.IsEmpty && source.ColorType is 0 or 2)
            {
                if (transparency.Length != (source.ColorType == 0 ? 2 : 6)) throw new InvalidDataException();
                if (red == BinaryPrimitives.ReadUInt16BigEndian(transparency) &&
                    (source.ColorType == 0 || (green == BinaryPrimitives.ReadUInt16BigEndian(transparency[2..]) &&
                                               blue == BinaryPrimitives.ReadUInt16BigEndian(transparency[4..]))))
                    alpha = 0;
            }

            return new(red / (float)maximum, green / (float)maximum, blue / (float)maximum, alpha / (float)maximum);
        }

        private static int ReadComponent(ReadOnlySpan<byte> pixel, int component, int bytes)
        {
            return bytes == 1 ? pixel[component] : BinaryPrimitives.ReadUInt16BigEndian(pixel[(component * 2)..]);
        }

        private static void WriteComponent(Span<byte> pixel, int component, int bytes, float value)
        {
            var sample = (ushort)Math.Round(Math.Clamp((double)value, 0, 1) * (bytes == 1 ? 255 : 65535));
            if (bytes == 1) pixel[component] = (byte)sample;
            else BinaryPrimitives.WriteUInt16BigEndian(pixel[(component * 2)..], sample);
        }

        private static void CopyOpaquePixel(PngRaster source, int x, int y, Span<byte> destination,
            ReadOnlySpan<byte> palette)
        {
            var bytes = source.Depth == 16 ? 2 : 1;
            if (source.ColorType == 3)
            {
                palette.Slice(source.Samples[y * source.Width + x] * 3, 3).CopyTo(destination);
            }
            else
            {
                var channels = Channels(source.ColorType);
                var pixel = source.Samples.AsSpan((y * source.Width + x) * channels * bytes, channels * bytes);
                pixel[..bytes].CopyTo(destination);
                pixel.Slice(channels < 3 ? 0 : bytes, bytes).CopyTo(destination[bytes..]);
                pixel.Slice(channels < 3 ? 0 : bytes * 2, bytes).CopyTo(destination[(bytes * 2)..]);
            }

            destination[(3 * bytes)..].Fill(255);
        }

        private static List<(string Type, ReadOnlyMemory<byte> Data)> OutputMetadata(PngRaster source,
            CancellationToken token)
        {
            var metadata = source.Metadata;
            if (source.ColorType is 0 or 4)
            {
                var sample = new byte[Channels(source.ColorType) * 2];
                metadata = PngColorTransform.NormalizeAnnotationRaster(new(1, 1, 16, source.ColorType, sample,
                    metadata), token).Metadata;
            }

            var result = new List<(string Type, ReadOnlyMemory<byte> Data)>();
            foreach (var chunk in metadata)
            {
                token.ThrowIfCancellationRequested();
                if (chunk.Type is "PLTE" or "tRNS" or "hIST") continue;
                switch (chunk.Type)
                {
                    case "bKGD" when source.ColorType == 3:
                    {
                        var palette = source.Metadata.FirstOrDefault(value => value.Type == "PLTE").Data.Span;
                        if (chunk.Data.Length != 1 || chunk.Data.Span[0] * 3 + 2 >= palette.Length)
                            throw new InvalidDataException("Invalid PNG palette background.");
                        var index = chunk.Data.Span[0] * 3;
                        result.Add((chunk.Type, new byte[]
                        {
                            0, palette[index], 0, palette[index + 1], 0, palette[index + 2],
                        }));
                        break;
                    }
                    case "sBIT":
                    {
                        var bits = chunk.Data.Span;
                        if (bits.Length is not (3 or 4))
                            throw new InvalidDataException("Invalid PNG significant bits.");
                        result.Add((chunk.Type, new[] { bits[0], bits[1], bits[2], source.Depth }));
                        break;
                    }
                    default:
                    {
                        result.Add(chunk);
                        break;
                    }
                }
            }

            return result;
        }
    }
}
