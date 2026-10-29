using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private static void DrawHdrFrame(HdrLayer output, CapturedFrame frame, SKRect? crop, CancellationToken token)
        {
            if (output.Source is null || output.Source.Width != frame.Width || output.Source.Height != frame.Height)
            {
                output.Source?.Dispose();
                output.Source = CreateHdrBitmap(frame.Width, frame.Height);
            }

            WriteLinearFrame(output.Source, frame, token);
            using var paint = new SKPaint();
            paint.FilterQuality = SKFilterQuality.Medium;
            paint.IsAntialias = true;
            output.Drawing.DrawBitmap(output.Source, crop ?? new SKRect(0, 0, frame.Width, frame.Height),
                new(0, 0, output.Bitmap.Width, output.Bitmap.Height), paint);
            output.IsHdrSource |= frame.ColorInfo.IsHdrSource;
        }

        private static unsafe void DrawHdrBitmap(HdrLayer output, SKBitmap bitmap, SKRect? crop,
            CancellationToken token)
        {
            using var colorSpace = SKColorSpace.CreateSrgb();
            var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul,
                colorSpace);
            var bytes = new byte[checked(info.RowBytes * info.Height)];
            using var pixmap = bitmap.PeekPixels();
            fixed (byte* address = bytes)
                if (!pixmap.ReadPixels(info, (nint)address, info.RowBytes))
                    throw new InvalidOperationException(UiText.Get("HdrSceneFrameUnavailable"));
            DrawHdrFrame(output, new(info.Width, info.Height, info.RowBytes,
                CapturePixelFormat.Bgra8Srgb, bytes, new(false), CaptureAlphaMode.Straight, 0, []), crop, token);
        }

        private static unsafe void WriteLinearFrame(SKBitmap bitmap, CapturedFrame frame, CancellationToken token)
        {
            var pixelBytes = frame.PixelFormat switch
            {
                CapturePixelFormat.Bgra8Srgb => 4,
                CapturePixelFormat.Rgba16FloatScRgb => 8,
                _ => throw new ArgumentOutOfRangeException(nameof(frame)),
            };
            if (frame.Width != bitmap.Width || frame.Height != bitmap.Height ||
                frame.Stride < (long)frame.Width * pixelBytes ||
                frame.Pixels.Length < (long)frame.Stride * frame.Height)
                throw new ArgumentException(null, nameof(frame));
            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var input = frame.Pixels.Span.Slice(y * frame.Stride, frame.Width * pixelBytes);
                var destination = new Span<Half>((byte*)bitmap.GetPixels() + y * bitmap.RowBytes, frame.Width * 4);
                if (frame.PixelFormat == CapturePixelFormat.Rgba16FloatScRgb)
                {
                    var source = MemoryMarshal.Cast<byte, Half>(input);
                    if (frame.AlphaMode == CaptureAlphaMode.Premultiplied) source.CopyTo(destination);
                    else
                        for (var x = 0; x < source.Length; x += 4)
                        {
                            var alpha = frame.AlphaMode == CaptureAlphaMode.Opaque
                                ? 1
                                : Math.Clamp((float)source[x + 3], 0, 1);
                            destination[x] = (Half)((float)source[x] * alpha);
                            destination[x + 1] = (Half)((float)source[x + 1] * alpha);
                            destination[x + 2] = (Half)((float)source[x + 2] * alpha);
                            destination[x + 3] = (Half)alpha;
                        }
                }
                else
                {
                    var white = frame.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits;
                    for (var x = 0; x < input.Length; x += 4)
                    {
                        var alpha = frame.AlphaMode == CaptureAlphaMode.Opaque ? 1 : input[x + 3] / 255f;
                        var denominator = frame.AlphaMode == CaptureAlphaMode.Premultiplied
                            ? Math.Max(alpha, 1 / 255f)
                            : 1;
                        destination[x] = (Half)(SrgbToLinear(Math.Min(1, input[x + 2] / 255f / denominator)) * white *
                                                alpha);
                        destination[x + 1] = (Half)(SrgbToLinear(Math.Min(1, input[x + 1] / 255f / denominator)) *
                                                    white * alpha);
                        destination[x + 2] =
                            (Half)(SrgbToLinear(Math.Min(1, input[x] / 255f / denominator)) * white * alpha);
                        destination[x + 3] = (Half)alpha;
                    }
                }
            }

            bitmap.NotifyPixelsChanged();
        }
    }
}
