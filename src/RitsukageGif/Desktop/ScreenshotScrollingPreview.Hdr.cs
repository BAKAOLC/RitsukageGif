using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingPreview
    {
        private static readonly ILogger HdrLogger = LogHub.GetLogger("Desktop.Capture.ScrollingPreview");
        private ScrollCaptureImage? _hdrImage;
        private HdrPreviewHost? _hdrPreview;
        private bool _hdrUnavailable;
        private Size _presentedSize;
        private (PixelRect Viewport, bool Matched)? _presentedViewport;

        internal void UpdateHdr(ScrollCaptureImage? image)
        {
            if (!HdrPreviewHost.IsSupported || _hdrUnavailable) return;
            if (image is not null)
            {
                _hdrImage = image.Hdr is null ? null : image;
                _presentedViewport = null;
            }

            if (_hdrImage?.Hdr is not { } hdr)
            {
                _hdrPreview?.SetFrame(null);
                return;
            }

            try
            {
                if (_hdrPreview is null)
                {
                    _hdrPreview = new();
                    Child = _hdrPreview;
                    _hdrPreview.DisplayColorChanged += () =>
                    {
                        _presentedViewport = null;
                        UpdateHdr(null);
                    };
                }
            }
            catch (Exception error) when (error is ExternalException or NotSupportedException)
            {
                HdrLogger.ZLogWarning(error, $"Scrolling HDR preview presentation is unavailable.");
                if (_hdrPreview is { } unavailable)
                {
                    Child = null;
                    unavailable.Dispose();
                }

                _hdrPreview = null;
                _hdrUnavailable = true;
                return;
            }

            if (_presentedViewport == (Viewport, Matched)) return;
            _presentedViewport = (Viewport, Matched);
            var white = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle() is { } handle
                ? HdrPreviewHost.DisplayWhiteLevel(handle.Handle,
                    hdr.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits)
                : 1;
            var size = checked(hdr.Width * hdr.Height);
            var sdr = ArrayPool<byte>.Shared.Rent(checked(size * 4));
            var half = ArrayPool<byte>.Shared.Rent(checked(size * 8));
            _hdrImage.Pixels.CopyTo(sdr, 0);
            for (var y = 0; y < hdr.Height; y++)
                hdr.Pixels.Span.Slice(y * hdr.Stride, hdr.Width * 8).CopyTo(half.AsSpan(y * hdr.Width * 8));
            if (Matched && ImageSize is { Width: > 0, Height: > 0 })
            {
                var left = Math.Clamp((int)((long)Viewport.X * hdr.Width / ImageSize.Width), 0, hdr.Width - 1);
                var top = Math.Clamp((int)((long)Viewport.Y * hdr.Height / ImageSize.Height), 0, hdr.Height - 1);
                var right = Math.Clamp((int)((long)Viewport.Right * hdr.Width / ImageSize.Width) - 1, left,
                    hdr.Width - 1);
                var bottom = Math.Clamp((int)((long)Viewport.Bottom * hdr.Height / ImageSize.Height) - 1, top,
                    hdr.Height - 1);
                var scale = ImageRectangle(_presentedSize).Width / ImageSize.Width;
                var thickness =
                    scale > 0
                        ? Math.Max(1, (int)Math.Ceiling(PreviewBorder * hdr.Width / (ImageSize.Width * scale)))
                        : 4;
                var red = Linear(ViewportColor.R, white);
                var green = Linear(ViewportColor.G, white);
                var blue = Linear(ViewportColor.B, white);
                for (var edge = 0; edge < thickness; edge++)
                {
                    for (var x = Math.Max(0, left - thickness); x <= Math.Min(hdr.Width - 1, right + thickness); x++)
                    {
                        Paint(x, top - 1 - edge, red, green, blue);
                        Paint(x, bottom + 1 + edge, red, green, blue);
                    }

                    for (var y = top; y <= bottom; y++)
                    {
                        Paint(left - 1 - edge, y, red, green, blue);
                        Paint(right + 1 + edge, y, red, green, blue);
                    }
                }
            }

            using var frame = new PreviewFrame(sdr, hdr.Width, hdr.Height, 0, half,
                hdr.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits, hdr.AlphaMode);
            _hdrPreview.SetFrame(frame);
            InvalidateArrange();
            return;

            void Paint(int x, int y, Half red, Half green, Half blue)
            {
                if ((uint)x >= (uint)hdr.Width || (uint)y >= (uint)hdr.Height) return;
                var offset = (y * hdr.Width + x) * 8;
                BinaryPrimitives.WriteHalfLittleEndian(half.AsSpan(offset), red);
                BinaryPrimitives.WriteHalfLittleEndian(half.AsSpan(offset + 2), green);
                BinaryPrimitives.WriteHalfLittleEndian(half.AsSpan(offset + 4), blue);
                BinaryPrimitives.WriteHalfLittleEndian(half.AsSpan(offset + 6), (Half)1);
            }
        }

        private static Half Linear(byte component, float white)
        {
            var value = component / 255f;
            return (Half)((value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f)) * white);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_hdrPreview is null || ImageSize.Width <= 0 || ImageSize.Height <= 0) return finalSize;
            _hdrPreview.Arrange(ImageRectangle(finalSize));
            if (_presentedSize != finalSize)
            {
                _presentedSize = finalSize;
                _presentedViewport = null;
                UpdateHdr(null);
            }

            return finalSize;
        }

        internal void DisposeHdr()
        {
            _hdrImage = null;
            _hdrPreview?.Dispose();
        }
    }
}
