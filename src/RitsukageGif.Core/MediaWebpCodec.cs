using System.Buffers.Binary;
using ImageMagick;

namespace RitsukageGif.Core
{
    internal static class MediaWebpCodec
    {
        internal static void Coalesce(string path, MagickImageCollection images, int width, int height,
            CancellationToken token)
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            stream.ReadExactly(header[..12]);
            if (!header[..4].SequenceEqual("RIFF"u8) || !header.Slice(8, 4).SequenceEqual("WEBP"u8))
                throw new InvalidDataException();
            var end = checked(8L + BinaryPrimitives.ReadUInt32LittleEndian(header[4..]));
            if (end > stream.Length) throw new InvalidDataException();
            var index = 0;
            uint? iterations = null;
            MagickColor? background = null;
            while (stream.Position < end)
            {
                token.ThrowIfCancellationRequested();
                if (end - stream.Position < 8) throw new InvalidDataException();
                stream.ReadExactly(header[..8]);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
                var next = checked(stream.Position + length + (length & 1));
                if (next > end) throw new InvalidDataException();
                if (header[..4].SequenceEqual("ANIM"u8))
                {
                    if (length != 6 || iterations.HasValue) throw new InvalidDataException();
                    stream.ReadExactly(header[..6]);
                    background = new MagickColor(header[2], header[1], header[0], header[3]);
                    iterations = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
                }
                else if (header[..4].SequenceEqual("ANMF"u8))
                {
                    if (length < 16 || index >= images.Count) throw new InvalidDataException();
                    stream.ReadExactly(header);
                    var frame = images[index++];
                    var x = Read24(header) * 2;
                    var y = Read24(header[3..]) * 2;
                    var frameWidth = Read24(header[6..]) + 1;
                    var frameHeight = Read24(header[9..]) + 1;
                    if ((long)x + frameWidth > width || (long)y + frameHeight > height || (header[15] & 0xfc) != 0)
                        throw new InvalidDataException();
                    if (frameWidth != frame.Width || frameHeight != frame.Height)
                    {
                        // Magick expands a small first frame to the canvas, while later frames remain cropped.
                        if (frame.Width != width || frame.Height != height || frame.Page.X != 0 || frame.Page.Y != 0)
                            throw new InvalidDataException();
                        frame.Crop(new MagickGeometry(x, y, (uint)frameWidth, (uint)frameHeight));
                        frame.ResetPage();
                    }

                    frame.Page = new MagickGeometry(x, y, (uint)width, (uint)height);
                    // Magick exposes centiseconds and GIF-style composition; retain the WebP control fields.
                    frame.AnimationTicksPerSecond = 1000;
                    frame.AnimationDelay = (uint)Math.Max(1, Read24(header[12..]));
                    frame.Compose = (header[15] & 2) != 0 ? CompositeOperator.Src : CompositeOperator.Over;
                    frame.GifDisposeMethod =
                        (header[15] & 1) != 0 ? GifDisposeMethod.Background : GifDisposeMethod.None;
                }

                stream.Position = next;
            }

            if (index == 0 && images.Count == 1) return;
            if (index != images.Count || !iterations.HasValue) throw new InvalidDataException();
            images[0].AnimationIterations = iterations.Value;
            MediaPngCodec.Coalesce(images, token, background);
        }

        private static int Read24(ReadOnlySpan<byte> value) => value[0] | (value[1] << 8) | (value[2] << 16);
    }
}
