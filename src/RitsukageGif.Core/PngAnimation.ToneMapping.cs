using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public static partial class PngAnimation
    {
        public static IEnumerable<(CapturedFrame Frame, int DelayMs)> ReadToneMappedFrames(string path,
            CancellationToken token = default)
        {
            byte[]? canvas = null;
            foreach (var (png, canvasWidth, canvasHeight, region, delayMs, disposal, blend, poster) in
                     ReadEncodedFramesCore(path, false, token))
            {
                token.ThrowIfCancellationRequested();
                if (poster) continue;
                var source = DecodeHdrPng(png, token);
                canvas ??= new byte[checked(canvasWidth * canvasHeight * 8)];
                byte[]? previous = null;
                if (disposal == 2)
                {
                    previous = new byte[checked(region.Width * region.Height * 8)];
                    for (var y = 0; y < region.Height; y++)
                        Buffer.BlockCopy(canvas, ((region.Y + y) * canvasWidth + region.X) * 8,
                            previous, y * region.Width * 8, region.Width * 8);
                }

                for (var y = 0; y < region.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = source.Pixels.Span.Slice(y * source.Stride, region.Width * 8);
                    var target = canvas.AsSpan(((region.Y + y) * canvasWidth + region.X) * 8,
                        region.Width * 8);
                    if (blend == 0) row.CopyTo(target);
                    else BlendToneMappedFrame(row, target);
                }

                var frame = new CapturedFrame(canvasWidth, canvasHeight, canvasWidth * 8,
                    CapturePixelFormat.Rgba16FloatScRgb, canvas, new(true), CaptureAlphaMode.Straight, 0, []);
                yield return (HdrToneMapping.Map(frame, token: token), delayMs);
                if (disposal == 1)
                    for (var y = 0; y < region.Height; y++)
                        Array.Clear(canvas, ((region.Y + y) * canvasWidth + region.X) * 8, region.Width * 8);
                else if (previous is not null)
                    for (var y = 0; y < region.Height; y++)
                        Buffer.BlockCopy(previous, y * region.Width * 8, canvas,
                            ((region.Y + y) * canvasWidth + region.X) * 8, region.Width * 8);
            }
        }

        public static CapturedFrame DecodeToneMappedImage(byte[] png, CancellationToken token = default)
        {
            return HdrToneMapping.Map(DecodeHdrPng(png, token), token: token);
        }

        private static CapturedFrame DecodeHdrPng(byte[] png, CancellationToken token)
        {
            using var stream = new MemoryStream(png, false);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException();
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.RgbaF16,
                SKAlphaType.Unpremul, codec.Info.ColorSpace);
            using var decoded = new SKBitmap();
            if (!decoded.TryAllocPixels(info)) throw new OutOfMemoryException(UiText.Get("MemoryLimit"));
            if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException();
            var stride = checked(info.Width * 8);
            var pixels = new byte[checked(stride * info.Height)];
            for (var y = 0; y < info.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                Marshal.Copy(decoded.GetPixels() + y * decoded.RowBytes, pixels, y * stride, stride);
                var row = pixels.AsSpan(y * stride, stride);
                HdrPngColor.DecodePreviewRow(row, row);
            }

            return new(info.Width, info.Height, stride, CapturePixelFormat.Rgba16FloatScRgb,
                pixels, new(true), CaptureAlphaMode.Straight, 0, []);
        }

        private static void BlendToneMappedFrame(ReadOnlySpan<byte> source, Span<byte> target)
        {
            var front = MemoryMarshal.Cast<byte, Half>(source);
            var back = MemoryMarshal.Cast<byte, Half>(target);
            for (var index = 0; index < front.Length; index += 4)
            {
                var alpha = Math.Clamp((float)front[index + 3], 0, 1);
                var other = Math.Clamp((float)back[index + 3], 0, 1) * (1 - alpha);
                var combined = alpha + other;
                for (var channel = 0; channel < 3; channel++)
                    back[index + channel] = (Half)(combined > 0
                        ? ((float)front[index + channel] * alpha + (float)back[index + channel] * other) / combined
                        : 0);
                back[index + 3] = (Half)combined;
            }
        }
    }
}
