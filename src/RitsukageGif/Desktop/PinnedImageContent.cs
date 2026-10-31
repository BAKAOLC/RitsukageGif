using System.Buffers.Binary;
using System.Security.Cryptography;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial record PinnedImageContent(
        byte[] Png,
        int Width,
        int Height,
        byte[] Pixels,
        byte[] ThumbnailPng)
    {
        private const long MaximumPixels = 128L * 1024 * 1024;
        public const int MaximumEncodedBytes = 256 * 1024 * 1024;
        public string ContentHash { get; private init; } = Hash(Png);
        internal byte[]? OriginalEncoded { get; init; }
        internal AnimationPinMetadata? Animation { get; init; }
        internal bool AnimationActive { get; init; } = true;

        private static string Hash(ReadOnlySpan<byte> png)
        {
            return Convert.ToHexString(SHA256.HashData(png));
        }

        public static PinnedImageContent Decode(ReadOnlyMemory<byte> encoded, CancellationToken token = default)
        {
            if (encoded.IsEmpty || encoded.Length > MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(encoded));
            token.ThrowIfCancellationRequested();
            byte[]? animationEncoded = null;
            AnimationPinMetadata? animation = null;
            if (MayAnimate(encoded.Span))
            {
                animationEncoded = encoded.ToArray();
                animation = AnimationPinMetadata.Read(animationEncoded, token);
                if (animation is not null)
                    AnimationPinDecoder.ValidateGeometry(animation, animationEncoded.LongLength);
            }

            try
            {
                if (TryDecodeHdr(encoded, token) is { } hdr)
                    return AttachAnimation(hdr, animationEncoded, animation, token);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }

            using var image = new MagickImage();
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var settings = new MagickReadSettings { FrameCount = 1 };
            try
            {
                image.Ping(encoded.Span, settings);
                ValidateSize(image);
                token.ThrowIfCancellationRequested();
                image.Read(encoded.Span, settings);
                var preservePng = image is
                    { Format: MagickFormat.Png, Orientation: Orientation.Undefined or Orientation.TopLeft };
                image.AutoOrient();
                ValidateSize(image);
                image.Depth = 8;
                var hasColorProfile = image.GetColorProfile() is not null;
                if (hasColorProfile && !image.TransformColorSpace(ColorProfiles.SRGB))
                    throw new InvalidDataException();
                image.ColorSpace = ColorSpace.sRGB;
                var width = checked((int)image.Width);
                var height = checked((int)image.Height);
                var png = preservePng ? encoded.ToArray() : image.ToByteArray(MagickFormat.Png);
                var sdrPng = preservePng && hasColorProfile ? image.ToByteArray(MagickFormat.Png) : null;
                var pixels = image.ToByteArray(MagickFormat.Bgra);
                var scale = Math.Min(1, 2048d / Math.Max(width, height));
                if (scale < 1)
                    image.Resize(new MagickGeometry((uint)Math.Max(1, Math.Round(width * scale)),
                        (uint)Math.Max(1, Math.Round(height * scale))) { IgnoreAspectRatio = true });
                var thumbnail = scale < 1 ? image.ToByteArray(MagickFormat.Png) : sdrPng ?? png;
                token.ThrowIfCancellationRequested();
                return AttachAnimation(new(png, width, height, pixels, thumbnail) { SdrPng = sdrPng }, animationEncoded,
                    animation, token);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }

        private static bool MayAnimate(ReadOnlySpan<byte> encoded)
        {
            if (encoded.StartsWith("GIF87a"u8) || encoded.StartsWith("GIF89a"u8)) return true;
            if (encoded.Length >= 12 && encoded[..4].SequenceEqual("RIFF"u8) &&
                encoded.Slice(8, 4).SequenceEqual("WEBP"u8)) return true;
            if (!encoded.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
            for (var offset = 8; offset <= encoded.Length - 12;)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(encoded[offset..]);
                if (length > encoded.Length - offset - 12) return false;
                var type = encoded.Slice(offset + 4, 4);
                if (type.SequenceEqual("acTL"u8)) return true;
                if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8)) return false;
                offset += checked((int)length + 12);
            }

            return false;
        }

        private static PinnedImageContent AttachAnimation(PinnedImageContent content, byte[]? owned,
            AnimationPinMetadata? animation, CancellationToken token)
        {
            if (animation is null) return content;
            var png = content.Png;
            if (animation.Extension == ".png")
                png = PngImageCodec.Encode(PngImageCodec.Decode(png, token), token);
            return content with
            {
                Png = png, OriginalEncoded = owned, Animation = animation, ContentHash = Hash(owned!),
            };
        }

        private static void ValidateSize(MagickImage image)
        {
            if (image.Width == 0 || image.Height == 0 || (long)image.Width * image.Height > MaximumPixels)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
        }
    }
}
