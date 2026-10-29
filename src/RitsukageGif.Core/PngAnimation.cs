using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace RitsukageGif.Core
{
    public static partial class PngAnimation
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

        public static MagickImageCollection Read(string path, uint limit = uint.MaxValue,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            using var source = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[8];
            source.ReadExactly(signature);
            var result = new MagickImageCollection();
            try
            {
                if (!signature.SequenceEqual(Signature))
                {
                    result.ReadAsync(path, new MagickReadSettings { FrameCount = limit }, token)
                        .GetAwaiter().GetResult();
                    token.ThrowIfCancellationRequested();
                    return result;
                }

                foreach (var frame in ReadFrames(path, token))
                {
                    result.Add(frame);
                    if (result.Count >= limit) break;
                }

                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        public static IEnumerable<MagickImage> ReadFrames(string path, CancellationToken token = default)
        {
            using var source = File.OpenRead(path);
            source.Position = 8;
            var header = Array.Empty<byte>();
            var shared = new List<(string Type, byte[] Data)>();
            byte[]? control = null;
            using var compressed = new MemoryStream();
            foreach (var chunk in ReadFrameChunks(source, token))
                switch (chunk.Type)
                {
                    case "IHDR": header = chunk.Data.ToArray(); break;
                    case "PLTE":
                    case "tRNS":
                        if (shared.Any(existing => existing.Type == chunk.Type)) throw new InvalidDataException();
                        shared.Add((chunk.Type, chunk.Data.ToArray()));
                        break;
                    case "fcTL":
                        if (FinishFrame() is { } previous) yield return previous;
                        control = chunk.Data.ToArray();
                        break;
                    case "IDAT":
                        if (compressed.Length + chunk.Data.Length > 256L * 1024 * 1024)
                            throw new InvalidOperationException(UiText.Get("MemoryLimit"));
                        compressed.Write(chunk.Data.Span);
                        break;
                    case "IEND":
                        if (FinishFrame() is { } last) yield return last;
                        yield break;
                }

            yield break;

            MagickImage? FinishFrame()
            {
                if (compressed.Length == 0) return null;
                using var png = new MemoryStream(checked((int)compressed.Length + 57 +
                                                         shared.Sum(chunk => chunk.Data.Length + 12)));
                png.Write(Signature);
                var frameHeader = (byte[])header.Clone();
                control?.AsSpan(4, 8).CopyTo(frameHeader);
                WriteChunk(png, "IHDR", frameHeader);
                foreach (var chunk in shared) WriteChunk(png, chunk.Type, chunk.Data);
                WriteChunk(png, "IDAT", compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
                WriteChunk(png, "IEND", []);
                token.ThrowIfCancellationRequested();
                var frame = new MagickImage();
                try
                {
                    frame.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                    png.Position = 0;
                    frame.Read(png);
                    token.ThrowIfCancellationRequested();
                }
                catch
                {
                    frame.Dispose();
                    throw;
                }

                if (control is not null)
                {
                    frame.Page = new MagickGeometry(checked((int)Get(control, 12)), checked((int)Get(control, 16)),
                        Get(header, 0), Get(header, 4));
                    var denominator = BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(22));
                    frame.AnimationTicksPerSecond = denominator == 0 ? 100 : denominator;
                    frame.AnimationDelay = BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(20));
                    frame.GifDisposeMethod = control[24] switch
                    {
                        1 => GifDisposeMethod.Background, 2 => GifDisposeMethod.Previous, _ => GifDisposeMethod.None,
                    };
                    frame.Compose = control[25] == 0 ? CompositeOperator.Src : CompositeOperator.Over;
                }

                compressed.SetLength(0);
                return frame;
            }
        }

        private static IEnumerable<(string Type, ReadOnlyMemory<byte> Data)> ReadFrameChunks(Stream stream,
            CancellationToken token)
        {
            var prefix = new byte[8];
            var buffer = new byte[32768];
            while (stream.Position < stream.Length)
            {
                token.ThrowIfCancellationRequested();
                stream.ReadExactly(prefix);
                var length = checked((int)Get(prefix, 0));
                if (length > stream.Length - stream.Position - 4) throw new InvalidDataException();
                var type = Encoding.ASCII.GetString(prefix, 4, 4);
                if (type is "IDAT" or "fdAT")
                {
                    if (type == "fdAT")
                    {
                        if (length < 4) throw new InvalidDataException();
                        stream.Position += 4;
                        length -= 4;
                    }

                    while (length > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        var take = Math.Min(length, buffer.Length);
                        stream.ReadExactly(buffer.AsSpan(0, take));
                        yield return ("IDAT", buffer.AsMemory(0, take));
                        length -= take;
                    }

                    stream.Position += 4;
                    continue;
                }

                var maximum = type switch
                {
                    "IHDR" => 13, "fcTL" => 26, "PLTE" => 768, "tRNS" => 256, "IEND" => 0, _ => -1,
                };
                if (maximum < 0)
                {
                    stream.Position += length + 4;
                    continue;
                }

                if (length > maximum || (type is "IHDR" or "fcTL" && length != maximum))
                    throw new InvalidDataException();
                stream.ReadExactly(buffer.AsSpan(0, length));
                stream.Position += 4;
                yield return (type, buffer.AsMemory(0, length));
                if (type == "IEND") yield break;
            }
        }

        private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> prefix = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, prefix[4..]);
            stream.Write(prefix);
            stream.Write(data);
            var crc = PngCrc32.Update(uint.MaxValue, prefix[4..]);
            crc = PngCrc32.Update(crc, data);
            Span<byte> suffix = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(suffix, ~crc);
            stream.Write(suffix);
        }

        private static uint Get(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
        }
    }
}
