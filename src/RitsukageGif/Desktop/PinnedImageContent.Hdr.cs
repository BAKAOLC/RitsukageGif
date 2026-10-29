using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ImageMagick;
using SkiaSharp;

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
            var data = encoded.Span;
            if (data.Length < 8 || data[0] != 137 || !data.Slice(1, 7).SequenceEqual("PNG\r\n\u001a\n"u8)) return null;
            if (data.Length < 33 || BinaryPrimitives.ReadUInt32BigEndian(data[8..]) != 13 ||
                !data.Slice(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException();
            ValidateCrc(data.Slice(12, 17), BinaryPrimitives.ReadUInt32BigEndian(data[29..]));
            var headerWidth = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
            var headerHeight = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
            if (headerWidth is 0 or > int.MaxValue || headerHeight is 0 or > int.MaxValue ||
                (long)headerWidth * headerHeight > MaximumPixels)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(encoded));
            var pq = false;
            var seenCicp = false;
            for (var offset = 8; offset <= data.Length - 12;)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                if (length > data.Length - offset - 12) throw new InvalidDataException();
                if (data.Slice(offset + 4, 4).SequenceEqual("IEND"u8))
                {
                    if (length != 0) throw new InvalidDataException();
                    ValidateCrc(data.Slice(offset + 4, 4), BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 8)..]));
                    break;
                }

                if (data.Slice(offset + 4, 4).SequenceEqual("cICP"u8))
                {
                    if (seenCicp || length != 4) throw new InvalidDataException();
                    seenCicp = true;
                    ValidateCrc(data.Slice(offset + 4, 8), BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 12)..]));
                    var color = data.Slice(offset + 8, 4);
                    if (color[1] is 16 or 18)
                    {
                        if (!color.SequenceEqual(new byte[] { 9, 16, 0, 1 }))
                            throw new NotSupportedException(UiText.Get("PinnedImageHdrUnsupported"));
                        pq = true;
                    }
                }

                offset += checked((int)length + 12);
            }

            if (!pq) return null;
            if ((long)headerWidth * headerHeight * 8 > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(encoded));
            token.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(encoded.ToArray(), false);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException();
            using var metadata = new MagickImage();
            metadata.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            metadata.Ping(encoded.Span, new MagickReadSettings { FrameCount = 1 });
            var origin = metadata.Orientation == Orientation.Undefined
                ? (int)codec.EncodedOrigin
                : (int)metadata.Orientation;
            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(encoded));
            var info = new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Unpremul, codec.Info.ColorSpace);
            using var bitmap = new SKBitmap(info);
            if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException();
            var linear = new byte[checked(width * height * 8)];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, linear, y * width * 8, width * 8);
                var row = linear.AsSpan(y * width * 8, width * 8);
                HdrPngColor.DecodePreviewRow(row, row);
            }

            if (origin != 1)
            {
                if (origin is < 1 or > 8) throw new InvalidDataException();
                var orientedWidth = origin >= 5 ? height : width;
                var orientedHeight = origin >= 5 ? width : height;
                var oriented = new byte[linear.Length];
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < width; x++)
                    {
                        var (targetX, targetY) = origin switch
                        {
                            2 => (width - x - 1, y),
                            3 => (width - x - 1, height - y - 1),
                            4 => (x, height - y - 1),
                            5 => (y, x),
                            6 => (height - y - 1, x),
                            7 => (height - y - 1, width - x - 1),
                            8 => (y, width - x - 1),
                            _ => throw new InvalidDataException(),
                        };
                        linear.AsSpan((y * width + x) * 8, 8)
                            .CopyTo(oriented.AsSpan((targetY * orientedWidth + targetX) * 8));
                    }
                }

                linear = oriented;
                width = orientedWidth;
                height = orientedHeight;
            }

            var hdr = new CapturedFrame(width, height, width * 8, CapturePixelFormat.Rgba16FloatScRgb, linear,
                new(true), CaptureAlphaMode.Straight, 0, []);
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

        private static void ValidateCrc(ReadOnlySpan<byte> data, uint expected)
        {
            var crc = uint.MaxValue;
            foreach (var value in data)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
            }

            if ((crc ^ uint.MaxValue) != expected) throw new InvalidDataException();
        }
    }
}
