using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static readonly IBrush MagnifierEllipseShadow = new RadialGradientBrush
        {
            GradientStops =
            [
                new(Color.FromArgb(96, 0, 0, 0), 0),
                new(Color.FromArgb(72, 0, 0, 0), .8),
                new(Color.FromArgb(0, 0, 0, 0), 1),
            ],
        };

        private int _magnifierPreviewDepth;
        private long _magnifierPreviewInFlight;
        private Rect? _magnifierPreviewViewport;
        private bool _movingMagnifierLens;

        private void TrimMagnifierPreviewsForTiles(long tileBytes)
        {
            var objects = _retained.Concat(_annotations).Distinct().ToArray();
            var bytes = objects.Sum(mark => mark.MagnifierPreview is { } cache
                ? (long)cache.Bitmap.PixelSize.Width * cache.Bitmap.PixelSize.Height * 4
                : 0);
            foreach (var mark in objects)
            {
                if (bytes + tileBytes <= AnnotationImageBudget) break;
                if (mark.MagnifierPreview is not { } cache) continue;
                bytes -= (long)cache.Bitmap.PixelSize.Width * cache.Bitmap.PixelSize.Height * 4;
                cache.Dispose();
                mark.MagnifierPreview = null;
            }
        }

        private void MoveMagnifierLens(ScreenshotAnnotation original, PixelPoint point)
        {
            var bounds = MagnifierLensBounds(original);
            var dx = Math.Clamp(point.X - _moveStart.X, Math.Min(0, AnnotationBounds.X - bounds.Left),
                Math.Max(0, AnnotationBounds.Right - bounds.Right));
            var dy = Math.Clamp(point.Y - _moveStart.Y, Math.Min(0, AnnotationBounds.Y - bounds.Top),
                Math.Max(0, AnnotationBounds.Bottom - bounds.Bottom));
            ReplaceMovingMagnifier(original, original.MagnifierOffset + new Vector(dx, dy));
            Changed?.Invoke();
        }

        private void ResizeMagnifier(ScreenshotAnnotation original, PixelPoint point)
        {
            var anchor = ObjectControlPoint(original, (_resizeCorner + 2) % 4);
            var current = new Point(Math.Clamp(point.X, AnnotationBounds.X, AnnotationBounds.Right),
                Math.Clamp(point.Y, AnnotationBounds.Y, AnnotationBounds.Bottom));
            var center = new Point((anchor.X + current.X) / 2, (anchor.Y + current.Y) / 2);
            var lens = MagnifierLensBounds(original);
            var replacement = ReplaceMovingMagnifier(original, lens.Center - center);
            if (replacement is null) return;
            replacement.Points[0] = anchor;
            replacement.Points[^1] = current;
            Changed?.Invoke();
        }

        private ScreenshotAnnotation? ReplaceMovingMagnifier(ScreenshotAnnotation original,
            Vector offset)
        {
            var index = _annotations.IndexOf(_selected!);
            if (index < 0) return null;
            var previous = _selected;
            var replacement = CloneAnnotation(original);
            replacement.MagnifierOffset = offset;
            _annotations[index] = replacement;
            _selected = replacement;
            if (!ReferenceEquals(previous, original) && previous is not null)
            {
                replacement.MagnifierPreview = previous.MagnifierPreview;
                previous.MagnifierPreview = null;
                previous.Dispose();
            }

            return replacement;
        }

        private static Rect MagnifierLensBounds(ScreenshotAnnotation annotation)
        {
            var source = annotation.Bounds;
            var properties = annotation.Properties;
            var width = source.Width * properties.Magnification;
            var height = source.Height * properties.Magnification;
            return new(source.Center.X + annotation.MagnifierOffset.X - width / 2,
                source.Center.Y + annotation.MagnifierOffset.Y - height / 2, width, height);
        }

        private static Geometry MagnifierShape(ScreenshotAnnotation annotation, Rect bounds)
        {
            return annotation.Properties.MagnifierEllipse
                ? new EllipseGeometry(bounds)
                : new RectangleGeometry(bounds, annotation.Properties.CornerRadius, annotation.Properties.CornerRadius);
        }

        private static bool MagnifierContains(ScreenshotAnnotation annotation, Rect bounds, Point point)
        {
            if (!bounds.Contains(point)) return false;
            if (annotation.Properties.MagnifierEllipse)
            {
                var dx = (point.X - bounds.Center.X) / (bounds.Width / 2);
                var dy = (point.Y - bounds.Center.Y) / (bounds.Height / 2);
                return dx * dx + dy * dy <= 1;
            }

            var radius = Math.Min(annotation.Properties.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2);
            if (radius <= 0) return true;
            var x = Math.Clamp(point.X, bounds.X + radius, bounds.Right - radius);
            var y = Math.Clamp(point.Y, bounds.Y + radius, bounds.Bottom - radius);
            return (point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y) <= radius * radius;
        }

        private static double MagnifierCoverage(ScreenshotAnnotation annotation, Rect lens, int x, int y)
        {
            if (!annotation.Properties.MagnifierAntialias)
                return MagnifierContains(annotation, lens, new(x + .5, y + .5)) ? 1 : 0;
            if (MagnifierContains(annotation, lens, new(x, y)) &&
                MagnifierContains(annotation, lens, new(x + 1, y)) &&
                MagnifierContains(annotation, lens, new(x, y + 1)) &&
                MagnifierContains(annotation, lens, new(x + 1, y + 1))) return 1;
            var count = 0;
            for (var sy = 0; sy < 4; sy++)
            for (var sx = 0; sx < 4; sx++)
                if (MagnifierContains(annotation, lens, new(x + (sx + .5) / 4, y + (sy + .5) / 4)))
                    count++;
            return count / 16d;
        }

        private void DrawMagnifier(DrawingContext context, ScreenshotAnnotation annotation, bool sampleImages)
        {
            var source = annotation.Bounds;
            var lens = MagnifierLensBounds(annotation);
            if (source.Width <= 0 || source.Height <= 0) return;
            var properties = annotation.Properties;
            var lensPath = MagnifierShape(annotation, lens);
            var brush = new SolidColorBrush(annotation.Color);
            var pen = new Pen(brush, annotation.Thickness);
            if (properties.MagnifierShadow)
            {
                using var outside = context.PushGeometryClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(lens.Inflate(12)), lensPath));
                if (properties.MagnifierEllipse)
                    context.DrawEllipse(MagnifierEllipseShadow, null,
                        new(lens.X - 4, lens.Y - 4, lens.Width + 12, lens.Height + 12));
                else
                    context.DrawRectangle(null, null, lens, properties.CornerRadius, properties.CornerRadius,
                        new(new() { Blur = 6, OffsetX = 2, OffsetY = 2, Color = Color.FromArgb(96, 0, 0, 0) }));
            }

            DrawMagnifierConnector(context, annotation, source, lens, pen, brush);
            if (sampleImages)
            {
                var visible = lens.Intersect(new(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width,
                    AnnotationBounds.Height));
                if (_magnifierPreviewViewport is { } viewport) visible = visible.Intersect(viewport);
                if (visible.Width <= 0 || visible.Height <= 0) return;
                var preview = MagnifierPreview(annotation, visible);
                var bitmap = preview.Bitmap;
                var sample = preview.Source;
                using var clip = context.PushGeometryClip(lensPath);
                using var options = context.PushRenderOptions(new()
                {
                    BitmapInterpolationMode = properties.MagnifierAntialias
                        ? BitmapInterpolationMode.HighQuality
                        : BitmapInterpolationMode.None,
                });
                context.DrawImage(bitmap, new(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                    new(lens.X + (sample.X - source.X) * properties.Magnification,
                        lens.Y + (sample.Y - source.Y) * properties.Magnification,
                        sample.Width * properties.Magnification, sample.Height * properties.Magnification));
            }

            context.DrawGeometry(null, pen, lensPath);
        }

        private static void DrawMagnifierConnector(DrawingContext context, ScreenshotAnnotation annotation,
            Rect source, Rect lens, IPen pen, IBrush brush)
        {
            if (annotation.Properties.MagnifierLink == AnnotationMagnifierLink.None ||
                MagnifierContains(annotation, lens, source.Center)) return;
            var end = MagnifierBoundary(annotation, lens, source.Center);
            var start = source.Center;
            if (annotation.Properties.MagnifierLink == AnnotationMagnifierLink.Outline)
            {
                start = MagnifierBoundary(annotation, source, lens.Center);
                context.DrawGeometry(null, pen, MagnifierShape(annotation, source));
            }

            context.DrawLine(pen, start, end);
            if (annotation.Properties.MagnifierLink == AnnotationMagnifierLink.DotLine)
                context.DrawEllipse(brush, null, start, annotation.Thickness * 2, annotation.Thickness * 2);
        }

        private static Point MagnifierBoundary(ScreenshotAnnotation annotation, Rect bounds, Point target)
        {
            var direction = target - bounds.Center;
            var low = 0d;
            var high = 1d;
            for (var i = 0; i < 24; i++)
            {
                var middle = (low + high) / 2;
                if (MagnifierContains(annotation, bounds, bounds.Center + direction * middle)) low = middle;
                else high = middle;
            }

            return bounds.Center + direction * low;
        }

        private MagnifierPreviewCache MagnifierPreview(ScreenshotAnnotation annotation, Rect visible)
        {
            var fullSource = annotation.Bounds;
            var lens = MagnifierLensBounds(annotation);
            var scale = annotation.Properties.Magnification;
            var sampled = new Rect(fullSource.X + (visible.X - lens.X) / scale,
                    fullSource.Y + (visible.Y - lens.Y) / scale, visible.Width / scale, visible.Height / scale)
                .Inflate(1).Intersect(fullSource);
            var source = new Rect(Math.Floor(sampled.X), Math.Floor(sampled.Y),
                Math.Ceiling(sampled.Right) - Math.Floor(sampled.X),
                Math.Ceiling(sampled.Bottom) - Math.Floor(sampled.Y));
            var index = _annotations.IndexOf(annotation);
            if (index < 0) index = _annotations.Count;
            var show = annotation.Properties.MagnifierShowAnnotations;
            var layerCount = show ? index : 0;
            if (annotation.MagnifierPreview is { } old && MagnifierPreviewCurrent(old, source, show, layerCount))
                return old;
            if (ReferenceEquals(annotation, _selected) && _moving?.MagnifierPreview is { } moving &&
                MagnifierPreviewCurrent(moving, source, show, layerCount)) return moving;

            var layers = new MagnifierSourceState[layerCount];
            for (var i = 0; i < layers.Length; i++) layers[i] = MagnifierLayerState(_annotations[i]);
            var frames = _overlayFrame is { } overlay ? _frames.Append(overlay).ToArray() : _frames.ToArray();

            var left = (int)Math.Floor(source.X);
            var top = (int)Math.Floor(source.Y);
            var width = Math.Max(1, (int)Math.Ceiling(source.Right) - left);
            var height = Math.Max(1, (int)Math.Ceiling(source.Bottom) - top);
            var previewObjects = _retained.Concat(_annotations).Append(annotation).Distinct().ToArray();
            var requestedBytes = (long)width * height * 8;
            var tileBytes = previewObjects.SelectMany(mark => mark.ImageTiles)
                .Sum(tile => (long)tile.Image.PixelSize.Width * tile.Image.PixelSize.Height * 4);
            var retainedBytes = previewObjects.Sum(mark => mark.MagnifierPreview is { } cache
                ? (long)cache.Bitmap.PixelSize.Width * cache.Bitmap.PixelSize.Height * 4
                : 0);
            if (_magnifierPreviewDepth == 0)
                foreach (var mark in previewObjects)
                {
                    if (retainedBytes + tileBytes + requestedBytes <= AnnotationImageBudget) break;
                    if (mark.MagnifierPreview is { } cache)
                        retainedBytes -= (long)cache.Bitmap.PixelSize.Width * cache.Bitmap.PixelSize.Height * 4;
                    mark.MagnifierPreview?.Dispose();
                    mark.MagnifierPreview = null;
                }

            var available = Math.Max(8, AnnotationImageBudget - tileBytes - retainedBytes - _magnifierPreviewInFlight);
            var previewScale = Math.Min(1, Math.Sqrt(available / (double)requestedBytes));
            width = Math.Max(1, (int)Math.Floor(width * previewScale));
            height = Math.Max(1, (int)Math.Floor(height * previewScale));
            var maximumPixels = Math.Max(1, available / 8);
            if ((long)width * height > maximumPixels)
            {
                if (width >= height) width = (int)Math.Max(1, maximumPixels / height);
                else height = (int)Math.Max(1, maximumPixels / width);
            }

            var scaleX = width / source.Width;
            var scaleY = height / source.Height;
            var reserved = (long)width * height * 8;
            _magnifierPreviewInFlight += reserved;
            _magnifierPreviewDepth++;
            try
            {
                using var raw = new WriteableBitmap(new(width, height), new(96, 96), PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul);
                using (var pixels = raw.Lock())
                {
                    var emptyRow = new byte[width * 4];
                    for (var row = 0; row < height; row++)
                        Marshal.Copy(emptyRow, 0, pixels.Address + row * pixels.RowBytes, width * 4);
                    var sample = MagnifierPixelBounds(source);
                    foreach (var frame in frames)
                    {
                        var clipped = frame.Region.Intersect(sample);
                        if (scaleX.Equals(1d) && scaleY.Equals(1d))
                        {
                            for (var y = 0; y < clipped.Height; y++)
                                Marshal.Copy(frame.Pixels,
                                    ((clipped.Y + y - frame.Region.Y) * frame.Region.Width + clipped.X -
                                     frame.Region.X) * 4,
                                    pixels.Address + (clipped.Y + y - top) * pixels.RowBytes + (clipped.X - left) * 4,
                                    clipped.Width * 4);
                        }
                        else
                        {
                            var row = new byte[width * 4];
                            for (var y = 0; y < height; y++)
                            {
                                var sy = top + (int)(y / scaleY);
                                if (sy < clipped.Y || sy >= clipped.Bottom) continue;
                                Marshal.Copy(pixels.Address + y * pixels.RowBytes, row, 0, row.Length);
                                for (var x = 0; x < width; x++)
                                {
                                    var sx = left + (int)(x / scaleX);
                                    if (sx < clipped.X || sx >= clipped.Right) continue;
                                    frame.Pixels
                                        .AsSpan(((sy - frame.Region.Y) * frame.Region.Width + sx - frame.Region.X) * 4,
                                            4)
                                        .CopyTo(row.AsSpan(x * 4));
                                }

                                Marshal.Copy(row, 0, pixels.Address + y * pixels.RowBytes, row.Length);
                            }
                        }
                    }
                }

                var bitmap = new RenderTargetBitmap(new(width, height), new(96, 96));
                var previousViewport = _magnifierPreviewViewport;
                _magnifierPreviewViewport = source;
                try
                {
                    using var drawing = bitmap.CreateDrawingContext();
                    drawing.DrawImage(raw, new(0, 0, width, height));
                    using var transform =
                        drawing.PushTransform(
                            Matrix.CreateTranslation(-left, -top) * Matrix.CreateScale(scaleX, scaleY));
                    for (var i = 0; i < layers.Length; i++)
                        if (RenderBounds(_annotations[i]).Intersects(source))
                            Draw(drawing, _annotations[i]);
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
                finally
                {
                    _magnifierPreviewViewport = previousViewport;
                }

                annotation.MagnifierPreview?.Dispose();
                annotation.MagnifierPreview = new()
                {
                    Source = source, ShowAnnotations = show, Bitmap = bitmap,
                    Frames = frames, Layers = layers,
                };
                return annotation.MagnifierPreview;
            }
            finally
            {
                _magnifierPreviewDepth--;
                _magnifierPreviewInFlight -= reserved;
            }
        }

        private static MagnifierSourceState MagnifierLayerState(ScreenshotAnnotation annotation)
        {
            if (annotation.MagnifierSourceState?.Matches(annotation) != true)
                annotation.MagnifierSourceState = new(annotation);
            return annotation.MagnifierSourceState;
        }

        private bool MagnifierPreviewCurrent(MagnifierPreviewCache cache, Rect source, bool show, int layerCount)
        {
            if (cache.Source != source || cache.ShowAnnotations != show || cache.Layers.Count != layerCount ||
                cache.Frames.Count != _frames.Count + (_overlayFrame is null ? 0 : 1)) return false;
            for (var i = 0; i < _frames.Count; i++)
                if (cache.Frames[i].Region != _frames[i].Region ||
                    !ReferenceEquals(cache.Frames[i].Pixels, _frames[i].Pixels))
                    return false;
            if (_overlayFrame is { } overlay && (cache.Frames[^1].Region != overlay.Region ||
                                                 !ReferenceEquals(cache.Frames[^1].Pixels, overlay.Pixels)))
                return false;
            for (var i = 0; i < layerCount; i++)
                if (!ReferenceEquals(cache.Layers[i], MagnifierLayerState(_annotations[i])))
                    return false;
            return true;
        }
    }
}
