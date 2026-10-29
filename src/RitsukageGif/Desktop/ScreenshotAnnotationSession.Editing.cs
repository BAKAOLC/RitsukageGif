using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static readonly IPen SelectedAnnotationPen = new Pen(Brushes.DeepSkyBlue, 1, DashStyle.Dash);
        private PixelPoint _moveStart;
        private ScreenshotAnnotation? _moving;
        private ScreenshotAnnotation? _selected;

        public OutputColorMode OutputColorMode
        {
            get => CanOutputHdr ? field : OutputColorMode.Sdr;
            set
            {
                if (field == value) return;
                field = value;
                Changed?.Invoke();
            }
        }

        public bool CanOutputHdr
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                Changed?.Invoke();
            }
        } = true;

        public bool HasSelectedAnnotation => _selected is not null;

        public bool HitTestAnnotation(PixelPoint point)
        {
            return FindAnnotation(point) is not null;
        }

        private ScreenshotAnnotation? FindAnnotation(PixelPoint point)
        {
            if (!_behavior.SecondaryEditingEnabled || !AnnotationsVisible ||
                !AnnotationBounds.Contains(point)) return null;
            var location = new Point(point.X, point.Y);
            return _annotations.LastOrDefault(annotation => (annotation.Tool != ScreenshotTool.Eraser ||
                                                             _behavior.EraserSecondaryEditingEnabled) &&
                                                            HitTest(annotation, location));
        }

        private bool HitTest(ScreenshotAnnotation annotation, Point point)
        {
            if (!AnnotationObjectBounds(annotation).Contains(point)) return false;
            var tolerance = Math.Max(5, annotation.Thickness / 2 + 3);
            if (annotation.Tool == ScreenshotTool.Arrow && !string.IsNullOrWhiteSpace(annotation.Text))
            {
                var comment = FormatLabel(annotation, Brushes.Black);
                if (new Rect(annotation.Points[^1] + new Vector(8, 8), new Size(comment.Width, comment.Height))
                    .Contains(point))
                    return true;
            }

            switch (annotation.Tool)
            {
                case ScreenshotTool.Number:
                case ScreenshotTool.Watermark:
                case ScreenshotTool.Magnifier:
                case ScreenshotTool.Spotlight:
                case ScreenshotTool.Text:
                case ScreenshotTool.Mosaic:
                case ScreenshotTool.Blur:
                    return true;
                case ScreenshotTool.Rectangle:
                    return annotation.Properties.Filled || !annotation.Bounds.Deflate(tolerance).Contains(point);
                case ScreenshotTool.Ellipse:
                    var bounds = annotation.Bounds;
                    var rx = Math.Max(1, bounds.Width / 2);
                    var ry = Math.Max(1, bounds.Height / 2);
                    var x = (point.X - bounds.Center.X) / rx;
                    var y = (point.Y - bounds.Center.Y) / ry;
                    return annotation.Properties.Filled
                        ? x * x + y * y <= 1
                        : Math.Abs(Math.Sqrt(x * x + y * y) - 1) * Math.Min(rx, ry) <= tolerance;
                case ScreenshotTool.Select:
                    return false;
                case ScreenshotTool.StrokeEraser:
                case ScreenshotTool.Eraser:
                case ScreenshotTool.Line:
                case ScreenshotTool.Polyline:
                case ScreenshotTool.Arrow:
                case ScreenshotTool.Brush:
                case ScreenshotTool.Highlight:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(annotation));
            }

            if (annotation is { Tool: ScreenshotTool.Arrow, Points.Count: > 2 })
            {
                var start = annotation.Points[0];
                var control = annotation.Points[1];
                var end = annotation.Points[^1];
                var steps = Math.Clamp((int)Math.Ceiling((Distance(start, control) + Distance(control, end)) / 4), 8,
                    256);
                var previous = start;
                for (var step = 1; step <= steps; step++)
                {
                    var fraction = (double)step / steps;
                    var remaining = 1 - fraction;
                    var current = new Point(
                        remaining * remaining * start.X + 2 * remaining * fraction * control.X +
                        fraction * fraction * end.X,
                        remaining * remaining * start.Y + 2 * remaining * fraction * control.Y +
                        fraction * fraction * end.Y);
                    if (SegmentDistance(point, previous, current) <= tolerance) return true;
                    previous = current;
                }

                return false;
            }

            if (annotation.Points.Count == 1) return Distance(annotation.Points[0], point) <= tolerance;
            for (var index = 1; index < annotation.Points.Count; index++)
            {
                var start = annotation.Points[index - 1];
                var end = annotation.Points[index];
                if (SegmentDistance(point, start, end) <= tolerance) return true;
            }

            return false;
        }

        private static double SegmentDistance(Point point, Point start, Point end)
        {
            var vector = end - start;
            var lengthSquared = vector.X * vector.X + vector.Y * vector.Y;
            var fraction = lengthSquared == 0
                ? 0
                : Math.Clamp(((point.X - start.X) * vector.X + (point.Y - start.Y) * vector.Y) / lengthSquared, 0, 1);
            return Distance(point, start + vector * fraction);
        }

        public bool BeginObjectMove(PixelPoint point)
        {
            if (IsBusy || !CanAnnotate) return false;
            var annotation = HitTestSelectedControl(point) ? _selected : FindAnnotation(point);
            _selected = annotation;
            if (annotation is null)
            {
                Changed?.Invoke();
                return false;
            }

            Properties = annotation.Properties;
            Color = annotation.Color;
            Thickness = annotation.Thickness;
            FontSize = annotation.FontSize;
            MosaicSize = annotation.MosaicSize;
            _moving = annotation;
            _resizeCorner = -1;
            if (ShowControlPoints(annotation))
            {
                var corners = ControlCorners(annotation.Bounds);
                for (var index = 0; index < corners.Length; index++)
                    if (Distance(corners[index], new(point.X, point.Y)) <= 8)
                        _resizeCorner = index;
            }

            if (annotation.Tool == ScreenshotTool.Arrow && _behavior.ArrowCurveControlPoints &&
                Distance(ArrowControlPoint(annotation), new(point.X, point.Y)) <= 8) _resizeCorner = -2;
            _moveStart = point;
            Changed?.Invoke();
            return true;
        }

        public void MoveObject(PixelPoint point)
        {
            if (_moving is not { } original) return;
            var bounds = AnnotationObjectBounds(original);
            switch (_resizeCorner)
            {
                case -2:
                    BendArrow(original, point);
                    return;
                case >= 0:
                    ResizeObject(original, point);
                    return;
            }

            var dx = Math.Clamp(point.X - _moveStart.X, Math.Min(0, AnnotationBounds.X - bounds.Left),
                Math.Max(0, AnnotationBounds.Right - bounds.Right));
            var dy = Math.Clamp(point.Y - _moveStart.Y, Math.Min(0, AnnotationBounds.Y - bounds.Top),
                Math.Max(0, AnnotationBounds.Bottom - bounds.Bottom));
            var anchoredWatermark = original is { Tool: ScreenshotTool.Watermark, Properties.NineGrid: true };
            var replacement = ReferenceEquals(_selected, original)
                ? CloneAnnotation(original, properties: anchoredWatermark
                    ? original.Properties with { NineGrid = false }
                    : original.Properties)
                : _selected!;
            var index = _annotations.IndexOf(_selected!);
            if (index < 0) return;
            for (var pointIndex = 0; pointIndex < original.Points.Count; pointIndex++)
                replacement.Points[pointIndex] =
                    new(original.Points[pointIndex].X + dx + (anchoredWatermark ? bounds.X - original.Points[0].X : 0),
                        original.Points[pointIndex].Y + dy + (anchoredWatermark ? bounds.Y - original.Points[0].Y : 0));
            _annotations[index] = replacement;
            _selected = replacement;
            Changed?.Invoke();
        }

        public async Task EndObjectMoveAsync()
        {
            if (_moving is not { } original || _selected is not { } replacement) return;
            var index = _annotations.IndexOf(replacement);
            _moving = null;
            _resizeCorner = -1;
            if (ReferenceEquals(original, replacement))
            {
                Changed?.Invoke();
                return;
            }

            _annotations[index] = original;
            _selected = original;
            if (original.Points.SequenceEqual(replacement.Points))
            {
                replacement.Dispose();
                Changed?.Invoke();
                return;
            }

            await ReplaceAnnotationAsync(index, replacement);
        }

        private void CancelMove()
        {
            if (_moving is not { } original) return;
            var index = _annotations.IndexOf(_selected!);
            if (index >= 0) _annotations[index] = original;
            if (!ReferenceEquals(_selected, original)) _selected?.Dispose();
            _selected = original;
            _moving = null;
            _resizeCorner = -1;
        }

        public void ClearAnnotationSelection()
        {
            CommitTextEdit();
            CancelGesture();
            _selected = null;
            Changed?.Invoke();
        }

        public void DeleteSelectedAnnotation()
        {
            CommitTextEdit();
            if (IsBusy || IsDrawing || _selected is null) return;
            SaveHistory();
            var removedNumber = _selected.Tool == ScreenshotTool.Number ? _selected.Properties.Number : (int?)null;
            _annotations.Remove(_selected);
            if (_behavior.RenumberOnDelete && removedNumber is { } number) RenumberAfterDelete(number);
            _selected = null;
            Changed?.Invoke();
        }

        public async Task MoveSelectedAnnotationAsync(int dx, int dy)
        {
            if (IsBusy || IsDrawing || _selected is not { } annotation) return;
            var first = annotation.Points[0];
            _moving = annotation;
            _moveStart = new((int)first.X, (int)first.Y);
            MoveObject(new(_moveStart.X + dx, _moveStart.Y + dy));
            await EndObjectMoveAsync();
        }

        private async Task ReplaceAnnotationAsync(int index, ScreenshotAnnotation replacement)
        {
            try
            {
                if (replacement.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.Magnifier
                    or ScreenshotTool.Eraser)
                {
                    IsBusy = true;
                    Changed?.Invoke();
                    using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
                    _gestureWork = work;
                    try
                    {
                        replacement.ImageTiles =
                            await CreateImageEffectAsync(replacement, work.Token);
                        work.Token.ThrowIfCancellationRequested();
                    }
                    finally
                    {
                        _gestureWork = null;
                    }
                }

                if (_disposed)
                {
                    replacement.Dispose();
                    return;
                }

                SaveHistory();
                _retained.Add(replacement);
                _annotations[index] = replacement;
                _selected = replacement;
                Error = null;
            }
            catch (OperationCanceledException)
            {
                replacement.Dispose();
            }
            catch (Exception error)
            {
                replacement.Dispose();
                Error = error.Message;
            }
            finally
            {
                IsBusy = false;
                Changed?.Invoke();
            }
        }

        private static ScreenshotAnnotation CloneAnnotation(ScreenshotAnnotation source, double dx = 0, double dy = 0,
            string? text = null, Color? color = null, double? fontSize = null, double? thickness = null,
            int? mosaicSize = null, ScreenshotAnnotationProperties? properties = null,
            ScreenshotAnnotationImage? watermarkImage = null,
            bool replaceImage = false)
        {
            var copy = new ScreenshotAnnotation
            {
                Tool = source.Tool, Color = color ?? source.Color, Thickness = thickness ?? source.Thickness,
                Text = text ?? source.Text, FontSize = fontSize ?? source.FontSize,
                MosaicSize = mosaicSize ?? source.MosaicSize, Properties = properties ?? source.Properties,
                WatermarkImage = (replaceImage ? watermarkImage : source.WatermarkImage)?.Retain(),
            };
            copy.Points.AddRange(source.Points.Select(point => new Point(point.X + dx, point.Y + dy)));
            return copy;
        }

        private void SaveHistory()
        {
            _undo.Push([.. _annotations]);
            _redo.Clear();
            if (_undo.Count > 128)
            {
                var recent = _undo.Take(128).Reverse().ToArray();
                _undo.Clear();
                foreach (var state in recent) _undo.Push(state);
            }

            var used = _undo.Concat(_redo).SelectMany(state => state).Concat(_annotations).ToHashSet();
            foreach (var annotation in _retained.Where(annotation => !used.Contains(annotation)).ToArray())
            {
                annotation.Dispose();
                _retained.Remove(annotation);
            }
        }

        private void RestoreHistory(Stack<ScreenshotAnnotation[]> from, Stack<ScreenshotAnnotation[]> to)
        {
            if (!from.TryPop(out var state)) return;
            to.Push([.. _annotations]);
            _annotations.Clear();
            _annotations.AddRange(state);
            _selected = null;
            SynchronizeNumberCounter();
            Changed?.Invoke();
        }

        public void RenderSelection(DrawingContext context)
        {
            if (IsEditingText) return;
            RenderPrivacyCandidates(context);
            if (_selected is null || IsBusy || !AnnotationsVisible) return;
            using var clip = context.PushClip(new(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width,
                AnnotationBounds.Height));
            context.DrawRectangle(null, SelectedAnnotationPen, AnnotationObjectBounds(_selected));
            if (ShowControlPoints(_selected))
                foreach (var point in ControlCorners(_selected.Bounds))
                    context.DrawRectangle(Brushes.White, SelectedAnnotationPen, new(point.X - 3, point.Y - 3, 6, 6));
            if (_selected.Tool == ScreenshotTool.Arrow && _behavior is
                    { ArrowCurveControlPoints: true, SecondaryEditingEnabled: true })
                context.DrawEllipse(Brushes.White, SelectedAnnotationPen, ArrowControlPoint(_selected), 4, 4);
        }
    }
}
