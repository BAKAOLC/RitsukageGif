using System.Buffers.Binary;
using System.IO.Compression;

namespace RitsukageGif.Core
{
    public static partial class HdrImageService
    {
        private static void WritePng(Stream stream, CapturedFrame frame, CancellationToken token)
        {
            stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            Span<byte> header = stackalloc byte[13];
            header.Clear();
            BinaryPrimitives.WriteInt32BigEndian(header, frame.Width);
            BinaryPrimitives.WriteInt32BigEndian(header[4..], frame.Height);
            header[8] = 16;
            header[9] = 6;
            HdrPngDataStream.WriteChunk(stream, "IHDR"u8, header);
            HdrPngDataStream.WriteChunk(stream, "cICP"u8, [9, 16, 0, 1]);
            var row = new byte[checked(frame.Width * 8 + 1)];
            using (var chunks = new HdrPngDataStream(stream, token))
            using (var compressed = new ZLibStream(chunks, CompressionLevel.Optimal, true))
                for (var y = 0; y < frame.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    HdrPngColor.EncodeRow(frame.Pixels.Span.Slice(y * frame.Stride, frame.Width * 8), row.AsSpan(1),
                        frame.AlphaMode);
                    // Sub filter preserves all 16 bits while compressing smooth UI areas efficiently.
                    row[0] = 1;
                    for (var i = row.Length - 1; i >= 9; i--) row[i] = unchecked((byte)(row[i] - row[i - 8]));
                    compressed.Write(row);
                }

            HdrPngDataStream.WriteChunk(stream, "IEND"u8, []);
        }
    }
}
