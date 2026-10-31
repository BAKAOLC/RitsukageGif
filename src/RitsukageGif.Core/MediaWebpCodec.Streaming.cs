using System.Buffers.Binary;
using ImageMagick;

namespace RitsukageGif.Core
{
    internal static partial class MediaWebpCodec
    {
        internal static IEnumerable<IMagickImage<byte>> ReadCoalescedFrames(string path, MediaInfo info,
            CancellationToken token)
        {
            using var stream = File.OpenRead(path);
            var header = new byte[16];
            stream.ReadExactly(header.AsSpan(0, 12));
            if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
                throw new InvalidDataException();
            var end = checked(8L + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)));
            if (end > stream.Length) throw new InvalidDataException();
            MagickColor? background = null;
            uint? iterations = null;
            MagickImage? canvas = null;
            var index = 0;
            try
            {
                while (stream.Position < end)
                {
                    token.ThrowIfCancellationRequested();
                    if (end - stream.Position < 8) throw new InvalidDataException();
                    stream.ReadExactly(header.AsSpan(0, 8));
                    var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                    var next = checked(stream.Position + length + (length & 1));
                    if (next > end) throw new InvalidDataException();
                    if (header.AsSpan(0, 4).SequenceEqual("ANIM"u8))
                    {
                        if (length != 6 || iterations.HasValue) throw new InvalidDataException();
                        stream.ReadExactly(header.AsSpan(0, 6));
                        background = new(header[2], header[1], header[0], header[3]);
                        iterations = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
                        canvas = new(background, (uint)info.Width, (uint)info.Height);
                        canvas.SetArtifact("compose:outside-overlay", "false");
                    }
                    else if (header.AsSpan(0, 4).SequenceEqual("ANMF"u8))
                    {
                        if (length < 16 || canvas is null || index >= info.Frames)
                            throw new InvalidDataException();
                        stream.ReadExactly(header);
                        var x = Read24(header) * 2;
                        var y = Read24(header.AsSpan(3)) * 2;
                        var width = Read24(header.AsSpan(6)) + 1;
                        var height = Read24(header.AsSpan(9)) + 1;
                        var flags = header[15];
                        var delay = (uint)Math.Max(1, Read24(header.AsSpan(12)));
                        if ((long)x + width > info.Width || (long)y + height > info.Height || (flags & 0xfc) != 0)
                            throw new InvalidDataException();
                        var bytes = new byte[checked((int)length + 14)];
                        "RIFF"u8.CopyTo(bytes);
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)bytes.Length - 8));
                        "WEBPVP8X"u8.CopyTo(bytes.AsSpan(8));
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 10);
                        bytes[20] = 0x10;
                        Write24(bytes.AsSpan(24), width - 1);
                        Write24(bytes.AsSpan(27), height - 1);
                        stream.ReadExactly(bytes.AsSpan(30));
                        using var frame = new MagickImage(bytes);
                        if (frame.Width != width || frame.Height != height) throw new InvalidDataException();
                        canvas.Composite(frame, x, y,
                            (flags & 2) != 0 ? CompositeOperator.Src : CompositeOperator.Over);
                        using var complete = MagickImageCloner.Clone(canvas, token);
                        complete.AnimationDelay = delay;
                        complete.AnimationTicksPerSecond = 1000;
                        complete.AnimationIterations = iterations!.Value;
                        complete.GifDisposeMethod = GifDisposeMethod.Background;
                        complete.ResetPage();
                        index++;
                        yield return complete;
                        if ((flags & 1) != 0)
                        {
                            using var empty = new MagickImage(background!, (uint)width, (uint)height);
                            canvas.Composite(empty, x, y, CompositeOperator.Src);
                        }
                    }

                    stream.Position = next;
                }

                if (index != info.Frames) throw new InvalidDataException();
            }
            finally
            {
                canvas?.Dispose();
            }
        }

        private static void Write24(Span<byte> target, int value)
        {
            target[0] = (byte)value;
            target[1] = (byte)(value >> 8);
            target[2] = (byte)(value >> 16);
        }
    }
}
