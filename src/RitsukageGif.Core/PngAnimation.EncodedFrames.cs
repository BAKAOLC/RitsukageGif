using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    public static partial class PngAnimation
    {
        public static bool HasHdrPqMetadata(string path, CancellationToken token = default)
        {
            using var source = File.OpenRead(path);
            Span<byte> prefix = stackalloc byte[8];
            Span<byte> color = stackalloc byte[4];
            if (source.Read(prefix) != 8 || !prefix.SequenceEqual(Signature)) return false;
            while (source.Position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                source.ReadExactly(prefix);
                var length = BinaryPrimitives.ReadUInt32BigEndian(prefix);
                if (length > source.Length - source.Position - 4) throw new InvalidDataException();
                if (prefix[4..].SequenceEqual("cICP"u8))
                {
                    if (length != 4) throw new InvalidDataException();
                    source.ReadExactly(color);
                    return color.SequenceEqual(new byte[] { 9, 16, 0, 1 });
                }

                if (prefix[4..].SequenceEqual("IDAT"u8) || prefix[4..].SequenceEqual("IEND"u8)) return false;
                source.Position += length + 4;
            }

            return false;
        }

        // The original high-depth PNG payload is preserved; no Q8 image decoding occurs here.
        public static IEnumerable<EncodedPngFrame> ReadEncodedFrames(string path, CancellationToken token = default)
        {
            using var source = File.OpenRead(path);
            source.Position = 8;
            byte[] header = [];
            byte[]? control = null;
            var shared = new List<(string Type, byte[] Data)>();
            using var compressed = new MemoryStream();
            foreach (var chunk in ReadFrameChunks(source, token))
                switch (chunk.Type)
                {
                    case "IHDR": header = chunk.Data.ToArray(); break;
                    case "PLTE":
                    case "tRNS":
                        if (shared.Any(item => item.Type == chunk.Type)) throw new InvalidDataException();
                        shared.Add((chunk.Type, chunk.Data.ToArray()));
                        break;
                    case "fcTL":
                        if (Finish(control is null) is { } previous) yield return previous;
                        control = chunk.Data.ToArray();
                        break;
                    case "IDAT":
                        if (compressed.Length + chunk.Data.Length > 256L * 1024 * 1024)
                            throw new InvalidOperationException(UiText.Get("MemoryLimit"));
                        compressed.Write(chunk.Data.Span);
                        break;
                    case "IEND":
                        if (Finish(false) is { } last) yield return last;
                        yield break;
                }

            yield break;

            EncodedPngFrame? Finish(bool poster)
            {
                if (compressed.Length == 0) return null;
                if (header.Length != 13) throw new InvalidDataException();
                var width = checked((int)Get(header, 0));
                var height = checked((int)Get(header, 4));
                var region = control is null
                    ? new PixelRect(0, 0, width, height)
                    : new PixelRect(checked((int)Get(control, 12)), checked((int)Get(control, 16)),
                        checked((int)Get(control, 4)), checked((int)Get(control, 8)));
                if (width <= 0 || height <= 0 || region.IsEmpty || region.Right > width || region.Bottom > height ||
                    (control is not null && (control[24] > 2 || control[25] > 1))) throw new InvalidDataException();
                using var png =
                    new MemoryStream(checked((int)compressed.Length + 57 + shared.Sum(item => item.Data.Length + 12)));
                png.Write(Signature);
                var frameHeader = (byte[])header.Clone();
                control?.AsSpan(4, 8).CopyTo(frameHeader);
                WriteChunk(png, "IHDR", frameHeader);
                foreach (var item in shared) WriteChunk(png, item.Type, item.Data);
                WriteChunk(png, "IDAT", compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
                WriteChunk(png, "IEND", []);
                var denominator = control is null ? 1000 : BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(22));
                if (denominator == 0) denominator = 100;
                var delay = control is null
                    ? 100
                    : (int)(BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(20)) * 1000L / denominator);
                compressed.SetLength(0);
                return new(png.ToArray(), width, height, region, Math.Max(10, delay), control?[24] ?? 0,
                    control?[25] ?? 0, poster);
            }
        }
    }
}
