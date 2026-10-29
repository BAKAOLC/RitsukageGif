using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    public static class PngImageResampler
    {
        public static byte[] Resize(byte[] png, int width, int height, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var original = PngImageCodec.ReadDimensions(png);
            if (original.Width == width && original.Height == height)
                return PngImageCodec.Crop(png, new(0, 0, width, height), token);
            if (width <= 0 || height <= 0 || (long)width * height > 128L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(width));
            var source = PngImageCodec.Decode(png, token);
            var gray = source.ColorType is 0 or 4;
            var channels = gray ? 2 : 4;
            var depth = source.Depth == 16 ? (byte)16 : (byte)8;
            var componentBytes = depth / 8;
            var outputLength = (long)width * height * channels * componentBytes;
            if (outputLength > 512L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(width));
            var output = new byte[(int)outputLength];
            var palette = source.Metadata.FirstOrDefault(chunk => chunk.Type == "PLTE").Data;
            var transparency = source.Metadata.FirstOrDefault(chunk => chunk.Type == "tRNS").Data;
            var sourceChannels = source.ColorType switch
            {
                0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new InvalidDataException(),
            };
            var maximum = (1 << source.Depth) - 1;
            Span<double> sums = stackalloc double[4];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                var yStart = Math.Max(0,
                    (y + 0.5) * source.Height / height - Math.Max(1, (double)source.Height / height) / 2);
                var yEnd = Math.Min(source.Height,
                    (y + 0.5) * source.Height / height + Math.Max(1, (double)source.Height / height) / 2);
                for (var x = 0; x < width; x++)
                {
                    sums.Clear();
                    var xStart = Math.Max(0,
                        (x + 0.5) * source.Width / width - Math.Max(1, (double)source.Width / width) / 2);
                    var xEnd = Math.Min(source.Width,
                        (x + 0.5) * source.Width / width + Math.Max(1, (double)source.Width / width) / 2);
                    double weightSum = 0;
                    for (var sy = (int)yStart; sy < Math.Ceiling(yEnd); sy++)
                    {
                        token.ThrowIfCancellationRequested();
                        var wy = Math.Min(sy + 1, yEnd) - Math.Max(sy, yStart);
                        for (var sx = (int)xStart; sx < Math.Ceiling(xEnd); sx++)
                        {
                            var weight = wy * (Math.Min(sx + 1, xEnd) - Math.Max(sx, xStart));
                            var offset = (sy * source.Width + sx) * sourceChannels;
                            var alpha = Alpha(offset);
                            for (var channel = 0; channel < channels - 1; channel++)
                            {
                                double component;
                                if (source.ColorType == 3)
                                {
                                    var index = source.Samples[offset];
                                    if (index * 3 + 2 >= palette.Length) throw new InvalidDataException();
                                    component = palette.Span[index * 3 + channel] / 255d;
                                }
                                else
                                {
                                    component = Sample(offset + channel) / (double)maximum;
                                }

                                sums[channel] += component * alpha * weight;
                            }

                            sums[channels - 1] += alpha * weight;
                            weightSum += weight;
                        }
                    }

                    var outputOffset = (y * width + x) * channels * componentBytes;
                    for (var channel = 0; channel < channels; channel++)
                    {
                        var denominator = channel == channels - 1 ? weightSum : sums[channels - 1];
                        var value = denominator > 0 ? sums[channel] / denominator : 0;
                        var sample = (ushort)Math.Clamp(Math.Round(value * (depth == 16 ? 65535 : 255)), 0,
                            depth == 16 ? 65535 : 255);
                        if (depth == 16)
                            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(outputOffset + channel * 2), sample);
                        else output[outputOffset + channel] = (byte)sample;
                    }
                }
            }

            var metadata = source.Metadata
                .Where(chunk => chunk.Type is not ("PLTE" or "tRNS" or "sBIT" or "bKGD" or "hIST")).ToArray();
            return PngImageCodec.Encode(new(width, height, depth, gray ? (byte)4 : (byte)6, output, metadata), token);

            int Sample(int component)
            {
                return source.Depth == 16
                    ? BinaryPrimitives.ReadUInt16BigEndian(source.Samples.AsSpan(component * 2))
                    : source.Samples[component];
            }

            double Alpha(int offset)
            {
                return source.ColorType switch
                {
                    4 or 6 => Sample(offset + sourceChannels - 1) / (double)maximum,
                    3 => source.Samples[offset] < transparency.Length
                        ? transparency.Span[source.Samples[offset]] / 255d
                        : 1,
                    0 when transparency.Length == 2 => Sample(offset) ==
                                                       BinaryPrimitives.ReadUInt16BigEndian(transparency.Span)
                        ? 0
                        : 1,
                    2 when transparency.Length == 6 &&
                           Sample(offset) == BinaryPrimitives.ReadUInt16BigEndian(transparency.Span) &&
                           Sample(offset + 1) == BinaryPrimitives.ReadUInt16BigEndian(transparency.Span[2..]) &&
                           Sample(offset + 2) == BinaryPrimitives.ReadUInt16BigEndian(transparency.Span[4..]) => 0,
                    _ => 1,
                };
            }
        }
    }
}
