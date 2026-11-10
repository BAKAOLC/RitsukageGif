using Avalonia;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal const long MaximumImagePreviewPixels = 8L * 1024 * 1024;
        private ImagePreviewLayout? _imagePreviewHdrLayout;
        private CapturedFrame? _imagePreviewHdrSource, _imagePreviewHdrSample;
        private byte[]? _imagePreviewPng;
        private PngRaster? _imagePreviewPngRaster;
        private ImagePreviewLayout? _imagePreviewSdrLayout;
        private PngRaster? _imagePreviewSdrSample;
        private object? _imagePreviewSdrSource;

        internal void ClearImagePreviewCache()
        {
            _imagePreviewPng = null;
            _imagePreviewPngRaster = null;
            _imagePreviewHdrSource = _imagePreviewHdrSample = null;
            _imagePreviewHdrLayout = _imagePreviewSdrLayout = null;
            _imagePreviewSdrSource = null;
            _imagePreviewSdrSample = null;
        }

        private ImagePreviewLayout ImagePreviewLayout(Rect visible, PixelSize target, int width, int height,
            IReadOnlyList<ScreenshotAnnotation> snapshot)
        {
            if (visible.Width <= 0 || visible.Height <= 0 || !double.IsFinite(visible.X) ||
                !double.IsFinite(visible.Y) || !double.IsFinite(visible.Width) || !double.IsFinite(visible.Height) ||
                target.Width <= 0 || target.Height <= 0 ||
                (long)target.Width * target.Height > MaximumImagePreviewPixels ||
                visible.X < 0 || visible.Y < 0 || visible.Right > width || visible.Bottom > height)
                throw new ArgumentOutOfRangeException(nameof(visible));
            var left = (int)Math.Floor(visible.X);
            var top = (int)Math.Floor(visible.Y);
            var sampling = ExpandPixelPreviewRegion(new(left, top,
                    (int)Math.Ceiling(visible.Right) - left, (int)Math.Ceiling(visible.Bottom) - top), snapshot)
                .Intersect(new(0, 0, width, height));
            var scale = Math.Min(1, Math.Min(target.Width / visible.Width, target.Height / visible.Height));
            scale = Math.Min(scale, Math.Sqrt((double)MaximumImagePreviewPixels /
                                              ((long)sampling.Width * sampling.Height)));
            var workWidth = Math.Max(1, (int)Math.Floor(sampling.Width * scale));
            var workHeight = Math.Max(1, (int)Math.Floor(sampling.Height * scale));
            return new(visible, sampling, new(workWidth, workHeight));
        }

        private ScreenshotAnnotationSession ImagePreviewPixelSession(ImagePreviewLayout layout)
        {
            return new()
            {
                Selection = layout.WorkBounds, _limitToSelection = true, _behavior = _behavior,
            };
        }

        private static ScreenshotAnnotation ImagePreviewPixelAnnotation(ScreenshotAnnotation original,
            ImagePreviewLayout layout)
        {
            var scale = Math.Sqrt(layout.ScaleX * layout.ScaleY);
            var copy = new ScreenshotAnnotation
            {
                Tool = original.Tool, Color = original.Color, Thickness = original.Thickness * scale,
                Text = original.Text, WatermarkTimestamp = original.WatermarkTimestamp,
                AutoMosaicId = original.AutoMosaicId, AutoMosaicRegions = original.AutoMosaicRegions,
                AutoMosaicCandidates = original.AutoMosaicCandidates,
                AutoMosaicCandidateBounds = original.AutoMosaicCandidateBounds,
                FontSize = original.FontSize * scale,
                MosaicSize = Math.Max(1, (int)Math.Round(original.MosaicSize * scale)),
                MagnifierOffset = new(original.MagnifierOffset.X * layout.ScaleX,
                    original.MagnifierOffset.Y * layout.ScaleY),
                Properties = original.Properties with
                {
                    BlurRadius = Math.Max(1, (int)Math.Round(original.Properties.BlurRadius * scale)),
                    InpaintingRadius = Math.Max(1, (int)Math.Round(original.Properties.InpaintingRadius * scale)),
                    CornerRadius = original.Properties.CornerRadius * scale,
                },
            };
            foreach (var point in original.Points) copy.Points.Add(layout.Transform.Transform(point));
            return copy;
        }

        private (PixelRect Area, byte[] Pixels)? ImagePreviewOverlay(ScreenshotAnnotation annotation,
            ImagePreviewLayout layout, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var bounds = RenderBounds(annotation).Intersect(new(layout.Sampling.X, layout.Sampling.Y,
                layout.Sampling.Width, layout.Sampling.Height));
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            var scaled = bounds.TransformToAABB(layout.Transform).Inflate(1);
            var left = Math.Max(0, (int)Math.Floor(scaled.X));
            var top = Math.Max(0, (int)Math.Floor(scaled.Y));
            var right = Math.Min(layout.WorkSize.Width, (int)Math.Ceiling(scaled.Right));
            var bottom = Math.Min(layout.WorkSize.Height, (int)Math.Ceiling(scaled.Bottom));
            if (right <= left || bottom <= top) return null;
            var area = new PixelRect(left, top, right - left, bottom - top);
            using var bitmap = new RenderTargetBitmap(new(area.Width, area.Height), new(96, 96));
            using (var drawing = bitmap.CreateDrawingContext())
            {
                var transform = new Matrix(layout.ScaleX, 0, 0, layout.ScaleY,
                    -layout.Sampling.X * layout.ScaleX - area.X, -layout.Sampling.Y * layout.ScaleY - area.Y);
                using var coordinate = drawing.PushTransform(transform);
                using var clip = drawing.PushClip(new(AnnotationBounds.X, AnnotationBounds.Y,
                    AnnotationBounds.Width, AnnotationBounds.Height));
                Draw(drawing, annotation, false);
            }

            using var pixels = new ImagePreviewFramebuffer(new(area.Width, area.Height));
            bitmap.CopyPixels(pixels);
            return (area, pixels.Pixels);
        }
    }
}
