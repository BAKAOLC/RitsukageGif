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

        public bool GlobalNumberCounter => _behavior.GlobalNumberCounter;

        public int MinimumNumber => _behavior.NumberStartsAtZero &&
                                    (Tool == ScreenshotTool.Number
                                        ? Properties
                                        : StyleFor(ScreenshotTool.Number).Properties)
                                    .NumberFormat == AnnotationNumberFormat.Decimal
            ? 0
            : 1;

        public int NextNumber => _behavior.GlobalNumberCounter
            ? Math.Clamp(Volatile.Read(ref _globalNumber) + (_behavior.NumberStartsAtZero ? 0 : 1), MinimumNumber,
                999999)
            : Math.Max(MinimumNumber, _nextNumber);

        public void SetNextNumber(int number)
        {
            if (_disposed || IsBusy || IsDrawing) return;
            _nextNumber = Math.Clamp(number, MinimumNumber, 999999);
            if (_behavior.GlobalNumberCounter)
                Interlocked.Exchange(ref _globalNumber, _nextNumber - (_behavior.NumberStartsAtZero ? 0 : 1));
            var style = StyleFor(ScreenshotTool.Number);
            _toolStyles[nameof(ScreenshotTool.Number)] =
                style with { Properties = style.Properties with { Number = NextNumber } };
            Changed?.Invoke();
        }

        // ReSharper disable once UnusedMember.Global
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
            if (_disposed || !CanAnnotate || IsBusy || (IsDrawing && !IsDrawingPolyline)) return Task.CompletedTask;
            var numberFormatChanged = Properties.NumberFormat != properties.NumberFormat;
            Properties = properties.Normalize();
            if (numberFormatChanged && (_selected?.Tool ?? Tool) == ScreenshotTool.Number)
            {
                StoreToolStyle(ScreenshotTool.Number);
                SynchronizeNumberCounter();
            }

            if (_gesture is { Tool: ScreenshotTool.Polyline } draft)
            {
                _gesture = CloneAnnotation(draft, properties: Properties);
                draft.Dispose();
                StoreToolStyle(ScreenshotTool.Polyline);
                Changed?.Invoke();
                return Task.CompletedTask;
            }

            if (IsEditingText)
            {
                NotifyTextEditState();
                return Task.CompletedTask;
            }

            if (ActiveStyleAnnotation is not { } annotation || annotation.Properties == Properties)
            {
                Changed?.Invoke();
                return Task.CompletedTask;
            }

            var replacement = CloneAnnotation(annotation, properties: Properties);
            NormalizeGeometryPoints(replacement);
            StoreToolStyle(AnnotationTool(annotation));
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
                CreateTypeface(annotation.Properties),
                annotation.Tool == ScreenshotTool.Text ? annotation.FontSize * 4 / 3 : annotation.FontSize, brush);
            if (_behavior.TextWrapMode != AnnotationTextWrapMode.NoWrap)
                text.MaxTextWidth = annotation.Properties.WrapWidth > 0
                    ? annotation.Properties.WrapWidth
                    : Math.Max(1,
                        AnnotationBounds.Right - annotation.Points[0].X - TextPadding(annotation.Properties) * 2);
            return text;
        }

        private void DrawLabel(DrawingContext context, ScreenshotAnnotation annotation)
        {
            using var textOptions = context.PushTextOptions(new() { TextRenderingMode = TextRenderingMode.Antialias });
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

            if (annotation.Tool == ScreenshotTool.Watermark)
            {
                DrawTextWatermark(context, annotation);
                return;
            }

            var text = FormatLabel(annotation, new SolidColorBrush(annotation.Color));
            var padding = TextPadding(properties);
            var size = new Size(text.Width, text.Height);
            var origin = annotation.Points[0];
            if (properties.TextBackgroundEnabled)
            {
                var color = Color.FromArgb((byte)Math.Round(properties.TextBackgroundOpacity * 255),
                    properties.TextBackground.R, properties.TextBackground.G, properties.TextBackground.B);
                context.DrawRectangle(new SolidColorBrush(color), null, new Rect(origin, size).Inflate(padding),
                    properties.TextBackgroundRadius, properties.TextBackgroundRadius);
            }

            if (properties is { TextOutlineEnabled: true, TextOutlineWidth: > 0 } && annotation.Color.A > 0)
            {
                if (annotation.TextGeometry is not { } geometry || !geometry.MaxWidth.Equals(text.MaxTextWidth))
                    annotation.TextGeometry = new(text.BuildGeometry(default), text.MaxTextWidth);
                if (annotation.TextGeometry.Path is { } path)
                {
                    using var transform = context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y));
                    var outline = Color.FromArgb(annotation.Color.A, properties.TextOutline.R,
                        properties.TextOutline.G, properties.TextOutline.B);
                    context.DrawGeometry(null, new Pen(new SolidColorBrush(outline), properties.TextOutlineWidth * 2,
                        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), path);
                }
            }

            context.DrawText(text, origin);
            if (properties.StrikeThrough)
                context.DrawLine(new Pen(new SolidColorBrush(annotation.Color), Math.Max(1, annotation.FontSize / 16)),
                    origin + new Vector(0, text.Height / 2), origin + new Vector(text.Width, text.Height / 2));
        }

        private static double TextPadding(ScreenshotAnnotationProperties properties)
        {
            return properties.TextBackgroundEnabled ? properties.TextPadding : 0;
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
                        var text = FormatWatermark(annotation);
                        var padding = annotation.FontSize * 4 / 3 * .1;
                        size = new(text.Width + padding * 2, text.Height + padding * 2);
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
