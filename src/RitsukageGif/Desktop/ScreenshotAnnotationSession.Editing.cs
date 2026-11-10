using System.Collections.Immutable;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static readonly IPen SelectedAnnotationPen = new Pen(Brushes.DeepSkyBlue, 1, DashStyle.Dash);
        private PixelPoint _moveStart;
        private ScreenshotAnnotation? _moving;
        private int _polylineNode = -1;
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

        internal void ClearSelectedAnnotation()
        {
            _selected = null;
            Changed?.Invoke();
        }

        public bool HitTestAnnotation(PixelPoint point, bool currentToolOnly = false)
        {
            return FindAnnotation(point, currentToolOnly) is not null;
        }

        private ScreenshotAnnotation? FindAnnotation(PixelPoint point, bool currentToolOnly = false)
        {
            if (!AnnotationsVisible || (!AllowUnrestrictedSelection && Tool == ScreenshotTool.Select) ||
                !AnnotationBounds.Contains(point)) return null;
            var location = new Point(point.X, point.Y);
            return _annotations.LastOrDefault(annotation =>
                CanEditAnnotation(annotation, ReferenceEquals(annotation, _selected)) &&
                (!currentToolOnly || Tool == ScreenshotTool.Select ||
                 ScreenshotToolCatalog.Get(AnnotationTool(annotation)).ObjectKind ==
                 ScreenshotToolCatalog.Get(Tool).ObjectKind) &&
                (annotation.Tool != ScreenshotTool.Eraser ||
                 _behavior.EraserSecondaryEditingEnabled) &&
                HitTest(annotation, location));
        }

        private bool HitTest(ScreenshotAnnotation annotation, Point point)
        {
            if (!AnnotationObjectBounds(annotation).Inflate(ControlHitRadius).Contains(point)) return false;
            var tolerance = Math.Max(5 * InteractionScale, annotation.Thickness / 2 + 3 * InteractionScale);
            if (annotation.Tool == ScreenshotTool.Arrow && !string.IsNullOrWhiteSpace(annotation.Text))
            {
                var comment = FormatLabel(annotation, Brushes.Black);
                if (new Rect(annotation.Points[^1] + new Vector(8, 8), new Size(comment.Width, comment.Height))
                    .Contains(point))
                    return true;
            }

            switch (annotation.Tool)
            {
                case ScreenshotTool.Highlight:
                    return MarkerContains(annotation, point);
                case ScreenshotTool.Number:
                    return NumberHitTest(annotation, point);
                case ScreenshotTool.Magnifier:
                    return MagnifierContains(annotation, annotation.Bounds, point) ||
                           MagnifierContains(annotation, MagnifierLensBounds(annotation), point);
                case ScreenshotTool.Watermark:
                case ScreenshotTool.Spotlight:
                case ScreenshotTool.Text:
                    return true;
                case ScreenshotTool.Mosaic:
                case ScreenshotTool.Blur:
                case ScreenshotTool.SmartErase:
                case ScreenshotTool.Eraser:
                    return PixelGeometryFor(annotation).FillContains(point);
                case ScreenshotTool.Rectangle:
                case ScreenshotTool.Ellipse:
                    var rectangle = GeometryFor(annotation).Path;
                    return (annotation.Properties.Filled && rectangle.FillContains(point)) ||
                           rectangle.StrokeContains(new Pen(Brushes.Black, tolerance * 2), point);
                case ScreenshotTool.Select:
                    return false;
                case ScreenshotTool.StrokeEraser:
                case ScreenshotTool.Line:
                case ScreenshotTool.Polyline:
                case ScreenshotTool.Arrow:
                case ScreenshotTool.Brush:
                    break;
                case ScreenshotTool.AutoMosaic:
                default:
                    throw new ArgumentOutOfRangeException(nameof(annotation));
            }

            switch (annotation)
            {
                case { Tool: ScreenshotTool.Arrow or ScreenshotTool.Line or ScreenshotTool.Polyline }:
                {
                    var geometry = GeometryFor(annotation);
                    var hitPen = new Pen(Brushes.Black, tolerance * 2);
                    return (geometry.Shaft ?? geometry.Path).StrokeContains(hitPen, point) ||
                           (geometry.PathFilled && geometry.Path.FillContains(point)) ||
                           (annotation is
                            {
                                Tool: ScreenshotTool.Line or ScreenshotTool.Polyline,
                                Properties.StrokeStyle: AnnotationStrokeStyle.Filled,
                            } &&
                            geometry.Path.FillContains(point)) ||
                           (geometry.StartHead is { } start &&
                            (start.StrokeContains(hitPen, point) ||
                             (geometry.StartFilled && start.FillContains(point)))) ||
                           (geometry.EndHead is { } end &&
                            (end.StrokeContains(hitPen, point) || (geometry.EndFilled && end.FillContains(point))));
                }
                case { Tool: ScreenshotTool.Brush, Points.Count: > 1 }:
                    return BrushGeometryFor(annotation).Path
                        .StrokeContains(new Pen(Brushes.Black, tolerance * 2), point);
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

        public bool BeginObjectMove(PixelPoint point, bool currentToolOnly = false)
        {
            if (IsBusy || !CanAnnotate || IsDrawing ||
                (currentToolOnly && IsCreationOnlyTool())) return false;
            var annotation = FindMoveAnnotation(point, currentToolOnly);
            _selected = annotation;
            if (annotation is null)
            {
                Changed?.Invoke();
                return false;
            }

            LoadObjectStyle(annotation);
            _moving = annotation;
            _movingMagnifierLens = annotation.Tool == ScreenshotTool.Magnifier &&
                                   !MagnifierContains(annotation, annotation.Bounds, new(point.X, point.Y));
            _resizeCorner = -1;
            _polylineNode = -1;
            if (CanEditNumberTarget(annotation) &&
                Distance(annotation.Points[1], new(point.X, point.Y)) <= ControlHitRadius)
                _polylineNode = 1;
            else if (annotation.Tool == ScreenshotTool.Number && HitNumberCaption(annotation, point))
                _polylineNode = 2;
            if (CanEditPolylineNodes(annotation))
                for (var index = 0; index < annotation.Points.Count; index++)
                    if (Distance(annotation.Points[index], new(point.X, point.Y)) <= ControlHitRadius)
                    {
                        _polylineNode = index;
                        break;
                    }

            if (CanEditControlPoints(annotation))
                for (var index = 0; index < ObjectControlPointCount(annotation); index++)
                    if (Distance(ObjectControlPoint(annotation, index), new(point.X, point.Y)) <= ControlHitRadius)
                        _resizeCorner = index;

            if (_resizeCorner < 0 && annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line &&
                annotation.Properties.Curved &&
                CanEditControlPoints(annotation) &&
                _behavior.ArrowCurveControlPoints &&
                Distance(ArrowControlPoint(annotation), new(point.X, point.Y)) <= ControlHitRadius) _resizeCorner = -2;
            _moveStart = point;
            Changed?.Invoke();
            return true;
        }

        private void MoveObject(PixelPoint point)
        {
            if (_moving is not { } original) return;
            if (original.Tool == ScreenshotTool.Number && _polylineNode == 2)
            {
                MoveNumberCaption(original, point);
                return;
            }

            if (_polylineNode >= 0)
            {
                var nodeReplacement = ReferenceEquals(_selected, original) ? CloneAnnotation(original) : _selected!;
                var nodeIndex = _annotations.IndexOf(_selected!);
                if (nodeIndex < 0) return;
                var anchor = original.Points[_polylineNode == 0 ? 1 : _polylineNode - 1];
                nodeReplacement.Points[_polylineNode] = ConstrainSegmentPoint(anchor, point, _shiftPressed);
                _annotations[nodeIndex] = nodeReplacement;
                _selected = nodeReplacement;
                Changed?.Invoke();
                return;
            }

            var bounds = AnnotationObjectBounds(original);
            if (_movingMagnifierLens && _resizeCorner < 0)
            {
                MoveMagnifierLens(original, point);
                return;
            }

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
            _movingMagnifierLens = false;
            _polylineNode = -1;
            _resizeCorner = -1;
            if (ReferenceEquals(original, replacement))
            {
                if (_behavior.SecondaryEditing == AnnotationSecondaryEditingMode.CompletelyDisabled) _selected = null;
                Changed?.Invoke();
                return;
            }

            _annotations[index] = original;
            _selected = original;
            if (original.Points.SequenceEqual(replacement.Points) && original.Properties == replacement.Properties &&
                original.MagnifierOffset == replacement.MagnifierOffset)
            {
                replacement.Dispose();
                if (_behavior.SecondaryEditing == AnnotationSecondaryEditingMode.CompletelyDisabled) _selected = null;
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
            _movingMagnifierLens = false;
            _polylineNode = -1;
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
            if (_behavior.RenumberOnDelete && removedNumber is { } number)
                RenumberAfterDelete(number, _selected.Properties.NumberFormat);
            _selected = null;
            Changed?.Invoke();
        }

        public async Task MoveSelectedAnnotationAsync(int dx, int dy)
        {
            if (IsBusy || IsDrawing || _selected is not { } annotation) return;
            var first = annotation.Points[0];
            _moving = annotation;
            _movingMagnifierLens = false;
            _moveStart = new((int)first.X, (int)first.Y);
            MoveObject(new(_moveStart.X + dx, _moveStart.Y + dy));
            await EndObjectMoveAsync();
        }

        private async Task ReplaceAnnotationAsync(int index, ScreenshotAnnotation replacement)
        {
            try
            {
                if (replacement.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase
                    or ScreenshotTool.Magnifier
                    or ScreenshotTool.Eraser)
                {
                    IsBusy = true;
                    Changed?.Invoke();
                    using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
                    _gestureWork = work;
                    try
                    {
                        replacement.ImageTiles =
                            await CreateImageEffectAsync(replacement, work.Token, index);
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
                _selected = replacement is { Tool: ScreenshotTool.Watermark, WatermarkImage: null } ||
                            _behavior.SecondaryEditing == AnnotationSecondaryEditingMode.CompletelyDisabled
                    ? null
                    : replacement;
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
            bool replaceImage = false, ScreenshotTool? tool = null,
            ImmutableArray<Rect>? autoMosaicRegions = null)
        {
            var copy = new ScreenshotAnnotation
            {
                Tool = tool ?? source.Tool, Color = color ?? source.Color, Thickness = thickness ?? source.Thickness,
                Text = text ?? source.Text, FontSize = fontSize ?? source.FontSize,
                MosaicSize = mosaicSize ?? source.MosaicSize, Properties = properties ?? source.Properties,
                MagnifierOffset = source.MagnifierOffset,
                AutoMosaicId = source.AutoMosaicId,
                AutoMosaicRegions = autoMosaicRegions ?? source.AutoMosaicRegions,
                AutoMosaicCandidates = source.AutoMosaicCandidates,
                AutoMosaicCandidateBounds = source.AutoMosaicCandidateBounds,
                WatermarkImage = (replaceImage ? watermarkImage : source.WatermarkImage)?.Retain(),
                WatermarkTimestamp = source.WatermarkTimestamp,
            };
            copy.Points.AddRange(source.Points.Select(point => new Point(point.X + dx, point.Y + dy)));
            return copy;
        }

        private void SaveHistory()
        {
            _undo.Push([.. _annotations]);
            _redo.Clear();
            DocumentBranchChanged?.Invoke();
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

        public void RenderSelection(DrawingContext context, double scale = 1)
        {
            if (IsEditingText) return;
            RenderPrivacyCandidates(context, scale);
            if (IsPrivacyDetectionActive) return;
            if (_selected is null || IsBusy || !AnnotationsVisible) return;
            var pen = SelectionPen(scale);
            var radius = 3 * scale;
            using var clip = context.PushClip(new Rect(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width,
                AnnotationBounds.Height).Inflate(4 * scale));
            context.DrawRectangle(null, pen,
                _selected.Tool == ScreenshotTool.Magnifier ? _selected.Bounds : AnnotationObjectBounds(_selected));
            if (ShowNumberTarget(_selected))
                context.DrawEllipse(Brushes.White, pen, _selected.Points[1], 4 * scale, 4 * scale);
            if (ShowPolylineNodes(_selected))
                foreach (var point in _selected.Points)
                    context.DrawRectangle(Brushes.White, pen,
                        new(point.X - radius, point.Y - radius, 2 * radius, 2 * radius));
            if (ShowControlPoints(_selected))
                for (var index = 0; index < ObjectControlPointCount(_selected); index++)
                {
                    var point = ObjectControlPoint(_selected, index);
                    context.DrawRectangle(Brushes.White, pen,
                        new(point.X - radius, point.Y - radius, 2 * radius, 2 * radius));
                }

            if (_selected.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line && _selected.Properties.Curved &&
                ShowControlPoints(_selected) && _behavior.ArrowCurveControlPoints)
                context.DrawEllipse(Brushes.White, pen, ArrowControlPoint(_selected), 4 * scale, 4 * scale);
        }
    }
}
