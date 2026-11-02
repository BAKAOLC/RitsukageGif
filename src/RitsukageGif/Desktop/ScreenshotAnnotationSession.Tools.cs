using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FontWeight = Avalonia.Media.FontWeight;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private ScreenshotAnnotation[]? _eraseOriginal;
        private int _nextNumber = 1;
        private ScreenshotAnnotationImage? _watermarkSource;
        public ScreenshotAnnotationProperties Properties { get; private set; } = new();
        public bool AnnotationsVisible { get; private set; } = true;
        public bool IsDrawingPolyline => _gesture?.Tool == ScreenshotTool.Polyline;

        public int NextNumber => _behavior.GlobalNumberCounter
            ? Math.Clamp(Volatile.Read(ref _globalNumber) + (_behavior.NumberStartsAtZero ? 0 : 1), 0, 999999)
            : _nextNumber;

        public void SetNextNumber(int number)
        {
            if (_disposed || IsBusy || IsDrawing) return;
            _nextNumber = Math.Clamp(number, 0, 999999);
            if (_behavior.GlobalNumberCounter)
                Interlocked.Exchange(ref _globalNumber, _nextNumber - (_behavior.NumberStartsAtZero ? 0 : 1));
            var style = StyleFor(ScreenshotTool.Number);
            _toolStyles[nameof(ScreenshotTool.Number)] =
                style with { Properties = style.Properties with { Number = NextNumber } };
            Changed?.Invoke();
        }

        public void SetWatermarkImage(byte[]? image, string? sourcePath = null)
        {
            if (_disposed || IsBusy || IsDrawing) return;
            if (image is not null) ValidateWatermarkImage(image);

            ScreenshotAnnotationImage? replacementSource = null;
            if (image is not null)
            {
                using var stream = new MemoryStream(image, false);
                replacementSource = new(new(stream), [.. image]);
            }

            _watermarkLoadWork?.Cancel();
            _watermarkSource?.Dispose();
            _watermarkSource = replacementSource;
            _watermarkSourcePath = image is null ? null : sourcePath;
            Properties = Properties with { WatermarkImagePath = _watermarkSourcePath };
            Error = null;
            if (_selected is { Tool: ScreenshotTool.Watermark } annotation)
            {
                var replacement = CloneAnnotation(annotation, properties: Properties, watermarkImage: _watermarkSource,
                    replaceImage: true);
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
            var numberFormatChanged = Properties.NumberFormat != properties.NumberFormat;
            Properties = properties.Normalize();
            if (numberFormatChanged && (_selected?.Tool ?? Tool) == ScreenshotTool.Number)
            {
                StoreToolStyle(ScreenshotTool.Number);
                SynchronizeNumberCounter();
            }

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

            var replacement = CloneAnnotation(annotation, properties: Properties);
            NormalizeGeometryPoints(replacement);
            StoreToolStyle(annotation.Tool);
            return numberFormatChanged && annotation.Tool == ScreenshotTool.Number
                ? ReplaceNumberPropertiesAsync(annotation, replacement)
                : ReplaceAnnotationAsync(_annotations.IndexOf(annotation), replacement);
        }

        public void FinishPolyline()
        {
            if (_gesture is not { Tool: ScreenshotTool.Polyline } annotation) return;
            annotation.Points.RemoveAt(annotation.Points.Count - 1);
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
            SynchronizeNumberCounter();
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
            ScreenshotAnnotation annotation, CancellationToken token, int? layerIndex = null)
        {
            return annotation.Tool switch
            {
                ScreenshotTool.Magnifier => Task.FromResult<IReadOnlyList<(Bitmap Image, Rect Bounds)>>([]),
                ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase =>
                    CreatePixelPreviewAsync(annotation,
                        [.. _annotations.Take(layerIndex ?? _annotations.Count)], token),
                _ => CreateMosaicAsync(
                    IsPixelTool(annotation.Tool) ? PixelEffectBounds(annotation) : annotation.Bounds,
                    annotation.Tool == ScreenshotTool.Mosaic ? annotation.MosaicSize : 1, token,
                    blurRadius: annotation.Tool == ScreenshotTool.Blur ? annotation.Properties.BlurRadius : 0),
            };
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
            var text = new FormattedText(annotation.DisplayText, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
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
            if (annotation.Tool == ScreenshotTool.Number)
            {
                DrawNumber(context, annotation);
                return;
            }

            var properties = annotation.Properties;
            if (annotation is { Tool: ScreenshotTool.Watermark, WatermarkImage: not null })
            {
                DrawImageWatermark(context, annotation);
                return;
            }

            var text = FormatLabel(annotation, new SolidColorBrush(annotation.Color));
            var padding = properties.TextPadding;
            var size = new Size(text.Width + padding * 2, text.Height + padding * 2);
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
                case ScreenshotTool.Rectangle:
                case ScreenshotTool.Ellipse:
                    return GeometryFor(annotation).Path.Bounds;
                case ScreenshotTool.Arrow:
                case ScreenshotTool.Line:
                case ScreenshotTool.Polyline:
                    return LineObjectBounds(annotation);
                case ScreenshotTool.Brush:
                    return (annotation.Points.Count == 1
                        ? new(annotation.Points[0], default(Size))
                        : BrushGeometryFor(annotation).Path.Bounds).Inflate(annotation.Thickness / 2);
                case ScreenshotTool.Magnifier:
                    return MagnifierLensBounds(annotation).Union(annotation.Bounds);
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

        private bool HasErasableOverlap(ScreenshotAnnotation eraser)
        {
            var footprint = PixelEffectBounds(eraser);
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
