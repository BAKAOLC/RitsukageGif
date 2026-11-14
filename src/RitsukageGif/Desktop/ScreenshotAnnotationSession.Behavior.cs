using Avalonia;
using Avalonia.Input;
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
                PinnedLastTool = preferences.RememberPinnedTool ? captured.LastTool : preferences.PinnedLastTool,
            };
        }

        public void MoveWithModifiers(PixelPoint point, bool shift)
        {
            _shiftPressed = shift;
            Move(point);
        }

        private Task AdjustToolWheelAsync(int delta, KeyModifiers modifiers)
        {
            if (_behavior.DisableWheelToolAdjustment || delta == 0 || IsBusy || IsDrawing) return Task.CompletedTask;
            if (_hoverPoint is { } pointer && FindAnnotation(pointer) is { Tool: ScreenshotTool.Magnifier } hovered)
            {
                if (!ReferenceEquals(_selected, hovered))
                {
                    _selected = hovered;
                    LoadObjectStyle(hovered);
                }

                return UpdateSelectedPropertiesAsync(Properties with
                {
                    Magnification = Math.Clamp(Properties.Magnification + Math.Sign(delta) * .1, 1, 8),
                });
            }

            var automatic = (_selected is { AutoMosaicId: var id } && id != Guid.Empty) ||
                            Tool == ScreenshotTool.AutoMosaic;
            var tool = automatic
                ? Properties.MosaicMode == AnnotationMosaicMode.Blur ? ScreenshotTool.Blur : ScreenshotTool.Mosaic
                : _selected?.Tool ?? Tool;
            var step = Math.Sign(delta);
            return tool switch
            {
                ScreenshotTool.Select => Task.CompletedTask,
                ScreenshotTool.Spotlight => modifiers == KeyModifiers.None
                    ? UpdateSelectedPropertiesAsync(Properties with
                    {
                        Opacity = Math.Clamp(Properties.Opacity + step * .05, .01, 1),
                    })
                    : Task.CompletedTask,
                ScreenshotTool.Text =>
                    UpdateSelectedStyleAsync(fontSize: Math.Clamp(FontSize + step * 2, 1, 1024)),
                ScreenshotTool.Watermark =>
                    UpdateSelectedStyleAsync(fontSize: Math.Clamp(FontSize + step * 2, 8, 80)),
                ScreenshotTool.Number =>
                    UpdateSelectedStyleAsync(thickness: Math.Clamp(Thickness + step, 1, MaximumBrushThickness)),
                ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase or ScreenshotTool.Eraser
                    when !automatic && Properties.PixelPath == AnnotationPixelPath.Brush =>
                    UpdateSelectedStyleAsync(thickness: Math.Clamp(Thickness + step, 1, MaximumBrushThickness)),
                ScreenshotTool.Mosaic => UpdateSelectedStyleAsync(mosaicSize: Math.Clamp(MosaicSize + step, 2, 28)),
                ScreenshotTool.Blur => UpdateSelectedPropertiesAsync(Properties with
                {
                    BlurRadius = Math.Clamp(Properties.BlurRadius + step, 3, 50),
                }),
                ScreenshotTool.SmartErase => Task.CompletedTask,
                _ => UpdateSelectedStyleAsync(thickness: Math.Clamp(Thickness + step, 1,
                    tool == ScreenshotTool.Brush ? MaximumBrushThickness : 1024)),
            };
        }

        public Task AdjustToolWheelWithModifiersAsync(int delta, KeyModifiers modifiers)
        {
            if (_behavior.DisableWheelToolAdjustment || delta == 0 || IsBusy || IsDrawing)
                return Task.CompletedTask;
            var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ??
                          (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
            if (_selected?.Tool == ScreenshotTool.Number && modifiers.HasFlag(command))
            {
                var alpha = Math.Clamp(Math.Round(Color.A * 100d / 255, MidpointRounding.AwayFromZero) +
                                       Math.Sign(delta) * 5, 10, 100);
                return UpdateSelectedStyleAsync(Color.FromArgb(
                    (byte)Math.Round(alpha * 255 / 100, MidpointRounding.AwayFromZero), Color.R, Color.G, Color.B));
            }

            return AdjustToolWheelAsync(delta, modifiers);
        }

        public void NotifyHover(PixelPoint? point)
        {
            _hoverPoint = point;
        }

        public bool TryBeginArrowComment(PixelPoint point)
        {
            if (!_behavior.ArrowDoubleClickCommentEnabled || IsBusy || IsDrawing)
                return false;
            if (FindAnnotation(point) is not { Tool: ScreenshotTool.Arrow } arrow) return false;
            _selected = arrow;
            Text = arrow.Text;
            Changed?.Invoke();
            ArrowCommentRequested?.Invoke();
            return true;
        }

        public void RenderPencilCursor(DrawingContext context, Point point, double scale = 1)
        {
            if (PointerCursor != StandardCursorType.None) return;
            var pen = new Pen(Brushes.White, scale);
            if (PencilCursor is AnnotationPencilCursor.Ring or AnnotationPencilCursor.CrosshairRing)
                context.DrawEllipse(null, pen, point, Thickness / 2, Thickness / 2);
            if (PencilCursor is AnnotationPencilCursor.Crosshair or AnnotationPencilCursor.CrosshairRing)
            {
                context.DrawLine(pen, point - new Vector(5 * scale, 0), point + new Vector(5 * scale, 0));
                context.DrawLine(pen, point - new Vector(0, 5 * scale), point + new Vector(0, 5 * scale));
            }
        }

        private bool ShowControlPoints(ScreenshotAnnotation annotation)
        {
            return CanEditControlPoints(annotation) &&
                   (_behavior.ControlPointVisibility == AnnotationControlPointVisibility.Always ||
                    (_hoverPoint is { } hover && AnnotationObjectBounds(annotation).Inflate(ControlHoverMargin)
                        .Contains(new Point(hover.X, hover.Y))));
        }

        private bool CanEditControlPoints(ScreenshotAnnotation annotation)
        {
            return CanEditAnnotation(annotation, ReferenceEquals(annotation, _selected)) &&
                   _behavior.ControlPointVisibility != AnnotationControlPointVisibility.Hidden &&
                   (annotation.Tool is ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Arrow
                        or ScreenshotTool.Line or ScreenshotTool.Highlight or ScreenshotTool.Magnifier ||
                    (IsPixelTool(annotation.Tool) && annotation.Properties.PixelPath == AnnotationPixelPath.Rectangle));
        }

        private bool HitTestSelectedControl(PixelPoint point)
        {
            if (_selected is not { } annotation || !CanEditAnnotation(annotation, true) || !AnnotationsVisible)
                return false;
            var location = new Point(point.X, point.Y);
            return (CanEditPolylineNodes(annotation) &&
                    annotation.Points.Any(node => Distance(node, location) <= ControlHitRadius)) ||
                   (CanEditNumberTarget(annotation) && Distance(annotation.Points[1], location) <= ControlHitRadius) ||
                   (CanEditControlPoints(annotation) && HitTestObjectControl(annotation, location)) ||
                   (annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line && annotation.Properties.Curved &&
                    CanEditControlPoints(annotation) &&
                    _behavior.ArrowCurveControlPoints &&
                    Distance(ArrowControlPoint(annotation), location) <= ControlHitRadius);
        }

        private bool ShowPolylineNodes(ScreenshotAnnotation annotation)
        {
            return CanEditPolylineNodes(annotation) &&
                   (_behavior.ControlPointVisibility == AnnotationControlPointVisibility.Always ||
                    (_hoverPoint is { } hover && AnnotationObjectBounds(annotation).Inflate(ControlHoverMargin)
                        .Contains(new Point(hover.X, hover.Y))));
        }

        private bool CanEditPolylineNodes(ScreenshotAnnotation annotation)
        {
            return annotation.Tool == ScreenshotTool.Polyline &&
                   CanEditAnnotation(annotation, ReferenceEquals(annotation, _selected)) &&
                   _behavior.ControlPointVisibility != AnnotationControlPointVisibility.Hidden;
        }

        private void ResizeObject(ScreenshotAnnotation original, PixelPoint point)
        {
            if (original.Tool == ScreenshotTool.Magnifier)
            {
                ResizeMagnifier(original, point);
                return;
            }

            if (original.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line || IsMarkerLine(original))
            {
                MoveLineEndpoint(original, point);
                return;
            }

            var anchor = ObjectControlPoint(original, (_resizeCorner + 2) % 4);
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
            var replacement = ReferenceEquals(_selected, original)
                ? CloneAnnotation(original, properties: original.Properties with { Curved = true })
                : _selected!;
            var index = _annotations.IndexOf(_selected!);
            if (index < 0) return;
            if (replacement.Points.Count == 2) replacement.Points.Insert(1, ArrowControlPoint(original));
            replacement.Points[1] = new(Math.Clamp(point.X, AnnotationBounds.X, AnnotationBounds.Right),
                Math.Clamp(point.Y, AnnotationBounds.Y, AnnotationBounds.Bottom));
            _annotations[index] = replacement;
            _selected = replacement;
            Changed?.Invoke();
        }

        private void RenumberAfterDelete(int removed, AnnotationNumberFormat format)
        {
            for (var index = 0; index < _annotations.Count; index++)
            {
                var annotation = _annotations[index];
                if (annotation.Tool != ScreenshotTool.Number || annotation.Properties.Number <= removed ||
                    (!_behavior.GlobalNumberCounter && annotation.Properties.NumberFormat != format)) continue;
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
            var format = StyleFor(ScreenshotTool.Number).Properties.NumberFormat;
            var next = Math.Min(999999, _annotations.Where(annotation => annotation.Tool == ScreenshotTool.Number &&
                                                                         annotation.Properties.NumberFormat == format)
                .Select(annotation => annotation.Properties.Number)
                .DefaultIfEmpty(_behavior.NumberStartsAtZero ? -1 : 0).Max() + 1);
            _nextNumber = next;
            if (Tool == ScreenshotTool.Number && _selected is not { Tool: ScreenshotTool.Number })
                Properties = Properties with { Number = next };
            var style = StyleFor(ScreenshotTool.Number);
            _toolStyles[nameof(ScreenshotTool.Number)] =
                style with { Properties = style.Properties with { Number = next } };
        }
    }
}
