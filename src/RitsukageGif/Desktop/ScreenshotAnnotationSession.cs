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

        private ScreenshotTool _tool;

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
            CanScroll && CanExport && !HasAnnotations;

        private bool CanExport => !IsBusy && !Selection.IsEmpty && !IsDrawing && !IsEditingText;

        public ScreenshotTool Tool
        {
            get => _tool;
            set
            {
                if (!CanUseDrawingTool(value)) return;
                if (_tool == value) return;
                CommitTextEdit();
                if (IsDrawingPolyline) FinishPolyline();
                else CancelGesture();
                _selected = null;
                StoreToolStyle(_styleTool);
                _tool = value;
                RestoreToolStyle(value);
                EnsureManualWatermark();
                Changed?.Invoke();
            }
        }

        public PixelRect Selection { get; private set; }
        public Color Color { get; private set; } = Colors.Red;
        public double Thickness { get; private set; } = 4;
        public double FontSize { get; private set; } = 24;
        private string Text { get; set; } = "";
        public int MosaicSize { get; private set; } = 5;
        public bool IsBusy { get; private set; }

        public bool IsDrawing => PrivacyDetectionPending || _gesture is not null || _gestureWork is not null ||
                                 _moving is not null;

        public bool HasAnnotations => _annotations.Count > 0;

        internal ScreenshotAnnotationSession? Underlay
        {
            get;
            set
            {
                field = value;
                Changed?.Invoke();
            }
        }

        public bool HasMosaicAnnotations => _annotations.Any(annotation => annotation.Tool is ScreenshotTool.Mosaic
            or ScreenshotTool.Blur or ScreenshotTool.SmartErase or
            ScreenshotTool.Magnifier or ScreenshotTool.Eraser);

        public bool CanUndo => _undo.Count > 0 || DocumentCanUndo?.Invoke() == true;
        public bool CanRedo => _redo.Count > 0 || DocumentCanRedo?.Invoke() == true;
        public string? Error { get; private set; }

        public void Dispose()
        {
            if (_disposed) return;
            CancelTextEdit();
            Logger.ZLogDebug(
                $"Annotation session disposed. SessionId={_logSessionId} Annotations={_annotations.Count} RetainedObjects={_retained.Count}");
            _disposed = true;
            _pixelPreviewWork?.Cancel();
            _pixelPreviewFrames = [];
            _pixelPreviewState = [];
            CancelPrivacyDetection();
            CancelMove();
            _cancel.Cancel();
            _cancel.Dispose();
            _gesture?.Dispose();
            DisposeRecordingCanvas();
            foreach (var annotation in _retained) annotation.Dispose();
            _retained.Clear();
            _undo.Clear();
            _annotations.Clear();
            _redo.Clear();
            _watermarkSource?.Dispose();
            _watermarkSource = null;
            _frames.Clear();
            _overlayFrame = null;
            ClearImagePreviewCache();
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

        private void Begin(PixelPoint point)
        {
            if (_disposed || !CanAnnotate || IsBusy || !CanUseDrawingTool(Tool) || Tool == ScreenshotTool.Select ||
                !AnnotationBounds.Contains(point)) return;
            if (Tool switch
                {
                    ScreenshotTool.Watermark => !WatermarkResourceReady || _watermarkSource is null,
                    ScreenshotTool.AutoMosaic => true,
                    ScreenshotTool.Eraser => !AnnotationsVisible || _annotations.Count == 0,
                    ScreenshotTool.Text => string.IsNullOrWhiteSpace(Text),
                    _ => false,
                }) return;

            if (_annotations.Count >= 1024 || _annotations.Concat(_retained).Distinct()
                    .Sum(annotation => annotation.Points.Count) >= 262144)
            {
                SetExportState(false, UiText.Get("ScreenshotAnnotationLimit"));
                return;
            }

            if (IsDrawingPointLine)
            {
                ConfirmPointLine(point, _shiftPressed);
                return;
            }

            if (Tool == ScreenshotTool.Polyline && _gesture is { Tool: ScreenshotTool.Polyline } polyline)
            {
                AppendPolylinePoint(polyline, point);
                return;
            }

            CancelGesture();
            _shiftAnchorIndex = -1;
            if (Tool == ScreenshotTool.StrokeEraser) _eraseOriginal = [.. _annotations];
            _gesture = new()
            {
                Tool = Tool, Color = Color, Thickness = Thickness,
                Text = Tool is ScreenshotTool.Text or ScreenshotTool.Watermark ? Text : "", FontSize = FontSize,
                MosaicSize = Math.Clamp(MosaicSize, 2, 28),
                Properties = Properties.Normalize() with
                {
                    Number = Tool == ScreenshotTool.Number ? NextNumber : Properties.Number,
                },
                WatermarkImage = Tool == ScreenshotTool.Watermark ? _watermarkSource?.Retain() : null,
                WatermarkTimestamp = Tool == ScreenshotTool.Watermark ? DateTimeOffset.Now : default,
            };
            _gesture.Points.Add(new(point.X, point.Y));
            if (Tool == ScreenshotTool.Polyline) _gesture.Points.Add(_gesture.Points[0]);
            if (Tool == ScreenshotTool.StrokeEraser) EraseAt(new(point.X, point.Y));
            if (Tool is ScreenshotTool.Text or ScreenshotTool.Watermark)
            {
                Commit(_gesture);
                _gesture = null;
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
                    MovePolylinePreview(point, _shiftPressed);
                    return;
            }

            if (IsMarkerLine(annotation) || annotation.Tool == ScreenshotTool.Number)
                current = ConstrainSegmentPoint(annotation.Points[0], point, _shiftPressed);

            if (annotation.Tool == ScreenshotTool.Brush || IsPixelBrush(annotation))
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
            if (annotation.Tool == ScreenshotTool.Polyline)
            {
                EndPolylineStroke(annotation);
                return;
            }

            if (IsPointLine(annotation))
            {
                FinishPointLine();
                return;
            }

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
                if (annotation.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase
                    or ScreenshotTool.Magnifier
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
                if (annotation.Tool == ScreenshotTool.Number) CompleteNumber(annotation.Properties.Number);
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
            NormalizeGeometryPoints(annotation);
            SaveHistory();
            _annotations.Add(annotation);
            _retained.Add(annotation);
            _selected = _behavior.SecondaryEditing != AnnotationSecondaryEditingMode.CompletelyDisabled &&
                        CanEditAnnotation(annotation)
                ? annotation
                : null;
            Error = null;
        }

        public void CancelWork()
        {
            CancelTextEdit();
            _sourceWork?.Cancel();
            _watermarkLoadWork?.Cancel();
            _gestureWork?.Cancel();
            CancelGesture();
        }

        public void CancelGesture()
        {
            ReleaseNumberCaptionKey();
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

            if (_undo.Count == 0)
            {
                if (DocumentCanUndo?.Invoke() == true) DocumentUndo?.Invoke();
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
            if (_redo.Count == 0)
            {
                if (DocumentCanRedo?.Invoke() == true) DocumentRedo?.Invoke();
                return;
            }

            if (CanRedo)
                Logger.ZLogDebug($"Annotation redo applied. SessionId={_logSessionId} RedoStates={_redo.Count}");
            RestoreHistory(_redo, _undo);
        }

        public void Render(DrawingContext context)
        {
            if (Selection.IsEmpty || !AnnotationsVisible) return;
            Underlay?.Render(context);
            RefreshPixelPreviews();
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
            if (annotation.FixedStroke is { } stroke)
            {
                using var strokeClip = context.PushClip(stroke.Clip);
                context.DrawGeometry(null, stroke.Pen, stroke.Path);
                return;
            }

            var first = annotation.Points[0];
            var brush = new SolidColorBrush(annotation.Color);
            var pen = new Pen(brush, annotation.Thickness, AnnotationStroke.Dashes(annotation.Properties.StrokeStyle),
                annotation.Tool is ScreenshotTool.Line or ScreenshotTool.Polyline
                    ? _behavior.PolylineCap
                    : PenLineCap.Round,
                annotation.Tool is ScreenshotTool.Line or ScreenshotTool.Polyline
                    ? _behavior.PolylineJoin
                    : PenLineJoin.Round);
            switch (annotation.Tool)
            {
                case ScreenshotTool.Text:
                case ScreenshotTool.Number:
                case ScreenshotTool.Watermark:
                    DrawLabel(context, annotation);
                    break;
                case ScreenshotTool.Spotlight:
                    DrawSpotlightGeometry(context, annotation);
                    break;
                case ScreenshotTool.Eraser:
                    if (RecordingContext) DrawRecordingEraser(context, annotation);
                    else DrawPixelTool(context, annotation);
                    break;
                case ScreenshotTool.StrokeEraser:
                    break;
                case ScreenshotTool.Line:
                case ScreenshotTool.Polyline:
                    DrawLineGeometry(context, annotation, brush, pen);
                    break;
                case ScreenshotTool.Rectangle:
                case ScreenshotTool.Ellipse:
                    context.DrawGeometry(annotation.Properties.Filled ? brush : null, pen,
                        GeometryFor(annotation).Path);
                    break;
                case ScreenshotTool.Magnifier:
                    DrawMagnifier(context, annotation, sampleImages);
                    break;
                case ScreenshotTool.Mosaic:
                case ScreenshotTool.Blur:
                case ScreenshotTool.SmartErase:
                    DrawPixelTool(context, annotation);
                    break;
                case ScreenshotTool.Arrow:
                    DrawArrowShape(context, annotation, brush, pen);
                    if (!string.IsNullOrWhiteSpace(annotation.Text))
                    {
                        using var textOptions = context.PushTextOptions(new()
                            { TextRenderingMode = TextRenderingMode.Antialias });
                        var comment = FormatLabel(annotation, brush);
                        context.DrawText(comment, annotation.Points[^1] + new Vector(8, 8));
                    }

                    break;
                case ScreenshotTool.Highlight:
                    DrawMarker(context, annotation);
                    break;
                case ScreenshotTool.Brush:
                    using (context.PushOpacity(1))
                        if (annotation.Points.Count == 1)
                            context.DrawEllipse(brush, null, first, annotation.Thickness / 2, annotation.Thickness / 2);
                        else
                            context.DrawGeometry(null, pen, BrushGeometryFor(annotation).Path);

                    break;
                case ScreenshotTool.Select:
                    break;
                case ScreenshotTool.AutoMosaic:
                default: throw new ArgumentOutOfRangeException(nameof(annotation));
            }
        }
    }
}
