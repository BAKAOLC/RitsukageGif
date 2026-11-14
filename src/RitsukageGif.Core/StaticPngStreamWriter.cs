using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace RitsukageGif.Core
{
    public static class StaticPngStreamWriter
    {
        public static void Write(Stream stream, int width, int height, StaticPngRowReader readRow,
            CancellationToken token, CaptureAlphaMode? hdrAlphaMode = null)
        {
            ArgumentNullException.ThrowIfNull(readRow);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if (hdrAlphaMode is { } alpha && !Enum.IsDefined(alpha))
                throw new ArgumentOutOfRangeException(nameof(hdrAlphaMode));
            token.ThrowIfCancellationRequested();
            var hdr = hdrAlphaMode.HasValue;
            var bytesPerPixel = hdr ? 8 : 4;
            var byteCount = checked(width * bytesPerPixel);
            var bufferSize = checked(byteCount * 2 + 1);

            stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            Span<byte> header = stackalloc byte[13];
            header.Clear();
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
            header[8] = hdr ? (byte)16 : (byte)8;
            header[9] = 6;
            HdrPngDataStream.WriteChunk(stream, "IHDR"u8, header);
            if (hdr) HdrPngDataStream.WriteChunk(stream, "cICP"u8, [9, 16, 0, 1]);
            else HdrPngDataStream.WriteChunk(stream, "sRGB"u8, [0]);

            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                var input = buffer.AsSpan(0, byteCount);
                var scanline = buffer.AsSpan(byteCount, byteCount + 1);
                scanline[0] = 1;
                using var chunks = new HdrPngDataStream(stream, token);
                using var compressed = new ZLibStream(chunks, CompressionLevel.Optimal, true);
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    readRow(y, input, token);
                    if (hdrAlphaMode is { } mode)
                    {
                        HdrPngColor.EncodeRow(input, scanline[1..], mode);
                        for (var i = scanline.Length - 1; i > bytesPerPixel; i--)
                            scanline[i] = unchecked((byte)(scanline[i] - scanline[i - bytesPerPixel]));
                    }
                    else
                    {
                        StreamingPngWriter.FilterRow(input, scanline[1..]);
                    }

                    compressed.Write(scanline);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            token.ThrowIfCancellationRequested();
            HdrPngDataStream.WriteChunk(stream, "IEND"u8, []);
        }
    }
}
