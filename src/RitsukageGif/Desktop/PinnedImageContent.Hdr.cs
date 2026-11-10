using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial record PinnedImageContent
    {
        public CapturedFrame? Hdr { get; private init; }
        public byte[]? SdrPng { get; private init; }

        public static PinnedImageContent FromHdr(byte[] png, PinnedImageContent sdr, CapturedFrame hdr)
        {
            HdrAnnotationBlender.Validate(hdr);
            if (sdr.Width != hdr.Width || sdr.Height != hdr.Height)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(hdr));
            var pixels = new byte[checked(hdr.Width * hdr.Height * 8)];
            for (var y = 0; y < hdr.Height; y++)
                hdr.Pixels.Span.Slice(y * hdr.Stride, hdr.Width * 8).CopyTo(pixels.AsSpan(y * hdr.Width * 8));
            return sdr with
            {
                Png = png, SdrPng = sdr.SdrPng ?? sdr.Png,
                ContentHash = Hash(png),
                Hdr = hdr with { Pixels = pixels, Stride = hdr.Width * 8 },
            };
        }

        private static PinnedImageContent? TryDecodeHdr(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            var hdr = HdrImageDecoder.TryDecode(encoded, token: token);
            if (hdr is null) return null;
            var width = hdr.Width;
            var height = hdr.Height;
            var mapped = HdrToneMapping.Map(hdr, token: token);
            var pixels = mapped.Pixels.ToArray();
            using var thumbnail = new MagickImage();
            thumbnail.ReadPixels(pixels, new PixelReadSettings((uint)width, (uint)height, StorageType.Char,
                PixelMapping.BGRA));
            var sdrPng = thumbnail.ToByteArray(MagickFormat.Png);
            var scale = Math.Min(1, 2048d / Math.Max(width, height));
            if (scale < 1) thumbnail.Resize((uint)Math.Max(1, width * scale), (uint)Math.Max(1, height * scale));
            return new(encoded.ToArray(), width, height, pixels, thumbnail.ToByteArray(MagickFormat.Png))
                { Hdr = hdr, SdrPng = sdrPng };
        }
    }
}
