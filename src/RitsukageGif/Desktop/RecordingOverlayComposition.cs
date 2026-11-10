using System.Buffers;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class RecordingOverlayComposition
    {
        // ReSharper disable once UnusedMember.Global
        internal static PreviewFrame Composite(PreviewFrame source, SKPicture overlay, SKRect affectedArea,
            CancellationToken token = default)
        {
            using var buffer = new RecordingOverlayBuffer(source);
            CompositeInto(buffer, overlay, affectedArea, token);
            return buffer.TakeResult();
        }

        internal static void CompositeInto(RecordingOverlayBuffer buffer, SKPicture overlay, SKRect affectedArea,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var source = buffer.Current;
            var area = Clip(source.Width, source.Height, affectedArea);
            if (area.IsEmpty) return;
            var target = buffer.Writable();
            var sdrFrame = new CapturedFrame(target.Width, target.Height, target.Width * 4,
                CapturePixelFormat.Bgra8Srgb, target.Pixels, new(false), CaptureAlphaMode.Premultiplied, 0, []);
            CapturedFrame? hdrFrame = target.HdrPixels is { } hdr
                ? new(target.Width, target.Height, target.Width * 8, CapturePixelFormat.Rgba16FloatScRgb, hdr,
                    new(true, target.HdrReferenceWhiteLevel * FrameColorInfo.ScRgbReferenceWhiteNits),
                    target.HdrAlphaMode, 0, [])
                : null;
            BlendTiles(sdrFrame, hdrFrame, target.Pixels, target.HdrPixels, overlay, area, token);
        }

        internal static CapturedFrameSet Composite(CapturedFrameSet source, SKPicture overlay, SKRect affectedArea,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var area = Clip(source.Sdr.Width, source.Sdr.Height, affectedArea);
            if (area.IsEmpty) return source;
            var sdr = source.Sdr.Pixels.ToArray();
            var hdr = source.Hdr?.Pixels.ToArray();
            BlendTiles(source.Sdr, source.Hdr, sdr, hdr, overlay, area, token);
            return source with
            {
                Sdr = source.Sdr with { Pixels = sdr },
                Hdr = source.Hdr is { } originalHdr ? originalHdr with { Pixels = hdr! } : null,
            };
        }

        private static unsafe void BlendTiles(CapturedFrame sdrFrame, CapturedFrame? hdrFrame, byte[] pixels,
            byte[]? hdr, SKPicture overlay, PixelRect area, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (sdrFrame.PixelFormat != CapturePixelFormat.Bgra8Srgb)
                throw new ArgumentException("The SDR plane must use BGRA pixels.", nameof(sdrFrame));
            var tileSize = Math.Min(1024, Math.Max(area.Width, area.Height));
            var bgra = ArrayPool<byte>.Shared.Rent(checked(tileSize * tileSize * 4));
            try
            {
                using var bitmap = new SKBitmap(tileSize, tileSize, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(bitmap);
                for (var y = area.Y; y < area.Bottom; y += tileSize)
                for (var x = area.X; x < area.Right; x += tileSize)
                {
                    token.ThrowIfCancellationRequested();
                    var width = Math.Min(tileSize, area.Right - x);
                    var height = Math.Min(tileSize, area.Bottom - y);
                    canvas.Clear(SKColors.Transparent);
                    canvas.Save();
                    canvas.Translate(-x, -y);
                    canvas.DrawPicture(overlay);
                    canvas.Restore();
                    canvas.Flush();
                    using var pixmap = bitmap.PeekPixels();
                    fixed (byte* address = bgra)
                        if (!pixmap.ReadPixels(new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
                                (nint)address, width * 4, 0, 0))
                            throw new InvalidOperationException("Cannot read the recording overlay.");
                    for (var row = 0; row < height; row++)
                    for (var column = 0; column < width; column++)
                    {
                        var input = (row * width + column) * 4;
                        var output = (y + row) * sdrFrame.Stride + (x + column) * 4;
                        var alpha = bgra[input + 3] / 255d;
                        if (alpha <= 0) continue;
                        var behind = sdrFrame.AlphaMode == CaptureAlphaMode.Opaque ? 1 : pixels[output + 3] / 255d;
                        var resultAlpha = alpha + behind * (1 - alpha);
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var source = pixels[output + channel] / 255d;
                            if (sdrFrame.AlphaMode == CaptureAlphaMode.Straight) source *= behind;
                            var result = bgra[input + channel] / 255d * alpha + source * (1 - alpha);
                            if (sdrFrame.AlphaMode == CaptureAlphaMode.Straight) result /= resultAlpha;
                            pixels[output + channel] = (byte)Math.Round(Math.Clamp(result, 0, 1) * 255);
                        }

                        pixels[output + 3] = (byte)Math.Round(resultAlpha * 255);
                    }

                    if (hdr is not null && hdrFrame is not null)
                        HdrAnnotationBlender.Blend(hdr, hdrFrame, new(x, y, width, height),
                            bgra.AsSpan(0, width * height * 4), width * 4, CaptureAlphaMode.Straight, token);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bgra);
            }
        }

        private static PixelRect Clip(int width, int height, SKRect affectedArea)
        {
            if (affectedArea.IsEmpty) return default;
            var left = Math.Max(0, (int)Math.Floor(affectedArea.Left) - 2);
            var top = Math.Max(0, (int)Math.Floor(affectedArea.Top) - 2);
            var right = Math.Min(width, (int)Math.Ceiling(affectedArea.Right) + 2);
            var bottom = Math.Min(height, (int)Math.Ceiling(affectedArea.Bottom) + 2);
            return new(left, top, right - left, bottom - top);
        }
    }
}
