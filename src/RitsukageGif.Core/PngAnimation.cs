using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace RitsukageGif.Core
{
    public static class PngAnimation
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

        public static void Encode(IReadOnlyList<StoredFrame> frames, string output, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var destination = File.Create(output);
            destination.Write(Signature);
            uint sequence = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                using var image = new MagickImage();
                using var png = new MemoryStream(EncodeFrame(image, frames[i].Path, token));
                png.Position = 8;
                var chunks = ReadChunks(png).ToArray();
                if (i == 0)
                {
                    WriteChunk(destination, "IHDR", chunks[0].Data);
                    var animation = new byte[8];
                    Put(animation, 0, (uint)frames.Count);
                    WriteChunk(destination, "acTL", animation);
                }

                var control = new byte[26];
                Put(control, 0, sequence++);
                Put(control, 4, image.Width);
                Put(control, 8, image.Height);
                // Millisecond precision for normal recordings, second precision for long holds.
                var divisor = frames[i].DelayMs <= ushort.MaxValue ? 1000 : 1;
                var delay = divisor == 1000 ? frames[i].DelayMs : (int)Math.Round(frames[i].DelayMs / 1000.0);
                BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(20), checked((ushort)delay));
                BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(22), (ushort)divisor);
                WriteChunk(destination, "fcTL", control);
                foreach (var chunk in chunks.Where(c => c.Type == "IDAT"))
                {
                    token.ThrowIfCancellationRequested();
                    if (i == 0)
                    {
                        WriteChunk(destination, "IDAT", chunk.Data);
                    }
                    else
                    {
                        var data = new byte[chunk.Data.Length + 4];
                        Put(data, 0, sequence++);
                        chunk.Data.CopyTo(data, 4);
                        WriteChunk(destination, "fdAT", data);
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            WriteChunk(destination, "IEND", []);
        }

        private static byte[] EncodeFrame(MagickImage image, string path, CancellationToken token)
        {
            image.Progress += (_, value) => value.Cancel = token.IsCancellationRequested;
            try
            {
                image.Read(path);
                // Reading replaces the native image and its progress monitor.
                image.Progress += (_, value) => value.Cancel = token.IsCancellationRequested;
                token.ThrowIfCancellationRequested();
                image.ColorType = ColorType.TrueColorAlpha;
                var data = image.ToByteArray(MagickFormat.Png32);
                token.ThrowIfCancellationRequested();
                return data;
            }
            catch (Exception error) when (token.IsCancellationRequested &&
                                          error is MagickException or ObjectDisposedException)
            {
                throw new OperationCanceledException(token);
            }
        }

        public static MagickImageCollection Read(string path, uint limit = uint.MaxValue)
        {
            using var source = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[8];
            source.ReadExactly(signature);
            if (!signature.SequenceEqual(Signature))
                return new MagickImageCollection(path, new MagickReadSettings { FrameCount = limit });
            var result = new MagickImageCollection();
            try
            {
                foreach (var frame in ReadFrames(path))
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

        public static IEnumerable<MagickImage> ReadFrames(string path)
        {
            using var source = File.OpenRead(path);
            source.Position = 8;
            var header = Array.Empty<byte>();
            var shared = new List<(string Type, byte[] Data)>();
            byte[]? control = null;
            using var compressed = new MemoryStream();
            foreach (var chunk in ReadChunks(source))
                switch (chunk.Type)
                {
                    case "IHDR": header = chunk.Data; break;
                    case "PLTE":
                    case "tRNS": shared.Add(chunk); break;
                    case "fcTL":
                        if (FinishFrame() is { } previous) yield return previous;
                        control = chunk.Data;
                        break;
                    case "IDAT": compressed.Write(chunk.Data); break;
                    case "fdAT": compressed.Write(chunk.Data.AsSpan(4)); break;
                    case "IEND":
                        if (FinishFrame() is { } last) yield return last;
                        yield break;
                }

            MagickImage? FinishFrame()
            {
                if (compressed.Length == 0) return null;
                using var png = new MemoryStream();
                png.Write(Signature);
                var frameHeader = (byte[])header.Clone();
                if (control is not null) control.AsSpan(4, 8).CopyTo(frameHeader);
                WriteChunk(png, "IHDR", frameHeader);
                foreach (var chunk in shared) WriteChunk(png, chunk.Type, chunk.Data);
                WriteChunk(png, "IDAT", compressed.ToArray());
                WriteChunk(png, "IEND", []);
                var frame = new MagickImage(png.ToArray());
                if (control is not null)
                {
                    frame.Page = new MagickGeometry(checked((int)Get(control, 12)), checked((int)Get(control, 16)),
                        Get(header, 0), Get(header, 4));
                    var denominator = BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(22));
                    frame.AnimationTicksPerSecond = denominator == 0 ? 100 : denominator;
                    frame.AnimationDelay = BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(20));
                    frame.GifDisposeMethod = control[24] switch
                    {
                        1 => GifDisposeMethod.Background, 2 => GifDisposeMethod.Previous, _ => GifDisposeMethod.None
                    };
                    frame.Compose = control[25] == 0 ? CompositeOperator.Src : CompositeOperator.Over;
                }

                compressed.SetLength(0);
                return frame;
            }
        }

        private static IEnumerable<(string Type, byte[] Data)> ReadChunks(Stream stream)
        {
            var prefix = new byte[8];
            while (stream.Position < stream.Length)
            {
                stream.ReadExactly(prefix);
                var length = checked((int)Get(prefix, 0));
                if (length > stream.Length - stream.Position - 4) throw new InvalidDataException();
                var data = new byte[length];
                stream.ReadExactly(data);
                var crc = new byte[4];
                stream.ReadExactly(crc);
                yield return (Encoding.ASCII.GetString(prefix, 4, 4), data);
            }
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var prefix = new byte[8];
            Put(prefix, 0, (uint)data.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(prefix, 4);
            stream.Write(prefix);
            stream.Write(data);
            var crc = 0xffffffff;
            foreach (var b in prefix.AsSpan(4)) crc = UpdateCrc(crc, b);
            foreach (var b in data) crc = UpdateCrc(crc, b);
            var suffix = new byte[4];
            Put(suffix, 0, ~crc);
            stream.Write(suffix);
        }

        private static uint UpdateCrc(uint crc, byte b)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            return crc;
        }

        private static uint Get(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
        }

        private static void Put(byte[] data, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
        }
    }
}
