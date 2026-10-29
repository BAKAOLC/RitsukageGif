using System.Buffers.Binary;
using System.IO.Compression;

namespace RitsukageGif.Core
{
    public static partial class PngImageCodec
    {
        public static byte[] Encode(PngRaster raster, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(raster);
            token.ThrowIfCancellationRequested();
            var targetWidth = raster.Width;
            var targetHeight = raster.Height;
            var depth = raster.Depth;
            var typeCode = raster.ColorType;
            var channels = typeCode switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
            var validDepth = typeCode switch
            {
                0 => depth is 1 or 2 or 4 or 8 or 16,
                3 => depth is 1 or 2 or 4 or 8,
                2 or 4 or 6 => depth is 8 or 16,
                _ => false,
            };
            var bits = channels * depth;
            var pixelBytes = Math.Max(1, bits / 8);
            var outputPixels = raster.Samples;
            if (targetWidth <= 0 || targetHeight <= 0 || (long)targetWidth * targetHeight > 128L * 1024 * 1024 ||
                !validDepth || (long)targetWidth * targetHeight * pixelBytes > 512L * 1024 * 1024 ||
                outputPixels is null || (long)targetWidth * targetHeight * pixelBytes != outputPixels.Length ||
                raster.Metadata is null) throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(raster));
            var paletteEntries = ValidateRasterMetadata(raster);
            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, targetWidth);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), targetHeight);
            header[8] = depth;
            header[9] = typeCode;
            using var result = new MemoryStream();
            result.Write(Signature);
            WriteTransformPngChunk(result, "IHDR", header, token);
            foreach (var (chunkType, sourceData) in raster.Metadata)
            {
                token.ThrowIfCancellationRequested();
                if (chunkType is null || chunkType.Length != 4 ||
                    chunkType.Any(character => !char.IsAsciiLetter(character)) ||
                    (char.IsUpper(chunkType[0]) && chunkType != "PLTE") ||
                    chunkType is "acTL" or "fcTL" or "fdAT" or "eXIf")
                    throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(raster));
                if (IsRawProfileChunk(chunkType, sourceData, "exif") ||
                    IsRawProfileChunk(chunkType, sourceData, "APP1")) continue;
                WriteTransformPngChunk(result, chunkType, sourceData.Span, token);
            }

            using (var data = new MemoryStream())
            {
                using (var deflate = new ZLibStream(data, CompressionLevel.Optimal, true))
                {
                    var row = new byte[checked((int)(((long)targetWidth * bits + 7) / 8)) + 1];
                    for (var y = 0; y < targetHeight; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        Array.Clear(row);
                        for (var x = 0; x < targetWidth; x++)
                        {
                            if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                            if ((bits < 8 && outputPixels[y * targetWidth + x] >= 1 << depth) ||
                                (typeCode == 3 && outputPixels[y * targetWidth + x] >= paletteEntries))
                                throw new InvalidDataException();
                            if (bits < 8)
                                row[1 + x * bits / 8] |=
                                    (byte)(outputPixels[y * targetWidth + x] << (8 - bits - x * bits % 8));
                            else
                                outputPixels.AsSpan((y * targetWidth + x) * pixelBytes, pixelBytes)
                                    .CopyTo(row.AsSpan(1 + x * pixelBytes));
                        }

                        if (bits >= 8)
                        {
                            row[0] = 1;
                            for (var index = row.Length - 1; index > pixelBytes; index--)
                            {
                                if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
                                row[index] = unchecked((byte)(row[index] - row[index - pixelBytes]));
                            }
                        }

                        for (var offset = 0; offset < row.Length; offset += 65536)
                        {
                            token.ThrowIfCancellationRequested();
                            deflate.Write(row.AsSpan(offset, Math.Min(65536, row.Length - offset)));
                        }
                    }
                }

                WriteTransformPngChunk(result, "IDAT", data.GetBuffer().AsSpan(0, (int)data.Length), token);
            }

            WriteTransformPngChunk(result, "IEND", [], token);
            token.ThrowIfCancellationRequested();
            return result.ToArray();
        }
    }
}
