using ImageMagick;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public sealed partial class PdfDocumentService
    {
        private static SKBitmap Decode(string path, CancellationToken token)
        {
            if (string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("PDF documents are output documents, not input bitmap images.");
            using var image = new MagickImage();
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var settings = new MagickReadSettings { FrameCount = 1 };
            image.Ping(path, settings);
            ValidateImage(image);
            token.ThrowIfCancellationRequested();
            image.Read(path, settings);
            return Convert(image, token);
        }

        private static SKBitmap Decode(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            if (encoded.Span.StartsWith("%PDF-"u8))
                throw new NotSupportedException("PDF documents are output documents, not input bitmap images.");
            using var image = new MagickImage();
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var settings = new MagickReadSettings { FrameCount = 1 };
            image.Ping(encoded.Span, settings);
            ValidateImage(image);
            token.ThrowIfCancellationRequested();
            image.Read(encoded.Span, settings);
            return Convert(image, token);
        }

        private static unsafe SKBitmap Convert(MagickImage image, CancellationToken token)
        {
            image.AutoOrient();
            ValidateImage(image);
            if (image.GetColorProfile() is not null && !image.TransformColorSpace(ColorProfiles.SRGB))
                throw new InvalidDataException("Image color profile could not be converted to sRGB.");
            image.ColorSpace = ColorSpace.sRGB;
            image.Depth = 8;
            token.ThrowIfCancellationRequested();
            using var colorSpace = SKColorSpace.CreateSrgb();
            var bitmap = new SKBitmap(new((int)image.Width, (int)image.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace));
            try
            {
                using (var pixels = new UnmanagedMemoryStream((byte*)bitmap.GetPixels(), bitmap.ByteCount,
                           bitmap.ByteCount, FileAccess.Write))
                    image.Write(pixels, MagickFormat.Bgra);
                var bytes = new Span<byte>((void*)bitmap.GetPixels(), bitmap.ByteCount);
                for (var row = 0; row < bitmap.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    var line = bytes.Slice(row * bitmap.RowBytes, bitmap.Width * 4);
                    for (var offset = 0; offset < line.Length; offset += 4)
                    {
                        var alpha = line[offset + 3];
                        if (alpha == byte.MaxValue) continue;
                        line[offset] = (byte)((line[offset] * alpha + 127) / 255);
                        line[offset + 1] = (byte)((line[offset + 1] * alpha + 127) / 255);
                        line[offset + 2] = (byte)((line[offset + 2] * alpha + 127) / 255);
                    }
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static void ValidateImage(MagickImage image)
        {
            if (image.Width == 0 || image.Height == 0 || (long)image.Width * image.Height * 4 > 256L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("MemoryLimit"));
            if (image.Format == MagickFormat.Pdf)
                throw new NotSupportedException("PDF documents are output documents, not input bitmap images.");
        }
    }
}
