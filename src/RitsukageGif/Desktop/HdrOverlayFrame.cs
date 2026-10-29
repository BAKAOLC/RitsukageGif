using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static class HdrOverlayFrame
    {
        public static CapturedFrame ComposeWindow(CapturedFrame desktop, PixelRect desktopBounds,
            CapturedFrame window, PixelRect windowBounds, CancellationToken token)
        {
            HdrAnnotationBlender.Validate(desktop);
            HdrAnnotationBlender.Validate(window);
            if (desktop.Width != desktopBounds.Width || desktop.Height != desktopBounds.Height ||
                window.Width != windowBounds.Width || window.Height != windowBounds.Height)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            var region = desktopBounds.Intersect(windowBounds);
            if (region.IsEmpty) throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            var pixels = new byte[checked(region.Width * region.Height * 8)];
            for (var y = 0; y < region.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var background = MemoryMarshal.Cast<byte, Half>(desktop.Pixels.Span.Slice(
                    (region.Y - desktopBounds.Y + y) * desktop.Stride + (region.X - desktopBounds.X) * 8,
                    region.Width * 8));
                var foreground = MemoryMarshal.Cast<byte, Half>(window.Pixels.Span.Slice(
                    (region.Y - windowBounds.Y + y) * window.Stride + (region.X - windowBounds.X) * 8,
                    region.Width * 8));
                var output = MemoryMarshal.Cast<byte, Half>(pixels.AsSpan(y * region.Width * 8, region.Width * 8));
                for (var x = 0; x < output.Length; x += 4)
                {
                    var alpha = window.AlphaMode == CaptureAlphaMode.Opaque
                        ? 1
                        : Math.Clamp((float)foreground[x + 3], 0, 1);
                    var backgroundAlpha = desktop.AlphaMode == CaptureAlphaMode.Straight
                        ? Math.Clamp((float)background[x + 3], 0, 1)
                        : 1;
                    var foregroundAlpha = window.AlphaMode == CaptureAlphaMode.Straight ? alpha : 1;
                    for (var channel = 0; channel < 3; channel++)
                        output[x + channel] = (Half)((float)foreground[x + channel] * foregroundAlpha +
                                                     (float)background[x + channel] * backgroundAlpha * (1 - alpha));
                    output[x + 3] = (Half)1;
                }
            }

            return new(region.Width, region.Height, checked(region.Width * 8), CapturePixelFormat.Rgba16FloatScRgb,
                pixels, desktop.ColorInfo, CaptureAlphaMode.Opaque, window.Revision, []);
        }
    }
}
