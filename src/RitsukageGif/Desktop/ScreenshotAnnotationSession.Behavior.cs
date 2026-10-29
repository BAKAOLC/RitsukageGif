using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static int _globalNumber;
        private AnnotationPreferences _behavior = new();
        private PixelPoint? _hoverPoint;
        private int _resizeCorner = -1;
        private int _shiftAnchorIndex = -1;
        private bool _shiftPressed;
        private bool QuickSettingsEnabled => _behavior.QuickSettingsEnabled;
        private double QuickSettingsPadding => _behavior.QuickSettingsPadding;
        public double MaximumBrushThickness => _behavior.MaximumBrushThickness;
        private AnnotationPencilCursor PencilCursor => _behavior.PencilCursor;
        public IReadOnlyList<Color> ColorPalette { get; private set; } = [];
        public Func<PixelPoint, PixelRect?>? QuickMarkBoundsProvider { get; set; }
        public event Action? ArrowCommentRequested;

        public void ApplyPinnedPreferences(AnnotationPreferences preferences)
        {
            ApplyPreferencesCore(preferences with
            {
                RememberLastTool = preferences.RememberPinnedTool, LastTool = preferences.PinnedLastTool,
            });
        }

        public async Task<bool> TryCreateQuickMarkAsync(PixelPoint point)
        {
            if (_disposed || !CanAnnotate || IsBusy || IsDrawing || !QuickSettingsEnabled ||
                Tool is not (ScreenshotTool.Rectangle or ScreenshotTool.Ellipse) ||
                !Selection.Contains(point) || QuickMarkBoundsProvider?.Invoke(point) is not { IsEmpty: false } detected)
                return false;
            var left = Math.Max(Selection.X, Math.Floor(detected.X - QuickSettingsPadding));
            var top = Math.Max(Selection.Y, Math.Floor(detected.Y - QuickSettingsPadding));
            var right = Math.Min(Selection.Right, Math.Ceiling(detected.Right + QuickSettingsPadding));
            var bottom = Math.Min(Selection.Bottom, Math.Ceiling(detected.Bottom + QuickSettingsPadding));
            if (right <= left || bottom <= top) return false;
            Begin(new((int)left, (int)top));
            if (_gesture is null) return false;
            await EndAsync(new((int)right, (int)bottom));
            return true;
        }

        public AnnotationPreferences CapturePinnedPreferences(AnnotationPreferences preferences)
        {
            var captured = CapturePreferences(preferences with { RememberLastTool = preferences.RememberPinnedTool });
            return captured with
            {
                LastTool = preferences.LastTool, RememberLastTool = preferences.RememberLastTool,
                PinnedLastTool = preferences.RememberPinnedTool ? Tool.ToString() : preferences.PinnedLastTool,
            };
        }

        public void MoveWithModifiers(PixelPoint point, bool shift)
        {
            _shiftPressed = shift;
            Move(point);
        }

        public Task AdjustToolWheelAsync(int delta)
        {
            if (_behavior.DisableWheelToolAdjustment || delta == 0 || IsBusy || IsDrawing) return Task.CompletedTask;
            var tool = _selected?.Tool ?? Tool;
            var step = Math.Sign(delta);
            return tool switch
            {
                ScreenshotTool.Select => Task.CompletedTask,
                ScreenshotTool.Text or ScreenshotTool.Number or ScreenshotTool.Watermark =>
                    UpdateSelectedStyleAsync(fontSize: Math.Clamp(FontSize + step * 2, 1, 1024)),
                ScreenshotTool.Mosaic => UpdateSelectedStyleAsync(mosaicSize: Math.Clamp(MosaicSize + step, 2, 256)),
                ScreenshotTool.Blur => UpdateSelectedPropertiesAsync(Properties with
                {
                    BlurRadius = Math.Clamp(Properties.BlurRadius + step, 1, 256),
                }),
                _ => UpdateSelectedStyleAsync(thickness: Math.Clamp(Thickness + step, 1,
                    tool == ScreenshotTool.Brush ? MaximumBrushThickness : 1024)),
            };
        }

        public void NotifyHover(PixelPoint? point)
        {
            _hoverPoint = point;
        }

        public bool TryBeginArrowComment(PixelPoint point)
        {
            if (!_behavior.ArrowDoubleClickCommentEnabled || !_behavior.SecondaryEditingEnabled || IsBusy || IsDrawing)
                return false;
            if (FindAnnotation(point) is not { Tool: ScreenshotTool.Arrow } arrow) return false;
            _selected = arrow;
            Text = arrow.Text;
            Changed?.Invoke();
            ArrowCommentRequested?.Invoke();
            return true;
        }

        public void RenderPencilCursor(DrawingContext context, Point point)
        {
            if (Tool is not (ScreenshotTool.Brush or ScreenshotTool.Highlight or ScreenshotTool.Eraser)) return;
            var pen = new Pen(Brushes.White);
            if (PencilCursor is AnnotationPencilCursor.Ring or AnnotationPencilCursor.CrosshairRing)
                context.DrawEllipse(null, pen, point, Thickness / 2, Thickness / 2);
            if (PencilCursor is AnnotationPencilCursor.Crosshair or AnnotationPencilCursor.CrosshairRing)
            {
                context.DrawLine(pen, point - new Vector(5, 0), point + new Vector(5, 0));
                context.DrawLine(pen, point - new Vector(0, 5), point + new Vector(0, 5));
            }
        }

        private bool ShowControlPoints(ScreenshotAnnotation annotation)
        {
            if (!_behavior.SecondaryEditingEnabled ||
                _behavior.ControlPointVisibility == AnnotationControlPointVisibility.Hidden)
                return false;
            if (annotation.Tool is not (ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Arrow))
                return false;
            return _behavior.ControlPointVisibility == AnnotationControlPointVisibility.Always ||
                   (_hoverPoint is { } hover && AnnotationObjectBounds(annotation).Inflate(10)
                       .Contains(new Point(hover.X, hover.Y)));
        }

        private static Point[] ControlCorners(Rect bounds)
        {
            return [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];
        }

        private bool HitTestSelectedControl(PixelPoint point)
        {
            if (_selected is not { } annotation || !_behavior.SecondaryEditingEnabled || !AnnotationsVisible)
                return false;
            var location = new Point(point.X, point.Y);
            return (ShowControlPoints(annotation) &&
                    ControlCorners(annotation.Bounds).Any(corner => Distance(corner, location) <= 8)) ||
                   (annotation.Tool == ScreenshotTool.Arrow && _behavior.ArrowCurveControlPoints &&
                    Distance(ArrowControlPoint(annotation), location) <= 8);
        }

        private void ResizeObject(ScreenshotAnnotation original, PixelPoint point)
        {
            var anchor = ControlCorners(original.Bounds)[(_resizeCorner + 2) % 4];
            var current = new Point(Math.Clamp(point.X, AnnotationBounds.X, AnnotationBounds.Right),
                Math.Clamp(point.Y, AnnotationBounds.Y, AnnotationBounds.Bottom));
            var replacement = ReferenceEquals(_selected, original) ? CloneAnnotation(original) : _selected!;
            var index = _annotations.IndexOf(_selected!);
            if (index < 0 || replacement.Points.Count < 2) return;
            replacement.Points[0] = anchor;
            replacement.Points[^1] = current;
            _annotations[index] = replacement;
            _selected = replacement;
            Changed?.Invoke();
        }

        private static Point ArrowControlPoint(ScreenshotAnnotation annotation)
        {
            return annotation.Points.Count > 2
                ? annotation.Points[1]
                : new((annotation.Points[0].X + annotation.Points[^1].X) / 2,
                    (annotation.Points[0].Y + annotation.Points[^1].Y) / 2);
        }

        private void BendArrow(ScreenshotAnnotation original, PixelPoint point)
        {
            var replacement = ReferenceEquals(_selected, original) ? CloneAnnotation(original) : _selected!;
            var index = _annotations.IndexOf(_selected!);
            if (index < 0) return;
            if (replacement.Points.Count == 2) replacement.Points.Insert(1, ArrowControlPoint(original));
            replacement.Points[1] = new(Math.Clamp(point.X, AnnotationBounds.X, AnnotationBounds.Right),
                Math.Clamp(point.Y, AnnotationBounds.Y, AnnotationBounds.Bottom));
            _annotations[index] = replacement;
            _selected = replacement;
            Changed?.Invoke();
        }

        private void RenumberAfterDelete(int removed)
        {
            for (var index = 0; index < _annotations.Count; index++)
            {
                var annotation = _annotations[index];
                if (annotation.Tool != ScreenshotTool.Number || annotation.Properties.Number <= removed) continue;
                var replacement = CloneAnnotation(annotation, properties: annotation.Properties with
                {
                    Number = annotation.Properties.Number - 1,
                });
                _annotations[index] = replacement;
                _retained.Add(replacement);
            }

            SynchronizeNumberCounter();
        }

        private void SynchronizeNumberCounter()
        {
            if (_behavior.GlobalNumberCounter) return;
            var next = Math.Min(999999, _annotations.Where(annotation => annotation.Tool == ScreenshotTool.Number)
                .Select(annotation => annotation.Properties.Number)
                .DefaultIfEmpty(_behavior.NumberStartsAtZero ? -1 : 0).Max() + 1);
            if (Tool == ScreenshotTool.Number) Properties = Properties with { Number = next };
            var style = _toolStyles.GetValueOrDefault(nameof(ScreenshotTool.Number), _defaultStyle);
            _toolStyles[nameof(ScreenshotTool.Number)] =
                style with { Properties = style.Properties with { Number = next } };
        }

        private static void DrawSmoothStroke(DrawingContext context, ScreenshotAnnotation annotation, IPen pen)
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(annotation.Points[0], false);
                for (var index = 0; index < annotation.Points.Count - 1; index++)
                {
                    var previous = annotation.Points[Math.Max(0, index - 1)];
                    var start = annotation.Points[index];
                    var end = annotation.Points[index + 1];
                    var next = annotation.Points[Math.Min(annotation.Points.Count - 1, index + 2)];
                    path.CubicBezierTo(start + new Vector(end.X - previous.X, end.Y - previous.Y) / 6,
                        end - new Vector(next.X - start.X, next.Y - start.Y) / 6, end);
                }

                path.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }
}
