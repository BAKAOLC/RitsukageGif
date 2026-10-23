using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace RitsukageGif.Core
{
    internal static class MediaPngCodec
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

        internal static void Coalesce(MagickImageCollection images, CancellationToken token)
        {
            var width = images.Max(image => Math.Max(image.Width, image.Page.Width));
            var height = images.Max(image => Math.Max(image.Height, image.Page.Height));
            using var canvas = new MagickImage(MagickColors.Transparent, width, height);
            canvas.SetArtifact("compose:outside-overlay", "false");
            using var complete = new MagickImageCollection();
            foreach (var frame in images)
            {
                token.ThrowIfCancellationRequested();
                using var previous = frame.GifDisposeMethod == GifDisposeMethod.Previous ? canvas.Clone() : null;
                // APNG SOURCE replaces transparent pixels too; GIF coalescing always blends them over.
                canvas.Composite(frame, frame.Page.X, frame.Page.Y, frame.Compose);
                var image = canvas.Clone();
                image.AnimationDelay = frame.AnimationDelay;
                image.AnimationTicksPerSecond = frame.AnimationTicksPerSecond;
                image.AnimationIterations = images[0].AnimationIterations;
                image.GifDisposeMethod = GifDisposeMethod.Background;
                complete.Add(image);
                if (previous is not null) canvas.Composite(previous, CompositeOperator.Src);
                else if (frame.GifDisposeMethod == GifDisposeMethod.Background)
                {
                    using var empty = new MagickImage(MagickColors.Transparent, frame.Width, frame.Height);
                    canvas.Composite(empty, frame.Page.X, frame.Page.Y, CompositeOperator.Src);
                }
            }

            foreach (var image in images) image.Dispose();
            images.Clear();
            foreach (var image in complete) images.Add(image.Clone());
        }

        internal static MagickImageCollection ReadImages(string path)
        {
            using var stream = File.OpenRead(path);
            stream.Position = 8;
            var animated = false;
            var poster = false;
            foreach (var (type, _) in ReadChunks(stream, true))
            {
                if (type == "acTL") animated = true;
                if (type == "fcTL") break;
                if (type != "IDAT") continue;
                poster = animated;
                break;
            }

            var images = PngAnimation.Read(path);
            if (poster)
            {
                images[0].Dispose();
                images.RemoveAt(0);
            }

            return images;
        }

        internal static uint ReadIterations(string path)
        {
            using var stream = File.OpenRead(path);
            stream.Position = 8;
            foreach (var (type, data) in ReadChunks(stream, true))
                if (type == "acTL")
                    return Read(data, 4);
            return 0;
        }

        internal static MediaInfo? Inspect(string path, CancellationToken token)
        {
            using var stream = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[8];
            if (stream.Read(signature) != 8 || !signature.SequenceEqual(Signature)) return null;
            var width = 0;
            var height = 0;
            var frames = 0;
            var duration = 0.0;
            var animated = false;
            uint declaredFrames = 0;
            var ended = false;
            foreach (var (type, data) in ReadChunks(stream, true))
            {
                token.ThrowIfCancellationRequested();
                if (width == 0 && type != "IHDR") throw new InvalidDataException();
                switch (type)
                {
                    case "IHDR":
                        if (data.Length != 13 || width != 0 || height != 0) throw new InvalidDataException();
                        width = checked((int)Read(data, 0));
                        height = checked((int)Read(data, 4));
                        if (width <= 0 || height <= 0) throw new InvalidDataException();
                        break;
                    case "acTL":
                        if (data.Length != 8 || animated || width == 0) throw new InvalidDataException();
                        animated = true;
                        declaredFrames = Read(data, 0);
                        if (declaredFrames == 0) throw new InvalidDataException();
                        break;
                    case "fcTL":
                        if (!animated || data.Length != 26 || Read(data, 4) == 0 || Read(data, 8) == 0 ||
                            (ulong)Read(data, 4) + Read(data, 12) > (ulong)width ||
                            (ulong)Read(data, 8) + Read(data, 16) > (ulong)height || data[24] > 2 || data[25] > 1)
                            throw new InvalidDataException();
                        frames++;
                        var denominator = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(22));
                        duration += BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(20)) /
                                    (double)(denominator == 0 ? 100 : denominator);
                        break;
                    case "IEND": ended = true; break;
                }

                if (ended) break;
            }

            if (!ended || width == 0 || height == 0 || (animated && frames != declaredFrames))
                throw new InvalidDataException();
            return new MediaInfo(path, stream.Length, width, height, Math.Max(1, frames), duration,
                animated ? "apng" : "png");
        }

        internal static void Write(MagickImageCollection images, string path, CancellationToken token)
        {
            using var stream = File.Create(path);
            stream.Write(Signature);
            uint sequence = 0;
            for (var index = 0; index < images.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var image = images[index];
                using var png = new MemoryStream(image.ToByteArray(MagickFormat.Png32));
                png.Position = 8;
                foreach (var (type, data) in ReadChunks(png, false))
                    if (type == "IHDR")
                    {
                        if (index == 0)
                        {
                            WriteChunk(stream, type, data);
                            var animation = new byte[8];
                            Put(animation, 0, (uint)images.Count);
                            Put(animation, 4, image.AnimationIterations);
                            WriteChunk(stream, "acTL", animation);
                        }

                        var control = new byte[26];
                        Put(control, 0, sequence++);
                        Put(control, 4, image.Width);
                        Put(control, 8, image.Height);
                        var numerator = image.AnimationDelay;
                        var denominator =
                            (uint)(image.AnimationTicksPerSecond == 0 ? 100 : image.AnimationTicksPerSecond);
                        var divisor = Gcd(numerator, denominator);
                        numerator /= divisor;
                        denominator /= divisor;
                        if (numerator > ushort.MaxValue || denominator > ushort.MaxValue)
                            throw new InvalidDataException();
                        BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(20), (ushort)numerator);
                        BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(22), (ushort)denominator);
                        WriteChunk(stream, "fcTL", control);
                    }
                    else if (type == "IDAT")
                    {
                        if (index == 0) WriteChunk(stream, type, data);
                        else
                        {
                            var frameData = new byte[data.Length + 4];
                            Put(frameData, 0, sequence++);
                            data.CopyTo(frameData, 4);
                            WriteChunk(stream, "fdAT", frameData);
                        }
                    }
            }

            WriteChunk(stream, "IEND", []);
        }

        private static IEnumerable<(string Type, byte[] Data)> ReadChunks(Stream stream, bool metadataOnly)
        {
            var prefix = new byte[8];
            while (stream.Position < stream.Length)
            {
                stream.ReadExactly(prefix);
                var length = checked((int)Read(prefix, 0));
                if (length > stream.Length - stream.Position - 4) throw new InvalidDataException();
                var type = Encoding.ASCII.GetString(prefix, 4, 4);
                if (type == "IEND" && length != 0) throw new InvalidDataException();
                if (metadataOnly && type is not ("IHDR" or "fcTL" or "acTL"))
                {
                    stream.Position += length + 4;
                    yield return (type, []);
                    continue;
                }

                if (metadataOnly && length > 26) throw new InvalidDataException();
                var data = new byte[length];
                stream.ReadExactly(data);
                stream.Position += 4;
                yield return (type, data);
            }
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var prefix = new byte[8];
            Put(prefix, 0, (uint)data.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(prefix, 4);
            stream.Write(prefix);
            stream.Write(data);
            var crc = uint.MaxValue;
            foreach (var value in prefix.AsSpan(4)) crc = UpdateCrc(crc, value);
            foreach (var value in data) crc = UpdateCrc(crc, value);
            Span<byte> suffix = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(suffix, ~crc);
            stream.Write(suffix);
        }

        private static uint UpdateCrc(uint crc, byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            return crc;
        }

        private static uint Gcd(uint left, uint right)
        {
            while (right != 0) (left, right) = (right, left % right);
            return left;
        }

        private static uint Read(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
        }

        private static void Put(byte[] data, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
        }
    }
}
