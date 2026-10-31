using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationPinMetadata
    {
        private static readonly uint[] PngCrcTable = CreatePngCrcTable();
        private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

        public byte[] EncodePngFrame(int index, CancellationToken token = default)
        {
            if (PngHeader is null)
                throw new InvalidOperationException("The animation does not contain PNG frame patches.");
            if ((uint)index >= Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
            token.ThrowIfCancellationRequested();
            var frame = Frames[index];
            var estimated = checked(45L + SharedChunks.Sum(chunk => (long)chunk.Length) +
                                    frame.Data.Sum(chunk => chunk.Length + 12L));
            if (estimated > MaximumEncodedBytes)
                throw new InvalidDataException("PNG frame exceeds the encoded size limit.");
            using var stream = new MemoryStream(checked((int)estimated));
            stream.Write(PngSignature);
            var header = PngHeader.ToArray();
            BinaryPrimitives.WriteInt32BigEndian(header, frame.Region.Width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), frame.Region.Height);
            WritePngChunk(stream, "IHDR"u8, header, token);
            foreach (var chunk in SharedChunks)
            {
                token.ThrowIfCancellationRequested();
                stream.Write(chunk.Span);
            }

            foreach (var chunk in frame.Data)
            {
                token.ThrowIfCancellationRequested();
                WritePngChunk(stream, "IDAT"u8, chunk.Span, token);
            }

            WritePngChunk(stream, "IEND"u8, [], token);
            return stream.ToArray();
        }

        private static AnimationPinMetadata? ReadPng(byte[] encoded, CancellationToken token)
        {
            var frames = new List<AnimationPinFrameInfo>();
            var shared = new List<ReadOnlyMemory<byte>>();
            var chunks = new List<ReadOnlyMemory<byte>>();
            AnimationPinFrameInfo? current = null;
            byte[]? header = null;
            var width = 0;
            var height = 0;
            var declaredFrames = 0u;
            var plays = 0;
            var seenIdat = false;
            var idatBytes = 0L;
            var currentUsesIdat = false;
            var idatFinished = false;
            var ended = false;
            var hdr = false;
            var hdrAncillary = false;
            var seenColor = false;
            var paletteEntries = 0;
            var seenTransparency = false;
            var sequence = 0UL;
            var chunkCount = 0;
            var offset = 8;
            while (offset < encoded.Length)
            {
                token.ThrowIfCancellationRequested();
                if (++chunkCount > 1000000 || encoded.Length - offset < 12) throw InvalidPng();
                var lengthValue = BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(offset));
                if (lengthValue > int.MaxValue || lengthValue > encoded.Length - offset - 12) throw InvalidPng();
                var length = (int)lengthValue;
                var type = encoded.AsSpan(offset + 4, 4);
                if (type[2] is < (byte)'A' or > (byte)'Z') throw InvalidPng();
                foreach (var value in type)
                    if (value is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z'))
                        throw InvalidPng();
                var data = encoded.AsSpan(offset + 8, length);
                if (PngCrc(encoded.AsSpan(offset + 4, length + 4), token) !=
                    BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(offset + length + 8)) ||
                    (header is null && !type.SequenceEqual("IHDR"u8))) throw InvalidPng();
                if (seenIdat && !type.SequenceEqual("IDAT"u8)) idatFinished = true;
                if (type.SequenceEqual("IHDR"u8))
                {
                    if (header is not null || length != 13) throw InvalidPng();
                    var widthValue = BinaryPrimitives.ReadUInt32BigEndian(data);
                    var heightValue = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    if (widthValue > int.MaxValue || heightValue > int.MaxValue) throw InvalidPng();
                    width = (int)widthValue;
                    height = (int)heightValue;
                    if (width <= 0 || height <= 0 || data[10] != 0 || data[11] != 0 || data[12] > 1
                        || data[9] switch
                        {
                            0 => data[8] is not (1 or 2 or 4 or 8 or 16), 2 or 4 or 6 => data[8] is not (8 or 16),
                            3 => data[8] is not (1 or 2 or 4 or 8), _ => true,
                        }) throw InvalidPng();
                    header = [.. data];
                }
                else if (type.SequenceEqual("acTL"u8))
                {
                    if (declaredFrames != 0 || seenIdat || length != 8) throw InvalidPng();
                    declaredFrames = BinaryPrimitives.ReadUInt32BigEndian(data);
                    var totalPlays = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    if (declaredFrames is 0 or > MaximumFrames || totalPlays > int.MaxValue) throw InvalidPng();
                    plays = (int)totalPlays;
                }
                else if (type.SequenceEqual("fcTL"u8))
                {
                    if (declaredFrames == 0 || length != 26) throw InvalidPng();
                    CheckSequence(data);
                    FinishFrame();
                    var frameWidth = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    var frameHeight = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
                    var x = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
                    var y = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
                    if (frameWidth == 0 || frameHeight == 0 || (ulong)x + frameWidth > (uint)width ||
                        (ulong)y + frameHeight > (uint)height || data[24] > 2 || data[25] > 1 ||
                        (!seenIdat && (frameWidth != width || frameHeight != height || x != 0 || y != 0)))
                        throw InvalidPng();
                    var numerator = BinaryPrimitives.ReadUInt16BigEndian(data[20..]);
                    var denominator = BinaryPrimitives.ReadUInt16BigEndian(data[22..]);
                    if (denominator == 0) denominator = 100;
                    var ticks = Math.Max(1, (numerator * TimeSpan.TicksPerSecond + denominator / 2) / denominator);
                    var disposal = frames.Count == 0 && data[24] == 2 ? (byte)1 : data[24];
                    current = new(ticks, new((int)x, (int)y, (int)frameWidth, (int)frameHeight), disposal, data[25]);
                    currentUsesIdat = !seenIdat;
                }
                else if (type.SequenceEqual("IDAT"u8))
                {
                    if (idatFinished || (current is not null && frames.Count != 0) ||
                        (header![9] == 3 && paletteEntries == 0)) throw InvalidPng();
                    seenIdat = true;
                    idatBytes += length;
                    if (current is not null) chunks.Add(encoded.AsMemory(offset + 8, length));
                }
                else if (type.SequenceEqual("fdAT"u8))
                {
                    if (!seenIdat || current is null || currentUsesIdat || length < 4) throw InvalidPng();
                    CheckSequence(data);
                    chunks.Add(encoded.AsMemory(offset + 12, length - 4));
                }
                else if (type.SequenceEqual("IEND"u8))
                {
                    if (length != 0 || !seenIdat || idatBytes == 0 || offset + 12 != encoded.Length) throw InvalidPng();
                    FinishFrame();
                    ended = true;
                    break;
                }
                else
                {
                    if (type.SequenceEqual("PLTE"u8))
                    {
                        if (seenIdat || paletteEntries != 0 || seenTransparency || length is 0 or > 768 ||
                            length % 3 != 0
                            || header![9] is 0 or 4 ||
                            (header[9] == 3 && length / 3 > 1 << header[8])) throw InvalidPng();
                        paletteEntries = length / 3;
                    }

                    if (type.SequenceEqual("tRNS"u8))
                    {
                        if (seenIdat || seenTransparency || header![9] switch
                            {
                                0 => length != 2, 2 => length != 6, 3 => length <= 0 || length > paletteEntries,
                                _ => true,
                            }) throw InvalidPng();
                        seenTransparency = true;
                    }

                    if (type.SequenceEqual("cICP"u8))
                    {
                        if (seenColor || seenIdat || length != 4) throw InvalidPng();
                        seenColor = true;
                        hdr = data is [9, 16, 0, 1];
                        if (!hdr && data[1] is 16 or 18)
                            throw new NotSupportedException("The animation uses an unsupported HDR color encoding.");
                    }

                    if (type.SequenceEqual("mDCv"u8) || type.SequenceEqual("cLLi"u8))
                    {
                        if (seenIdat || length != (type.SequenceEqual("mDCv"u8) ? 24 : 8)) throw InvalidPng();
                        hdrAncillary = true;
                    }

                    if (!seenIdat) shared.Add(encoded.AsMemory(offset, length + 12));
                    if ((type[0] & 32) == 0 && !type.SequenceEqual("PLTE"u8)) throw InvalidPng();
                }

                offset = checked(offset + length + 12);
            }

            if (!ended || declaredFrames != frames.Count || (hdrAncillary && !hdr)) throw InvalidPng();
            if (frames.Count <= 1) return null;
            return new()
            {
                Width = width, Height = height, Extension = ".png", MimeType = "image/png", IsHdr = hdr,
                TotalPlays = plays, Frames = frames, PngHeader = header, SharedChunks = shared,
            };

            void CheckSequence(ReadOnlySpan<byte> data)
            {
                if (sequence > uint.MaxValue || BinaryPrimitives.ReadUInt32BigEndian(data) != sequence++)
                    throw InvalidPng();
            }

            void FinishFrame()
            {
                if (current is null) return;
                if (chunks.Count == 0 || chunks.All(chunk => chunk.Length == 0) || frames.Count >= MaximumFrames)
                    throw InvalidPng();
                frames.Add(current with { Data = [.. chunks] });
                chunks.Clear();
                current = null;
            }
        }

        private static InvalidDataException InvalidPng()
        {
            return new("Invalid animated PNG structure.");
        }

        private static uint PngCrc(ReadOnlySpan<byte> bytes, CancellationToken token)
        {
            return ~UpdatePngCrc(uint.MaxValue, bytes, token);
        }

        private static uint UpdatePngCrc(uint crc, ReadOnlySpan<byte> bytes, CancellationToken token)
        {
            while (!bytes.IsEmpty)
            {
                token.ThrowIfCancellationRequested();
                var take = Math.Min(bytes.Length, 16384);
                foreach (var value in bytes[..take]) crc = PngCrcTable[(byte)(crc ^ value)] ^ (crc >> 8);
                bytes = bytes[take..];
            }

            return crc;
        }

        private static uint[] CreatePngCrcTable()
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

        private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data,
            CancellationToken token)
        {
            Span<byte> prefix = stackalloc byte[8];
            BinaryPrimitives.WriteInt32BigEndian(prefix, data.Length);
            type.CopyTo(prefix[4..]);
            stream.Write(prefix);
            stream.Write(data);
            var crc = UpdatePngCrc(UpdatePngCrc(uint.MaxValue, type, token), data, token);
            Span<byte> suffix = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(suffix, ~crc);
            stream.Write(suffix);
        }
    }
}
