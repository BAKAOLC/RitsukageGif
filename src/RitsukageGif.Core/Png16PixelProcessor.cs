using System.Buffers.Binary;
using System.Numerics;

namespace RitsukageGif.Core
{
    public static partial class Png16PixelProcessor
    {
        public static PngRaster ExpandRgba(PngRaster source, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(source.Samples);
            ArgumentNullException.ThrowIfNull(source.Metadata);
            if (source.Depth != 16 || source.ColorType is not (0 or 2 or 4 or 6))
                throw new ArgumentException("A direct-color 16-bit PNG is required.", nameof(source));
            var channels = source.ColorType switch { 0 => 1, 2 => 3, 4 => 2, _ => 4 };
            if (source.Width <= 0 || source.Height <= 0 || (long)source.Width * source.Height > 64L * 1024 * 1024 ||
                (long)source.Width * source.Height * channels * 2 != source.Samples.Length)
                throw new ArgumentException("Invalid 16-bit raster.", nameof(source));
            if (source.ColorType == 6) return source;
            var output = new byte[checked(source.Width * source.Height * 8)];
            var transparent = source.Metadata.FirstOrDefault(chunk => chunk.Type == "tRNS").Data;
            for (var y = 0; y < source.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < source.Width; x++)
                {
                    var original = source.Samples.AsSpan((y * source.Width + x) * channels * 2, channels * 2);
                    var destination = output.AsSpan((y * source.Width + x) * 8, 8);
                    var red = Read(original, 0);
                    var green = channels < 3 ? red : Read(original, 1);
                    var blue = channels < 3 ? red : Read(original, 2);
                    var alpha = source.ColorType == 4 ? Read(original, 1) : ushort.MaxValue;
                    if (!transparent.IsEmpty && (source.ColorType == 0
                            ? red == Read(transparent.Span, 0)
                            : red == Read(transparent.Span, 0) && green == Read(transparent.Span, 1) &&
                              blue == Read(transparent.Span, 2)))
                        alpha = 0;
                    Write(destination, 0, red);
                    Write(destination, 1, green);
                    Write(destination, 2, blue);
                    Write(destination, 3, alpha);
                }
            }

            var metadata = new List<(string Type, ReadOnlyMemory<byte> Data)>();
            foreach (var chunk in source.Metadata)
            {
                if (chunk.Type is "tRNS" or "PLTE") continue;
                switch (chunk.Type)
                {
                    case "sBIT":
                    {
                        var bits = chunk.Data.Span;
                        if (bits.Length != channels || bits.ToArray().Any(value => value is < 1 or > 16))
                            throw new InvalidDataException();
                        metadata.Add((chunk.Type, new[]
                        {
                            bits[0], channels < 3 ? bits[0] : bits[1],
                            channels < 3 ? bits[0] : bits[2], source.ColorType == 4 ? bits[1] : (byte)16,
                        }));
                        break;
                    }
                    case "bKGD" when source.ColorType is 0 or 4:
                    {
                        if (chunk.Data.Length != 2) throw new InvalidDataException();
                        metadata.Add((chunk.Type, new[]
                        {
                            chunk.Data.Span[0], chunk.Data.Span[1],
                            chunk.Data.Span[0], chunk.Data.Span[1], chunk.Data.Span[0], chunk.Data.Span[1],
                        }));
                        break;
                    }
                    default:
                        metadata.Add(chunk);
                        break;
                }
            }

            return source with { ColorType = 6, Samples = output, Metadata = metadata };
        }

        public static void Blend(byte[] target, int width, int height, PixelRect area, ushort[] rgba,
            CancellationToken token = default)
        {
            Validate(target, width, height);
            ArgumentNullException.ThrowIfNull(rgba);
            if (area.IsEmpty || area.X < 0 || area.Y < 0 || (long)area.X + area.Width > width ||
                (long)area.Y + area.Height > height || (long)area.Width * area.Height * 4 != rgba.Length)
                throw new ArgumentException("Invalid overlay region.", nameof(area));
            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < area.Width; x++)
                {
                    var source = rgba.AsSpan((y * area.Width + x) * 4, 4);
                    var alpha = (ulong)source[3];
                    if (alpha == 0) continue;
                    var destination = target.AsSpan(((area.Y + y) * width + area.X + x) * 8, 8);
                    var behind = Read(destination, 3) * (65535 - alpha);
                    var weight = alpha * 65535 + behind;
                    for (var channel = 0; channel < 3; channel++)
                        Write(destination, channel, (ushort)((source[channel] * alpha * 65535 +
                                                              Read(destination, channel) * behind + weight / 2) /
                                                             weight));
                    Write(destination, 3, (ushort)((weight + 32767) / 65535));
                }
            }
        }

        public static void Restore(byte[] target, byte[] original, int width, int height, PixelRect area,
            ushort[] coverage, CancellationToken token = default)
        {
            Validate(target, width, height);
            Validate(original, width, height);
            ArgumentNullException.ThrowIfNull(coverage);
            if (area.IsEmpty || area.X < 0 || area.Y < 0 || (long)area.X + area.Width > width ||
                (long)area.Y + area.Height > height || (long)area.Width * area.Height != coverage.Length)
                throw new ArgumentException("Invalid restoration region.", nameof(area));
            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < area.Width; x++)
                {
                    var mask = coverage[y * area.Width + x];
                    if (mask == 0) continue;
                    var offset = ((area.Y + y) * width + area.X + x) * 8;
                    var source = original.AsSpan(offset, 8);
                    var destination = target.AsSpan(offset, 8);
                    if (mask == ushort.MaxValue)
                    {
                        source.CopyTo(destination);
                        continue;
                    }

                    var amount = (ulong)BitOperations.PopCount(mask);
                    var sourceWeight = Read(source, 3) * amount;
                    var behind = Read(destination, 3) * (16 - amount);
                    var weight = sourceWeight + behind;
                    for (var channel = 0; channel < 3; channel++)
                        Write(destination, channel, weight > 0
                            ? (ushort)((Read(source, channel) * sourceWeight +
                                        Read(destination, channel) * behind + weight / 2) / weight)
                            : (ushort)0);
                    Write(destination, 3, (ushort)((weight + 8) / 16));
                }
            }
        }

        private static void Validate(byte[] samples, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(samples);
            if (width <= 0 || height <= 0 || (long)width * height > 64L * 1024 * 1024 ||
                (long)width * height * 8 != samples.Length)
                throw new ArgumentException("Invalid 16-bit raster.", nameof(samples));
        }

        private static ushort Read(ReadOnlySpan<byte> pixel, int channel)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(pixel[(channel * 2)..]);
        }

        private static void Write(Span<byte> pixel, int channel, ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(pixel[(channel * 2)..], value);
        }

        private static PixelRect Clip(PixelRect area, int width, int height)
        {
            var left = Math.Clamp((long)area.X, 0, width);
            var top = Math.Clamp((long)area.Y, 0, height);
            var right = Math.Clamp((long)area.X + area.Width, 0, width);
            var bottom = Math.Clamp((long)area.Y + area.Height, 0, height);
            return new((int)left, (int)top, (int)Math.Max(0, right - left), (int)Math.Max(0, bottom - top));
        }
    }
}
