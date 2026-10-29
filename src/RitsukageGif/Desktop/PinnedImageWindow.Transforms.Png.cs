using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private static byte[] TransformPng(byte[] png, PinnedImageTransform transform, CancellationToken token)
        {
            var raster = PngImageCodec.Decode(png, token);
            var width = raster.Width;
            var height = raster.Height;
            var depth = raster.Depth;
            var typeCode = raster.ColorType;
            var pixels = raster.Samples;
            var chunks = raster.Metadata.ToList();
            var channels = typeCode switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
            var bits = channels * depth;
            var pixelBytes = Math.Max(1, bits / 8);
            var effect = transform is PinnedImageTransform.Grayscale or PinnedImageTransform.Invert;
            if (effect && chunks.Any(chunk => chunk.Type == "iCCP" ||
                                              IsTransformRawProfileChunk(chunk.Type, chunk.Data, "icc") ||
                                              IsTransformRawProfileChunk(chunk.Type, chunk.Data, "icm")))
            {
                pixels = ConvertTransformPngProfile(pixels, width, height, depth, typeCode, chunks, token);
                depth = depth == 16 ? (byte)16 : (byte)8;
                typeCode = 6;
                channels = 4;
                pixelBytes = depth / 8 * channels;
                chunks.RemoveAll(chunk =>
                    chunk.Type is "iCCP" or "gAMA" or "cHRM" or "cICP" or "sRGB" or "sBIT" or "PLTE" or "tRNS"
                        or "bKGD");
                chunks.RemoveAll(chunk =>
                    IsTransformRawProfileChunk(chunk.Type, chunk.Data, "icc") ||
                    IsTransformRawProfileChunk(chunk.Type, chunk.Data, "icm"));
                chunks.Add(("sRGB", new byte[] { 0 }));
            }

            if (effect && typeCode == 2 && chunks.FirstOrDefault(chunk => chunk.Type == "tRNS").Data is
                    { Length: > 0 } transparency)
            {
                if (transparency.Length != 6) throw new InvalidDataException();
                var expandedLength = (long)width * height * 4 * (depth / 8);
                if (expandedLength > 512L * 1024 * 1024)
                    throw new ArgumentException(UiText.Get("RecordingTooLarge"), nameof(png));
                var expanded = new byte[checked((int)expandedLength)];
                var componentBytes = depth / 8;
                var alphaMaximum = (1 << depth) - 1;
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < width; x++)
                    {
                        if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                        var source = pixels.AsSpan((y * width + x) * pixelBytes, pixelBytes);
                        var destination = expanded.AsSpan((y * width + x) * componentBytes * 4, componentBytes * 4);
                        source.CopyTo(destination);
                        var transparent = true;
                        for (var component = 0; component < 3; component++)
                        {
                            var value = componentBytes == 1
                                ? source[component]
                                : BinaryPrimitives.ReadUInt16BigEndian(source[(component * 2)..]);
                            transparent &= value ==
                                           BinaryPrimitives.ReadUInt16BigEndian(transparency.Span[(component * 2)..]);
                        }

                        if (componentBytes == 1) destination[3] = transparent ? (byte)0 : (byte)alphaMaximum;
                        else
                            BinaryPrimitives.WriteUInt16BigEndian(destination[6..],
                                transparent ? (ushort)0 : (ushort)alphaMaximum);
                    }
                }

                pixels = expanded;
                channels = 4;
                pixelBytes = componentBytes * 4;
                typeCode = 6;
                chunks.RemoveAll(chunk => chunk.Type == "tRNS");
            }

            var targetWidth = transform == PinnedImageTransform.RotateClockwise ? height : width;
            var targetHeight = transform == PinnedImageTransform.RotateClockwise ? width : height;
            var outputPixels = effect ? pixels : new byte[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                    var source = pixels.AsSpan((y * width + x) * pixelBytes, pixelBytes);
                    if (effect)
                    {
                        if (typeCode != 3) TransformPngSample(source, channels, depth, transform);
                    }
                    else
                    {
                        var point = TransformPoint(x, y, width, height, transform);
                        source.CopyTo(outputPixels.AsSpan((point.Y * targetWidth + point.X) * pixelBytes, pixelBytes));
                    }
                }
            }

            var metadata = new List<(string Type, ReadOnlyMemory<byte> Data)>();
            foreach (var (chunkType, sourceData) in chunks)
            {
                token.ThrowIfCancellationRequested();
                if (effect && chunkType is "sBIT" or "bKGD") continue;
                var data = sourceData;
                switch (chunkType)
                {
                    case "pHYs" when transform == PinnedImageTransform.RotateClockwise && data.Length == 9:
                        var resolution = data.ToArray();
                        sourceData.Span[..4].CopyTo(resolution.AsSpan(4));
                        sourceData.Span.Slice(4, 4).CopyTo(resolution);
                        data = resolution;
                        break;
                    case "PLTE" when effect:
                        var palette = data.ToArray();
                        if (palette.Length == 0 || palette.Length % 3 != 0) throw new InvalidDataException();
                        for (var index = 0; index < palette.Length; index += 3)
                            TransformPngSample(palette.AsSpan(index, 3), 3, 8, transform);
                        data = palette;
                        break;
                    case "tRNS" when effect && typeCode == 0 && transform == PinnedImageTransform.Invert:
                        if (data.Length != 2) throw new InvalidDataException();
                        var transparencySample = data.ToArray();
                        var value = BinaryPrimitives.ReadUInt16BigEndian(transparencySample);
                        BinaryPrimitives.WriteUInt16BigEndian(transparencySample,
                            checked((ushort)((1 << depth) - 1 - value)));
                        data = transparencySample;
                        break;
                }

                metadata.Add((chunkType, data));
            }

            return PngImageCodec.Encode(new(targetWidth, targetHeight, depth, typeCode, outputPixels, metadata), token);
        }

        private static void TransformPngSample(Span<byte> sample, int channels, int depth,
            PinnedImageTransform transform)
        {
            var maximum = (1 << depth) - 1;
            var size = depth == 16 ? 2 : 1;
            var red = size == 1 ? (int)sample[0] : BinaryPrimitives.ReadUInt16BigEndian(sample);
            var green = channels < 3 ? red : size == 1 ? sample[1] : BinaryPrimitives.ReadUInt16BigEndian(sample[2..]);
            var blue = channels < 3 ? red : size == 1 ? sample[2] : BinaryPrimitives.ReadUInt16BigEndian(sample[4..]);
            if (transform == PinnedImageTransform.Grayscale)
            {
                red = green = blue = (int)Math.Round((77d * red + 150d * green + 29d * blue) / 256);
            }
            else
            {
                red = maximum - red;
                green = maximum - green;
                blue = maximum - blue;
            }

            if (size == 1)
            {
                sample[0] = (byte)red;
                if (channels >= 3)
                {
                    sample[1] = (byte)green;
                    sample[2] = (byte)blue;
                }
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(sample, (ushort)red);
                if (channels >= 3)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(sample[2..], (ushort)green);
                    BinaryPrimitives.WriteUInt16BigEndian(sample[4..], (ushort)blue);
                }
            }
        }
    }
}
