using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession : IDisposable
    {
        private readonly List<ScreenshotAnnotation> _annotations = [];
        private readonly CancellationTokenSource _cancel = new();
        private readonly List<(PixelRect Region, byte[] Pixels)> _frames = [];
        private readonly Stack<ScreenshotAnnotation[]> _redo = [];
        private readonly HashSet<ScreenshotAnnotation> _retained = [];
        private readonly Stack<ScreenshotAnnotation[]> _undo = [];
        private bool _disposed;
        private ScreenshotAnnotation? _gesture;
        private CancellationTokenSource? _gestureWork;
        private (PixelRect Region, byte[] Pixels)? _overlayFrame;

        public bool CanAnnotate
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                if (!value) CancelWork();
                Changed?.Invoke();
            }
        } = true;

        public bool CanScroll
        {
            get;
            set
            {
                field = value;
                Changed?.Invoke();
            }
        } = true;

        public bool CanStartScrolling =>
            CanScroll && CanExport && !HasAnnotations && OutputColorMode == OutputColorMode.Sdr;

        private bool CanExport => !IsBusy && !Selection.IsEmpty && !IsDrawing && !IsEditingText;

        public ScreenshotTool Tool
        {
            get;
            set
            {
                if (field == value) return;
                CommitTextEdit();
                CancelGesture();
                _selected = null;
                StoreToolStyle(field);
                field = value;
                RestoreToolStyle(value);
                Changed?.Invoke();
            }
        }

        public PixelRect Selection { get; private set; }
        public Color Color { get; private set; } = Colors.Red;
        public double Thickness { get; private set; } = 4;
        public double FontSize { get; private set; } = 24;
        private string Text { get; set; } = "";
        public int MosaicSize { get; private set; } = 12;
        public bool IsBusy { get; private set; }

        public bool IsDrawing => IsPrivacyDetectionActive || _gesture is not null || _gestureWork is not null ||
                                 _moving is not null;

        public bool HasAnnotations => _annotations.Count > 0;

        public bool HasMosaicAnnotations => _annotations.Any(annotation => annotation.Tool is ScreenshotTool.Mosaic
            or ScreenshotTool.Blur or
            ScreenshotTool.Magnifier or ScreenshotTool.Eraser);

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public string? Error { get; private set; }

        public void Dispose()
        {
            if (_disposed) return;
            CancelTextEdit();
            Logger.ZLogDebug(
                $"Annotation session disposed. SessionId={_logSessionId} Annotations={_annotations.Count} RetainedObjects={_retained.Count}");
            _disposed = true;
            CancelPrivacyDetection();
            CancelMove();
            _cancel.Cancel();
            _cancel.Dispose();
            _gesture?.Dispose();
            foreach (var annotation in _retained) annotation.Dispose();
            _retained.Clear();
            _undo.Clear();
            _annotations.Clear();
            _redo.Clear();
            _watermarkSource?.Dispose();
            _watermarkSource = null;
            _frames.Clear();
            _overlayFrame = null;
        }

        public event Action? Changed;
        public event Action<ScreenshotOutputAction>? CompleteRequested;

        public void RegisterFrame(PixelRect region, byte[] pixels)
        {
            if (_frames.Any(frame => frame.Region == region)) return;
            if ((long)region.Width * region.Height * 4 != pixels.Length)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(pixels));
            _frames.Add((region, pixels));
        }

        public void SetExportState(bool busy, string? error = null)
        {
            if (busy) CommitTextEdit();
            IsBusy = busy;
            Error = error;
            Changed?.Invoke();
        }

        public void NotifySelection(PixelRect selected)
        {
            if (selected != Selection) CancelTextEdit();
            if (selected != Selection) CancelPrivacyDetection();
            Selection = selected;
            Changed?.Invoke();
        }

        public void RequestComplete(ScreenshotOutputAction action)
        {
            CommitTextEdit();
            if (!IsBusy && !Selection.IsEmpty && !IsDrawing && CompleteRequested is not null)
            {
                Logger.ZLogInformation(
                    $"Annotation output requested. SessionId={_logSessionId} Action={action.ToString()} Width={Selection.Width} Height={Selection.Height} Annotations={_annotations.Count} ColorMode={OutputColorMode.ToString()}");
                CompleteRequested.Invoke(action);
            }
        }

        public void Begin(PixelPoint point)
        {
            if (_disposed || !CanAnnotate || IsBusy || Tool == ScreenshotTool.Select ||
                !AnnotationBounds.Contains(point)) return;
            if (Tool == ScreenshotTool.Eraser && (!AnnotationsVisible || _annotations.Count == 0)) return;
            if (string.IsNullOrWhiteSpace(Text) && (Tool == ScreenshotTool.Text ||
                                                    (Tool == ScreenshotTool.Watermark && _watermarkSource is null)))
                return;
            if (_annotations.Count >= 1024 || _annotations.Concat(_retained).Distinct()
                    .Sum(annotation => annotation.Points.Count) >= 262144)
            {
                SetExportState(false, UiText.Get("ScreenshotAnnotationLimit"));
                return;
            }

            if (Tool == ScreenshotTool.Polyline && _gesture is { Tool: ScreenshotTool.Polyline } polyline)
            {
                polyline.Points.Add(new(point.X, point.Y));
                Changed?.Invoke();
                return;
            }

            CancelGesture();
            _shiftAnchorIndex = -1;
            if (Tool == ScreenshotTool.StrokeEraser) _eraseOriginal = [.. _annotations];
            _gesture = new()
            {
                Tool = Tool, Color = Color, Thickness = Thickness,
                Text = Tool is ScreenshotTool.Text or ScreenshotTool.Watermark ? Text : "", FontSize = FontSize,
                MosaicSize = Math.Clamp(MosaicSize, 2, 256), Properties =
                    Tool == ScreenshotTool.Number && _behavior.GlobalNumberCounter
                        ? Properties.Normalize() with
                        {
                            Number = Math.Max(0,
                                Interlocked.Increment(ref _globalNumber) - (_behavior.NumberStartsAtZero ? 1 : 0)),
                        }
                        : Properties.Normalize(),
                WatermarkImage = Tool == ScreenshotTool.Watermark ? _watermarkSource?.Retain() : null,
            };
            _gesture.Points.Add(new(point.X, point.Y));
            if (Tool == ScreenshotTool.StrokeEraser) EraseAt(new(point.X, point.Y));
            if (Tool is ScreenshotTool.Text or ScreenshotTool.Number or ScreenshotTool.Watermark)
            {
                Commit(_gesture);
                _gesture = null;
                if (Tool == ScreenshotTool.Number)
                    Properties = Properties with { Number = Math.Min(999999, Properties.Number + 1) };
            }

            Changed?.Invoke();
        }

        private void Move(PixelPoint point)
        {
            if (_gesture is not { } annotation) return;
            var current = new Point(Math.Clamp(point.X, AnnotationBounds.X, AnnotationBounds.Right),
                Math.Clamp(point.Y, AnnotationBounds.Y, AnnotationBounds.Bottom));
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (annotation.Tool)
            {
                case ScreenshotTool.StrokeEraser:
                    var previous = annotation.Points[^1];
                    var vector = new Vector(current.X - previous.X, current.Y - previous.Y);
                    var steps = Math.Max(1, (int)Math.Ceiling(vector.Length / 4));
                    for (var step = 1; step <= steps; step++) EraseAt(previous + vector * ((double)step / steps));
                    annotation.Points[^1] = current;
                    Changed?.Invoke();
                    return;
                case ScreenshotTool.Polyline:
                    return;
            }

            if (annotation.Tool is ScreenshotTool.Brush or ScreenshotTool.Highlight or ScreenshotTool.Eraser)
            {
                if (annotation.Tool == ScreenshotTool.Brush && _shiftPressed)
                {
                    if (_shiftAnchorIndex < 0) _shiftAnchorIndex = annotation.Points.Count - 1;
                    var anchor = annotation.Points[_shiftAnchorIndex];
                    if (_behavior.PencilShiftMode == AnnotationPencilShiftMode.SnapAngleLine)
                    {
                        var vector = new Vector(current.X - anchor.X, current.Y - anchor.Y);
                        var angle = Math.Round(Math.Atan2(vector.Y, vector.X) / (Math.PI / 4)) * Math.PI / 4;
                        current = anchor + new Vector(Math.Cos(angle), Math.Sin(angle)) * vector.Length;
                    }

                    if (annotation.Points.Count > _shiftAnchorIndex + 1)
                        annotation.Points.RemoveRange(_shiftAnchorIndex + 1,
                            annotation.Points.Count - _shiftAnchorIndex - 1);
                    annotation.Points.Add(current);
                    Changed?.Invoke();
                    return;
                }

                _shiftAnchorIndex = -1;
                if (annotation.Points.Count >= 65536 || Distance(current, annotation.Points[^1]) < .75) return;
                annotation.Points.Add(current);
            }
            else if (annotation.Points.Count == 1)
            {
                annotation.Points.Add(current);
            }
            else
            {
                annotation.Points[^1] = current;
            }

            Changed?.Invoke();
        }

        public async Task EndAsync(PixelPoint point)
        {
            if (_gesture is not { } annotation) return;
            Move(point);
            if (annotation.Tool == ScreenshotTool.Polyline) return;
            _gesture = null;
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (annotation.Tool)
            {
                case ScreenshotTool.StrokeEraser:
                    FinishErase();
                    annotation.Dispose();
                    return;
                case ScreenshotTool.Eraser when !HasErasableOverlap(annotation):
                    annotation.Dispose();
                    Changed?.Invoke();
                    return;
            }

            try
            {
                if (annotation.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.Magnifier
                    or ScreenshotTool.Eraser)
                {
                    IsBusy = true;
                    Changed?.Invoke();
                    using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
                    _gestureWork = work;
                    try
                    {
                        annotation.ImageTiles =
                            await CreateImageEffectAsync(annotation, work.Token);
                        work.Token.ThrowIfCancellationRequested();
                    }
                    finally
                    {
                        _gestureWork = null;
                    }

                    if (_disposed)
                    {
                        annotation.Dispose();
                        return;
                    }
                }

                Commit(annotation);
            }
            catch (OperationCanceledException)
            {
                annotation.Dispose();
            }
            catch (Exception error)
            {
                annotation.Dispose();
                Error = error.Message;
            }
            finally
            {
                IsBusy = false;
                Changed?.Invoke();
            }
        }

        private void Commit(ScreenshotAnnotation annotation)
        {
            SaveHistory();
            _annotations.Add(annotation);
            _retained.Add(annotation);
            _selected = _behavior.SecondaryEditingEnabled ? annotation : null;
            Error = null;
        }

        public void CancelWork()
        {
            CancelTextEdit();
            _sourceWork?.Cancel();
            _gestureWork?.Cancel();
            CancelGesture();
        }

        public void CancelGesture()
        {
            CancelPrivacyDetection();
            CancelErase();
            _gesture?.Dispose();
            _gesture = null;
            CancelMove();
            Changed?.Invoke();
        }

        public void Undo()
        {
            if (IsBusy) return;
            CommitTextEdit();
            if (IsDrawing)
            {
                CancelGesture();
                return;
            }

            if (CanUndo)
                Logger.ZLogDebug($"Annotation undo applied. SessionId={_logSessionId} UndoStates={_undo.Count}");
            RestoreHistory(_undo, _redo);
        }

        public void Redo()
        {
            if (IsBusy) return;
            CommitTextEdit();
            CancelGesture();
            if (CanRedo)
                Logger.ZLogDebug($"Annotation redo applied. SessionId={_logSessionId} RedoStates={_redo.Count}");
            RestoreHistory(_redo, _undo);
        }

        public void Render(DrawingContext context)
        {
            if (Selection.IsEmpty || !AnnotationsVisible) return;
            using var clip = context.PushClip(new(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width,
                AnnotationBounds.Height));
            foreach (var annotation in _annotations) Draw(context, annotation);
            if (_gesture is { } current) Draw(context, current);
        }

        private static double Distance(Point first, Point second)
        {
            return Math.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));
        }

        private void Draw(DrawingContext context, ScreenshotAnnotation annotation, bool sampleImages = true)
        {
            if (annotation.Points.Count == 0) return;
            var first = annotation.Points[0];
            var last = annotation.Points[^1];
            var brush = new SolidColorBrush(annotation.Color);
            var pen = new Pen(brush, annotation.Thickness, annotation.Properties.Dashed ? DashStyle.Dash : null,
                annotation.Tool == ScreenshotTool.Polyline ? _behavior.PolylineCap : PenLineCap.Round,
                annotation.Tool == ScreenshotTool.Polyline ? _behavior.PolylineJoin : PenLineJoin.Round);
            switch (annotation.Tool)
            {
                case ScreenshotTool.Text:
                case ScreenshotTool.Number:
                case ScreenshotTool.Watermark:
                    DrawLabel(context, annotation);
                    break;
                case ScreenshotTool.Spotlight:
                    DrawSpotlight(context, annotation);
                    break;
                case ScreenshotTool.Eraser:
                    DrawPixelEraser(context, annotation);
                    break;
                case ScreenshotTool.StrokeEraser:
                    break;
                case ScreenshotTool.Line:
                    context.DrawLine(pen, first, last);
                    break;
                case ScreenshotTool.Polyline:
                    var path = new StreamGeometry();
                    using (var line = path.Open())
                    {
                        line.BeginFigure(first, false);
                        for (var index = 1; index < annotation.Points.Count; index++)
                            line.LineTo(annotation.Points[index]);
                        line.EndFigure(false);
                    }

                    context.DrawGeometry(null, pen, path);
                    break;
                case ScreenshotTool.Rectangle:
                    context.DrawRectangle(annotation.Properties.Filled ? brush : null, pen, annotation.Bounds); break;
                case ScreenshotTool.Ellipse:
                    context.DrawEllipse(annotation.Properties.Filled ? brush : null, pen, annotation.Bounds); break;
                case ScreenshotTool.Magnifier:
                    using (context.PushClip(annotation.Bounds))
                    {
                        var center = annotation.Bounds.Center;
                        var scale = annotation.Properties.Magnification;
                        using var magnify = context.PushTransform(new(scale, 0, 0, scale,
                            center.X * (1 - scale), center.Y * (1 - scale)));
                        if (sampleImages)
                            foreach (var tile in annotation.ImageTiles)
                                context.DrawImage(tile.Image, tile.Bounds);
                    }

                    context.DrawRectangle(null, pen, annotation.Bounds);
                    break;
                case ScreenshotTool.Mosaic:
                case ScreenshotTool.Blur:
                    if (annotation.ImageTiles.Count > 0)
                        foreach (var tile in annotation.ImageTiles)
                            context.DrawImage(tile.Image, tile.Bounds);
                    else
                        context.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, 1, DashStyle.Dash), annotation.Bounds);
                    break;
                case ScreenshotTool.Arrow:
                    if (annotation.Points.Count > 2)
                    {
                        var arrow = new StreamGeometry();
                        using (var curve = arrow.Open())
                        {
                            curve.BeginFigure(first, false);
                            curve.QuadraticBezierTo(annotation.Points[1], last);
                            curve.EndFigure(false);
                        }

                        context.DrawGeometry(null, pen, arrow);
                    }
                    else
                    {
                        context.DrawLine(pen, first, last);
                    }

                    var tangent = annotation.Points.Count > 2 ? annotation.Points[1] : first;
                    var direction = new Vector(last.X - tangent.X, last.Y - tangent.Y);
                    if (direction.Length > .1)
                    {
                        var unit = direction / direction.Length;
                        var wing = Math.Max(10, annotation.Thickness * 3);
                        var perpendicular = new Vector(-unit.Y, unit.X);
                        context.DrawLine(pen, last, last - unit * wing + perpendicular * wing * .5);
                        context.DrawLine(pen, last, last - unit * wing - perpendicular * wing * .5);
                    }

                    if (!string.IsNullOrWhiteSpace(annotation.Text))
                    {
                        var comment = FormatLabel(annotation, brush);
                        context.DrawText(comment, last + new Vector(8, 8));
                    }

                    break;
                case ScreenshotTool.Brush:
                case ScreenshotTool.Highlight:
                    using (context.PushOpacity(annotation.Tool == ScreenshotTool.Highlight ? .35 : 1))
                    {
                        if (annotation.Points.Count == 1)
                            context.DrawEllipse(brush, null, first, annotation.Thickness / 2, annotation.Thickness / 2);
                        if (annotation.Tool == ScreenshotTool.Brush && _behavior.SmoothBrush &&
                            annotation.Points.Count > 2)
                            DrawSmoothStroke(context, annotation, pen);
                        else
                            for (var i = 1; i < annotation.Points.Count; i++)
                                context.DrawLine(pen, annotation.Points[i - 1], annotation.Points[i]);
                    }

                    break;
                case ScreenshotTool.Select:
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(annotation));
            }
        }
    }
}
