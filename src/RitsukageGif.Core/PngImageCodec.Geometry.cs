using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    public static partial class PngImageCodec
    {
        public static (int Width, int Height) ReadDimensions(byte[] png)
        {
            ArgumentNullException.ThrowIfNull(png);
            if (png.Length < 33 || png.Length > MaximumEncodedBytes ||
                !png.AsSpan(0, 8).SequenceEqual(Signature) ||
                BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8)) != 13 ||
                !png.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
                ComputeTransformPngCrc(png.AsSpan(12, 17)) != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(29)))
                throw new InvalidDataException();
            var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
            var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
            if (width <= 0 || height <= 0 || (long)width * height > 128L * 1024 * 1024)
                throw new InvalidDataException();
            return (width, height);
        }

        public static byte[] Crop(byte[] png, PixelRect region, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(png);
            token.ThrowIfCancellationRequested();
            var dimensions = ReadDimensions(png);
            if (region.IsEmpty || region.X < 0 || region.Y < 0 ||
                (long)region.X + region.Width > dimensions.Width ||
                (long)region.Y + region.Height > dimensions.Height)
                throw new ArgumentOutOfRangeException(nameof(region));
            if (region == new PixelRect(0, 0, dimensions.Width, dimensions.Height))
            {
                ValidateEncodedChunks(png, token);
                return png;
            }

            var raster = Decode(png, token);
            var channels = raster.ColorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
            var pixelBytes = Math.Max(1, channels * raster.Depth / 8);
            var rowBytes = checked(region.Width * pixelBytes);
            var pixels = new byte[checked(rowBytes * region.Height)];
            for (var y = 0; y < region.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                raster.Samples.AsSpan(((y + region.Y) * raster.Width + region.X) * pixelBytes, rowBytes)
                    .CopyTo(pixels.AsSpan(y * rowBytes));
            }

            return Encode(raster with { Width = region.Width, Height = region.Height, Samples = pixels }, token);
        }

        public static void ValidateEncoded(byte[] png, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            _ = ReadDimensions(png);
            ValidateEncodedChunks(png, token);
        }

        private static void ValidateEncodedChunks(byte[] png, CancellationToken token)
        {
            var hasData = false;
            var dataEnded = false;
            for (var offset = 8; offset <= png.Length - 12;)
            {
                token.ThrowIfCancellationRequested();
                var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
                if (length > png.Length - offset - 12) throw new InvalidDataException();
                var size = (int)length;
                var type = png.AsSpan(offset + 4, 4);
                if (ComputeTransformPngCrc(png.AsSpan(offset + 4, size + 4), token: token) !=
                    BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + size)))
                    throw new InvalidDataException();
                if (type.SequenceEqual("IDAT"u8))
                {
                    if (dataEnded) throw new InvalidDataException();
                    hasData |= size != 0;
                }
                else
                {
                    if (hasData) dataEnded = true;
                    if (type.SequenceEqual("IEND"u8))
                    {
                        if (!hasData || size != 0) throw new InvalidDataException();
                        return;
                    }

                    if ((type.SequenceEqual("IHDR"u8) && offset != 8) ||
                        (type[0] is >= (byte)'A' and <= (byte)'Z' &&
                         !type.SequenceEqual("IHDR"u8) && !type.SequenceEqual("PLTE"u8)))
                        throw new InvalidDataException();
                }

                offset += size + 12;
            }

            throw new InvalidDataException();
        }
    }
}
