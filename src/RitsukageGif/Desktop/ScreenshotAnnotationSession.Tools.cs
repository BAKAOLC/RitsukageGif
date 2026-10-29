using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageMagick;
using FontWeight = Avalonia.Media.FontWeight;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private ScreenshotAnnotation[]? _eraseOriginal;
        private ScreenshotAnnotationImage? _watermarkSource;
        public ScreenshotAnnotationProperties Properties { get; private set; } = new();
        public bool AnnotationsVisible { get; private set; } = true;
        public bool IsDrawingPolyline => _gesture?.Tool == ScreenshotTool.Polyline;

        public void SetWatermarkImage(byte[]? image)
        {
            if (_disposed || IsBusy || IsDrawing) return;
            if (image is not null)
            {
                if (image.Length > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(image));
                var info = new MagickImageInfo(image);
                if (info.Width == 0 || info.Height == 0 || (long)info.Width * info.Height > 16L * 1024 * 1024)
                    throw new ArgumentOutOfRangeException(nameof(image));
                var retainedBytes = _retained.Select(mark => mark.WatermarkImage)
                    .Where(value => value is not null).Distinct()
                    .Sum(value => (long)value!.Bitmap.PixelSize.Width * value.Bitmap.PixelSize.Height * 4 +
                                  (value.Encoded?.Length ?? 0));
                if (retainedBytes + (long)info.Width * info.Height * 4 + image.Length > 128L * 1024 * 1024)
                    throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
            }

            ScreenshotAnnotationImage? replacementSource = null;
            if (image is not null)
            {
                using var stream = new MemoryStream(image, false);
                replacementSource = new(new(stream), [.. image]);
            }

            _watermarkSource?.Dispose();
            _watermarkSource = replacementSource;
            if (_selected is { Tool: ScreenshotTool.Watermark } annotation)
            {
                var replacement = CloneAnnotation(annotation, watermarkImage: _watermarkSource, replaceImage: true);
                SaveHistory();
                _retained.Add(replacement);
                _annotations[_annotations.IndexOf(annotation)] = replacement;
                _selected = replacement;
            }

            Changed?.Invoke();
        }

        public Task UpdateSelectedPropertiesAsync(ScreenshotAnnotationProperties properties)
        {
            if (_disposed || !CanAnnotate || IsBusy || IsDrawing) return Task.CompletedTask;
            Properties = properties.Normalize();
            if (IsEditingText)
            {
                NotifyTextEditState();
                return Task.CompletedTask;
            }

            if (_selected is not { } annotation || annotation.Properties == Properties)
            {
                Changed?.Invoke();
                return Task.CompletedTask;
            }

            return ReplaceAnnotationAsync(_annotations.IndexOf(annotation), CloneAnnotation(annotation,
                properties: Properties));
        }

        public void FinishPolyline()
        {
            if (_gesture is not { Tool: ScreenshotTool.Polyline } annotation) return;
            _gesture = null;
            if (annotation.Points.Count >= 2) Commit(annotation);
            else annotation.Dispose();
            Changed?.Invoke();
        }

        public void ToggleAnnotations()
        {
            AnnotationsVisible = !AnnotationsVisible;
            Changed?.Invoke();
        }

        public void ClearAnnotations()
        {
            CommitTextEdit();
            if (IsBusy || IsDrawing || _annotations.Count == 0) return;
            SaveHistory();
            Logger.ZLogInformation(
                $"Annotations cleared. SessionId={_logSessionId} AnnotationCount={_annotations.Count}");
            _annotations.Clear();
            _selected = null;
            Changed?.Invoke();
        }

        public void RenumberAnnotations(int start = 1)
        {
            if (IsBusy || IsDrawing || start is < 0 or > 998975) return;
            if (_annotations.All(annotation => annotation.Tool != ScreenshotTool.Number)) return;
            SaveHistory();
            for (var index = 0; index < _annotations.Count; index++)
            {
                var annotation = _annotations[index];
                if (annotation.Tool != ScreenshotTool.Number) continue;
                var copy = CloneAnnotation(annotation, properties: annotation.Properties with { Number = start++ });
                _retained.Add(copy);
                _annotations[index] = copy;
            }

            Properties = Properties with { Number = start };
            _selected = null;
            Changed?.Invoke();
        }

        private void EraseAt(Point point)
        {
            if (_eraseOriginal is null) return;
            _annotations.RemoveAll(annotation => HitTest(annotation, point));
            _selected = null;
            Changed?.Invoke();
        }

        private void FinishErase()
        {
            if (_eraseOriginal is not { } original) return;
            var remaining = _annotations.ToArray();
            _annotations.Clear();
            _annotations.AddRange(original);
            if (!original.SequenceEqual(remaining)) SaveHistory();
            _annotations.Clear();
            _annotations.AddRange(remaining);
            _eraseOriginal = null;
            Changed?.Invoke();
        }

        private void CancelErase()
        {
            if (_eraseOriginal is not { } original) return;
            _annotations.Clear();
            _annotations.AddRange(original);
            _eraseOriginal = null;
        }

        private Task<IReadOnlyList<(Bitmap Image, Rect Bounds)>> CreateImageEffectAsync(
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            return CreateMosaicAsync(
                annotation.Tool == ScreenshotTool.Eraser ? RenderBounds(annotation) : annotation.Bounds,
                annotation.Tool == ScreenshotTool.Mosaic ? annotation.MosaicSize : 1, token,
                blurRadius: annotation.Tool == ScreenshotTool.Blur ? annotation.Properties.BlurRadius : 0);
        }

        private static Typeface CreateTypeface(ScreenshotAnnotationProperties properties)
        {
            return new(string.IsNullOrWhiteSpace(properties.FontFamily)
                    ? FontFamily.Default
                    : new(properties.FontFamily), properties.Italic ? FontStyle.Italic : FontStyle.Normal,
                properties.Bold ? FontWeight.Bold : FontWeight.Normal);
        }

        private FormattedText FormatLabel(ScreenshotAnnotation annotation, IBrush brush)
        {
            var text = new FormattedText(annotation.Tool == ScreenshotTool.Number
                    ? annotation.Properties.Number.ToString(CultureInfo.CurrentCulture)
                    : annotation.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                CreateTypeface(annotation.Properties), annotation.FontSize, brush);
            if (_behavior.TextWrapMode != AnnotationTextWrapMode.NoWrap)
                text.MaxTextWidth = annotation.Properties.WrapWidth > 0
                    ? annotation.Properties.WrapWidth
                    : Math.Max(1,
                        AnnotationBounds.Right - annotation.Points[0].X - annotation.Properties.TextPadding * 2);
            return text;
        }

        private void DrawLabel(DrawingContext context, ScreenshotAnnotation annotation)
        {
            var properties = annotation.Properties;
            if (annotation is { Tool: ScreenshotTool.Watermark, WatermarkImage: not null })
            {
                DrawImageWatermark(context, annotation);
                return;
            }

            var text = FormatLabel(annotation, new SolidColorBrush(annotation.Color));
            var padding = properties.TextPadding;
            var size = new Size(text.Width + padding * 2, text.Height + padding * 2);
            if (annotation.Tool == ScreenshotTool.Number)
            {
                var diameter = Math.Max(annotation.FontSize * 1.7, size.Width);
                var bounds = new Rect(annotation.Points[0], new Size(diameter, diameter));
                context.DrawEllipse(new SolidColorBrush(annotation.Color), null, bounds);
                text.SetForegroundBrush(Brushes.White);
                context.DrawText(text, new(bounds.Center.X - text.Width / 2, bounds.Center.Y - text.Height / 2));
                return;
            }

            using var opacity =
                context.PushOpacity(annotation.Tool == ScreenshotTool.Watermark ? properties.Opacity : 1);
            if (annotation.Tool == ScreenshotTool.Watermark && properties.Tiled)
                for (var y = (double)AnnotationBounds.Y;
                     y < AnnotationBounds.Bottom;
                     y += Math.Max(24, size.Height * 2))
                for (var x = (double)AnnotationBounds.X;
                     x < AnnotationBounds.Right;
                     x += Math.Max(48, size.Width * 1.5))
                    DrawAt(new(x, y));
            else
                DrawAt(annotation.Tool == ScreenshotTool.Watermark && properties.NineGrid
                    ? WatermarkOrigin(size, properties.Anchor)
                    : annotation.Points[0]);
            return;

            void DrawAt(Point origin)
            {
                context.DrawRectangle(new SolidColorBrush(properties.TextBackground), null, new(origin, size));
                var location = origin + new Vector(padding, padding);
                if (properties is { TextOutline.A: > 0, TextOutlineWidth: > 0 })
                {
                    var outline = FormatLabel(annotation, new SolidColorBrush(properties.TextOutline));
                    var radius = properties.TextOutlineWidth;
                    for (var direction = 0; direction < 8; direction++)
                    {
                        var angle = direction * Math.PI / 4;
                        context.DrawText(outline,
                            location + new Vector(Math.Cos(angle) * radius, Math.Sin(angle) * radius));
                    }
                }

                context.DrawText(text, location);
                if (properties.StrikeThrough)
                    context.DrawLine(
                        new Pen(new SolidColorBrush(annotation.Color), Math.Max(1, annotation.FontSize / 16)),
                        location + new Vector(0, text.Height / 2), location + new Vector(text.Width, text.Height / 2));
            }
        }

        private Point WatermarkOrigin(Size size, int anchor)
        {
            var column = anchor % 3;
            var row = anchor / 3;
            return new(AnnotationBounds.X + Math.Max(0, AnnotationBounds.Width - size.Width) * column / 2,
                AnnotationBounds.Y + Math.Max(0, AnnotationBounds.Height - size.Height) * row / 2);
        }

        private Rect AnnotationObjectBounds(ScreenshotAnnotation annotation)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (annotation.Tool)
            {
                case ScreenshotTool.Spotlight:
                    return annotation.Bounds;
                case ScreenshotTool.Watermark:
                    if (annotation.Properties.Tiled)
                        return new(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width,
                            AnnotationBounds.Height);
                    var properties = annotation.Properties;
                    Size size;
                    if (annotation.WatermarkImage is { } image)
                    {
                        size = new(properties.ImageWidth,
                            properties.ImageWidth * image.Bitmap.PixelSize.Height / image.Bitmap.PixelSize.Width);
                    }
                    else
                    {
                        var text = FormatLabel(annotation, Brushes.Black);
                        size = new(text.Width + properties.TextPadding * 2, text.Height + properties.TextPadding * 2);
                    }

                    return new(properties.NineGrid ? WatermarkOrigin(size, properties.Anchor) : annotation.Points[0],
                        size);
                default:
                    return RenderBounds(annotation);
            }
        }

        private void DrawImageWatermark(DrawingContext context, ScreenshotAnnotation annotation)
        {
            var image = annotation.WatermarkImage!.Bitmap;
            var properties = annotation.Properties;
            var size = new Size(properties.ImageWidth,
                properties.ImageWidth * image.PixelSize.Height / image.PixelSize.Width);
            using var opacity = context.PushOpacity(properties.Opacity);
            if (properties.Tiled)
                for (var y = (double)AnnotationBounds.Y;
                     y < AnnotationBounds.Bottom;
                     y += Math.Max(16, size.Height * 1.5))
                for (var x = (double)AnnotationBounds.X;
                     x < AnnotationBounds.Right;
                     x += Math.Max(16, size.Width * 1.5))
                    context.DrawImage(image, new(new(x, y), size));
            else
                context.DrawImage(image, new(properties.NineGrid
                    ? WatermarkOrigin(size, properties.Anchor)
                    : annotation.Points[0], size));
        }

        private void DrawSpotlight(DrawingContext context, ScreenshotAnnotation annotation)
        {
            var whole = new Rect(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width,
                AnnotationBounds.Height);
            var hole = annotation.Bounds.Intersect(whole);
            var shade = new SolidColorBrush(Colors.Black);
            using var opacity = context.PushOpacity(annotation.Properties.Opacity);
            if (hole.Width <= 0 || hole.Height <= 0)
            {
                context.DrawRectangle(shade, null, whole);
            }
            else
            {
                context.DrawRectangle(shade, null, new(whole.X, whole.Y, whole.Width, hole.Y - whole.Y));
                context.DrawRectangle(shade, null, new(whole.X, hole.Bottom, whole.Width, whole.Bottom - hole.Bottom));
                context.DrawRectangle(shade, null, new(whole.X, hole.Y, hole.X - whole.X, hole.Height));
                context.DrawRectangle(shade, null, new(hole.Right, hole.Y, whole.Right - hole.Right, hole.Height));
            }
        }

        private static void DrawPixelEraser(DrawingContext context, ScreenshotAnnotation annotation)
        {
            var radius = annotation.Thickness / 2;
            foreach (var center in EraserSamples(annotation))
            {
                using var clip = context.PushGeometryClip(new EllipseGeometry(new(center.X - radius,
                    center.Y - radius, radius * 2, radius * 2)));
                foreach (var tile in annotation.ImageTiles) context.DrawImage(tile.Image, tile.Bounds);
            }
        }

        private bool HasErasableOverlap(ScreenshotAnnotation eraser)
        {
            var left = eraser.Points.Min(point => point.X);
            var top = eraser.Points.Min(point => point.Y);
            var right = eraser.Points.Max(point => point.X);
            var bottom = eraser.Points.Max(point => point.Y);
            var footprint = new Rect(left, top, right - left, bottom - top).Inflate(eraser.Thickness / 2 + 1);
            return _annotations.Any(annotation => annotation.Tool != ScreenshotTool.Eraser &&
                                                  RenderBounds(annotation).Intersects(footprint));
        }

        private static IEnumerable<Point> EraserSamples(ScreenshotAnnotation annotation)
        {
            if (annotation.Points.Count == 0) yield break;
            yield return annotation.Points[0];
            for (var index = 1; index < annotation.Points.Count; index++)
            {
                var start = annotation.Points[index - 1];
                var vector = new Vector(annotation.Points[index].X - start.X, annotation.Points[index].Y - start.Y);
                var count = Math.Max(1, (int)Math.Ceiling(vector.Length / Math.Max(1, annotation.Thickness / 4)));
                for (var step = 1; step <= count; step++) yield return start + vector * ((double)step / count);
            }
        }
    }
}
