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
                var scale = Math.Min(_presentedSize.Width / ImageSize.Width, _presentedSize.Height / ImageSize.Height);
                var thickness =
                    scale > 0 ? Math.Max(1, (int)Math.Ceiling(2 * hdr.Width / (ImageSize.Width * scale))) : 2;
                thickness = Math.Min(thickness, Math.Max(1, Math.Min((right - left + 1) / 2, (bottom - top + 1) / 2)));
                for (var edge = 0; edge < thickness; edge++)
                {
                    for (var x = left; x <= right; x++)
                    {
                        Green(x, top + edge);
                        Green(x, bottom - edge);
                    }

                    for (var y = top; y <= bottom; y++)
                    {
                        Green(left + edge, y);
                        Green(right - edge, y);
                    }
                }
            }

            using var frame = new PreviewFrame(sdr, hdr.Width, hdr.Height, 0, half,
                hdr.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits, hdr.AlphaMode);
            _hdrPreview.SetFrame(frame);
            InvalidateArrange();
            return;

            void Green(int x, int y)
            {
                var offset = (y * hdr.Width + x) * 8;
                BinaryPrimitives.WriteHalfLittleEndian(half.AsSpan(offset), (Half)(0.03f * white));
                BinaryPrimitives.WriteHalfLittleEndian(half.AsSpan(offset + 2), (Half)(0.75f * white));
                BinaryPrimitives.WriteHalfLittleEndian(half.AsSpan(offset + 4), (Half)(0.03f * white));
                BinaryPrimitives.WriteHalfLittleEndian(half.AsSpan(offset + 6), (Half)1);
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_hdrPreview is null || ImageSize.Width <= 0 || ImageSize.Height <= 0) return finalSize;
            var scale = Math.Min(finalSize.Width / ImageSize.Width, finalSize.Height / ImageSize.Height);
            var width = ImageSize.Width * scale;
            var height = ImageSize.Height * scale;
            _hdrPreview.Arrange(new((finalSize.Width - width) / 2, (finalSize.Height - height) / 2, width, height));
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
