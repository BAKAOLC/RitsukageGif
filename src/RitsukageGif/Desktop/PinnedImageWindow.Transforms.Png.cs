using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private static byte[] TransformPngCore(byte[] png, PinnedImageTransform transform, PinnedViewTransform? view,
            CancellationToken token, RgbColorMatrix? colorMatrix = null)
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
            var effect = colorMatrix is not null ||
                         (view is null && transform is PinnedImageTransform.Grayscale or PinnedImageTransform.Invert);
            var matrix = colorMatrix ?? (transform == PinnedImageTransform.Grayscale
                ? RgbColorMatrix.Grayscale
                : RgbColorMatrix.Invert);
            var orientation = view ?? (effect ? default : default(PinnedViewTransform).Apply(transform));
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

            if (effect && typeCode == 0 && chunks.FirstOrDefault(chunk => chunk.Type == "tRNS").Data is
                    { Length: > 0 } grayTransparency)
            {
                if (grayTransparency.Length != 2) throw new InvalidDataException();
                var transparent = BinaryPrimitives.ReadUInt16BigEndian(grayTransparency.Span);
                var oldMaximum = (1 << depth) - 1;
                var newDepth = depth == 16 ? (byte)16 : (byte)8;
                var componentBytes = newDepth / 8;
                var expanded = new byte[checked(width * height * componentBytes * 2)];
                for (var index = 0; index < width * height; index++)
                {
                    if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                    var value = depth == 16
                        ? BinaryPrimitives.ReadUInt16BigEndian(pixels.AsSpan(index * 2))
                        : pixels[index];
                    var destination = expanded.AsSpan(index * componentBytes * 2, componentBytes * 2);
                    if (componentBytes == 1)
                    {
                        destination[0] = (byte)(value * 255 / oldMaximum);
                        destination[1] = value == transparent ? (byte)0 : (byte)255;
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(destination, value);
                        BinaryPrimitives.WriteUInt16BigEndian(destination[2..],
                            value == transparent ? (ushort)0 : ushort.MaxValue);
                    }
                }

                pixels = expanded;
                depth = newDepth;
                channels = 2;
                pixelBytes = componentBytes * 2;
                typeCode = 4;
                chunks.RemoveAll(chunk => chunk.Type == "tRNS");
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

            var targetWidth = orientation.SwapsAxes ? height : width;
            var targetHeight = orientation.SwapsAxes ? width : height;
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
                        if (typeCode != 3) TransformPngSample(source, channels, depth, matrix);
                    }
                    else
                    {
                        var point = orientation.PixelPosition(x, y, width, height);
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
                    case "pHYs" when orientation.SwapsAxes && data.Length == 9:
                        var resolution = data.ToArray();
                        sourceData.Span[..4].CopyTo(resolution.AsSpan(4));
                        sourceData.Span.Slice(4, 4).CopyTo(resolution);
                        data = resolution;
                        break;
                    case "PLTE" when effect:
                        var palette = data.ToArray();
                        if (palette.Length == 0 || palette.Length % 3 != 0) throw new InvalidDataException();
                        for (var index = 0; index < palette.Length; index += 3)
                            TransformPngSample(palette.AsSpan(index, 3), 3, 8, matrix);
                        data = palette;
                        break;
                }

                metadata.Add((chunkType, data));
            }

            return PngImageCodec.Encode(new(targetWidth, targetHeight, depth, typeCode, outputPixels, metadata), token);
        }

        private static void TransformPngSample(Span<byte> sample, int channels, int depth,
            RgbColorMatrix matrix)
        {
            var maximum = (1 << depth) - 1;
            var size = depth == 16 ? 2 : 1;
            var red = size == 1 ? (int)sample[0] : BinaryPrimitives.ReadUInt16BigEndian(sample);
            var green = channels < 3 ? red : size == 1 ? sample[1] : BinaryPrimitives.ReadUInt16BigEndian(sample[2..]);
            var blue = channels < 3 ? red : size == 1 ? sample[2] : BinaryPrimitives.ReadUInt16BigEndian(sample[4..]);
            var transformed = matrix.Transform(new(red, green, blue), maximum / 255f);
            red = (int)Math.Clamp(MathF.Round(transformed.X), 0, maximum);
            green = (int)Math.Clamp(MathF.Round(transformed.Y), 0, maximum);
            blue = (int)Math.Clamp(MathF.Round(transformed.Z), 0, maximum);

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
