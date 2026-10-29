using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RitsukageGif.Core
{
    public static partial class PngImageCodec
    {
        // ReSharper disable once MemberCanBePrivate.Global
        public const int MaximumEncodedBytes = 256 * 1024 * 1024;
        private static readonly uint[] TransformPngCrcTable = CreateTransformPngCrcTable();
        private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

        public static PngRaster Decode(byte[] png, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(png);
            token.ThrowIfCancellationRequested();
            if (png.Length > MaximumEncodedBytes || png.Length < 33 ||
                !png.AsSpan(0, 8).SequenceEqual(Signature))
                throw new InvalidDataException();
            byte[]? header = null;
            var chunks = new List<(string Type, ReadOnlyMemory<byte> Data)>();
            using var compressed = new MemoryStream();
            var ended = false;
            for (var offset = 8; offset <= png.Length - 12;)
            {
                token.ThrowIfCancellationRequested();
                var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
                if (length > png.Length - offset - 12) throw new InvalidDataException();
                var size = (int)length;
                var type = Encoding.ASCII.GetString(png, offset + 4, 4);
                var data = png.AsMemory(offset + 8, size);
                var crc = ComputeTransformPngCrc(png.AsSpan(offset + 4, size + 4), token: token);
                if (crc != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + size + 8)))
                    throw new InvalidDataException();
                switch (type)
                {
                    case "IHDR":
                        if (header is not null || offset != 8 || size != 13) throw new InvalidDataException();
                        header = data.ToArray();
                        break;
                    case "IDAT": compressed.Write(data.Span); break;
                    case "IEND":
                        if (size != 0) throw new InvalidDataException();
                        ended = true;
                        break;
                    case "eXIf":
                    case "acTL":
                    case "fcTL":
                    case "fdAT":
                        break;
                    default:
                        if (char.IsUpper(type[0]) && type != "PLTE") throw new InvalidDataException();
                        if (IsRawProfileChunk(type, data, "exif") || IsRawProfileChunk(type, data, "APP1")) break;
                        if (type is "PLTE" or "tRNS" or "iCCP" or "gAMA" or "cHRM" or "sRGB" or "cICP" or "sBIT"
                                or "pHYs" or "bKGD" || char.IsLower(type[3]))
                            chunks.Add((type, data));
                        break;
                }

                offset += size + 12;
                if (ended) break;
            }

            if (!ended || header is null || compressed.Length == 0) throw new InvalidDataException();
            var width = BinaryPrimitives.ReadInt32BigEndian(header);
            var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
            var depth = header[8];
            var typeCode = header[9];
            var channels = typeCode switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
            var validDepth = typeCode switch
            {
                0 => depth is 1 or 2 or 4 or 8 or 16,
                3 => depth is 1 or 2 or 4 or 8,
                2 or 4 or 6 => depth is 8 or 16,
                _ => false,
            };
            if (width <= 0 || height <= 0 || (long)width * height > 128L * 1024 * 1024 ||
                !validDepth || header[10] != 0 || header[11] != 0 || header[12] > 1) throw new InvalidDataException();
            var bits = channels * depth;
            var pixelBytes = Math.Max(1, bits / 8);
            var decodedLength = (long)width * height * pixelBytes;
            if (decodedLength > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"), nameof(png));
            var pixels = new byte[checked((int)decodedLength)];
            compressed.Position = 0;
            using (var inflate = new ZLibStream(compressed, CompressionMode.Decompress, true))
            {
                if (header[12] == 0)
                {
                    ReadPass(0, 0, 1, 1);
                }
                else
                {
                    ReadPass(0, 0, 8, 8);
                    ReadPass(4, 0, 8, 8);
                    ReadPass(0, 4, 4, 8);
                    ReadPass(2, 0, 4, 4);
                    ReadPass(0, 2, 2, 4);
                    ReadPass(1, 0, 2, 2);
                    ReadPass(0, 1, 1, 2);
                }

                if (inflate.ReadByte() != -1) throw new InvalidDataException();

                void ReadPass(int xStart, int yStart, int xStep, int yStep)
                {
                    if (xStart >= width || yStart >= height) return;
                    var passWidth = (width - xStart + xStep - 1) / xStep;
                    var passHeight = (height - yStart + yStep - 1) / yStep;
                    var rowLength = checked((int)(((long)passWidth * bits + 7) / 8));
                    var previous = new byte[rowLength];
                    var row = new byte[rowLength];
                    for (var y = 0; y < passHeight; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        var filter = inflate.ReadByte();
                        if (filter is < 0 or > 4) throw new InvalidDataException();
                        for (var offset = 0; offset < row.Length; offset += 65536)
                        {
                            token.ThrowIfCancellationRequested();
                            inflate.ReadExactly(row.AsSpan(offset, Math.Min(65536, row.Length - offset)));
                        }

                        for (var index = 0; index < rowLength; index++)
                        {
                            if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
                            var left = index < pixelBytes ? 0 : row[index - pixelBytes];
                            var above = previous[index];
                            var upperLeft = index < pixelBytes ? 0 : previous[index - pixelBytes];
                            row[index] = unchecked((byte)(row[index] + filter switch
                            {
                                1 => left, 2 => above, 3 => (left + above) / 2,
                                4 => TransformPngPaeth(left, above, upperLeft), _ => 0,
                            }));
                        }

                        for (var x = 0; x < passWidth; x++)
                        {
                            if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                            var target = ((yStart + y * yStep) * width + xStart + x * xStep) * pixelBytes;
                            if (bits < 8)
                                pixels[target] = (byte)((row[x * bits / 8] >> (8 - bits - x * bits % 8)) &
                                                        ((1 << bits) - 1));
                            else row.AsSpan(x * pixelBytes, pixelBytes).CopyTo(pixels.AsSpan(target, pixelBytes));
                        }

                        (row, previous) = (previous, row);
                    }
                }
            }

            var raster = new PngRaster(width, height, depth, typeCode, pixels, chunks);
            ValidateRasterMetadata(raster);
            return raster;
        }

        private static int TransformPngPaeth(int left, int above, int upperLeft)
        {
            var prediction = left + above - upperLeft;
            var a = Math.Abs(prediction - left);
            var b = Math.Abs(prediction - above);
            var c = Math.Abs(prediction - upperLeft);
            return a <= b && a <= c ? left : b <= c ? above : upperLeft;
        }

        private static uint[] CreateTransformPngCrcTable()
        {
            var table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                var value = index;
                for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320u);
                table[index] = value;
            }

            return table;
        }

        private static uint ComputeTransformPngCrc(ReadOnlySpan<byte> bytes, uint crc = uint.MaxValue,
            CancellationToken token = default)
        {
            for (var index = 0; index < bytes.Length; index++)
            {
                if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
                crc = TransformPngCrcTable[(crc ^ bytes[index]) & 255] ^ (crc >> 8);
            }

            return crc ^ uint.MaxValue;
        }

        private static void WriteTransformPngChunk(Stream stream, string type, ReadOnlySpan<byte> data,
            CancellationToken token)
        {
            if (stream.Length + data.Length + 12 > MaximumEncodedBytes)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"), nameof(data));
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
            Encoding.ASCII.GetBytes(type, header[4..]);
            stream.Write(header);
            for (var offset = 0; offset < data.Length; offset += 65536)
            {
                token.ThrowIfCancellationRequested();
                stream.Write(data.Slice(offset, Math.Min(65536, data.Length - offset)));
            }

            var crc = ComputeTransformPngCrc(header[4..], token: token) ^ uint.MaxValue;
            crc = ComputeTransformPngCrc(data, crc, token);
            BinaryPrimitives.WriteUInt32BigEndian(header, crc);
            stream.Write(header[..4]);
        }

        private static bool IsRawProfileChunk(string type, ReadOnlyMemory<byte> data, string profile)
        {
            if (type is not ("tEXt" or "zTXt" or "iTXt")) return false;
            var separator = data.Span.IndexOf((byte)0);
            return separator > 0 && Encoding.Latin1.GetString(data.Span[..separator])
                .Equals("Raw profile type " + profile, StringComparison.OrdinalIgnoreCase);
        }
    }
}
