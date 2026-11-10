using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSurface : Canvas, IDisposable, ICustomHitTest,
        IPinnedInputCoverageSource
    {
        private static readonly Dictionary<StandardCursorType, Cursor> AnnotationCursors =
            new()
            {
                [StandardCursorType.Arrow] = new(StandardCursorType.Arrow),
                [StandardCursorType.Cross] = new(StandardCursorType.Cross),
                [StandardCursorType.None] = new(StandardCursorType.None),
                [StandardCursorType.SizeAll] = new(StandardCursorType.SizeAll),
                [StandardCursorType.SizeNorthSouth] = new(StandardCursorType.SizeNorthSouth),
                [StandardCursorType.TopLeftCorner] = new(StandardCursorType.TopLeftCorner),
                [StandardCursorType.TopRightCorner] = new(StandardCursorType.TopRightCorner),
            };

        private readonly ScreenshotAnnotationDrawingSurface _drawingSurface;
        private readonly ScreenshotAnnotationSession _session;
        private IPointer? _captured;
        private bool _completing;
        private PixelPoint? _pointerPosition;

        public ScreenshotAnnotationSurface(ScreenshotAnnotationSession session, PixelRect monitor, double scale,
            Func<PointerEventArgs, PixelPoint> sourcePoint, SelectionCoordinator? coordinator = null)
        {
            _session = session;
            _drawingSurface = new(RenderAnnotations) { IsHitTestVisible = false };
            Scale = scale;
            SourceOrigin = new(monitor.X, monitor.Y);
            Children.Add(_drawingSurface);
            RefreshCursor();
            _session.Changed += Refresh;
            _session.TextEditStateChanged += RefreshTextEditor;
            Refresh();
            ConfigureMouseInput(args =>
            {
                _session.InteractionScale = EffectiveControlScale;
                return sourcePoint(args);
            }, coordinator);
            RefreshTextEditor();
            PointerExited += (_, _) =>
            {
                _pointerPosition = null;
                _session.NotifyHover(null);
                RefreshCursor();
                InvalidateAnnotations();
            };
            PointerCaptureLost += (_, _) =>
            {
                _captured = null;
                _mouseButtonsPressed = false;
                _mouseChordCancelled = false;
                coordinator?.SetMouseInputHeld(false);
                _mouseGesture.Reset();
                _mouseSelecting = false;
                if (!_completing) _session.CancelGesture();
            };
        }

        public double Scale
        {
            get;
            set
            {
                if (field.Equals(value)) return;
                field = value;
                _session.InteractionScale = EffectiveControlScale;
                InvalidateAnnotations();
                RefreshTextEditor();
            }
        }

        public double? ControlScale
        {
            get;
            set
            {
                if (field.Equals(value)) return;
                field = value;
                _session.InteractionScale = EffectiveControlScale;
                InvalidateAnnotations();
            }
        }

        public PixelPoint SourceOrigin
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                InvalidateAnnotations();
                RefreshTextEditor();
            }
        }

        private double EffectiveControlScale => ControlScale ?? Scale;
        private double DragThreshold => 4 * EffectiveControlScale / Scale;

        public bool RenderContent
        {
            get;
            set
            {
                if (field.Equals(value)) return;
                field = value;
                InvalidateAnnotations();
            }
        } = true;

        public bool HitTest(Point point)
        {
            _session.InteractionScale = EffectiveControlScale;
            return _session.Tool != ScreenshotTool.Select || _session.HasSelectedAnnotation ||
                   _session.HitTestAnnotation(new((int)(point.X * Scale + SourceOrigin.X),
                       (int)(point.Y * Scale + SourceOrigin.Y)));
        }

        public void Dispose()
        {
            _session.TextEditStateChanged -= RefreshTextEditor;
            if (_inlineTextEditor is not null) _session.CancelTextEdit();
            Children.Clear();
            _inlineTextEditor = null;
            _session.Changed -= Refresh;
            _mouseClicks.Dispose();
            _captured?.Capture(null);
            _captured = null;
        }

        public event Action? InputCoverageChanged;

        private void Refresh()
        {
            IsHitTestVisible = _session.CanAnnotate;
            RefreshCursor();
            InvalidateAnnotations();
        }

        private void RefreshCursor()
        {
            Cursor = AnnotationCursors[_session.PointerCursor];
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var result = base.ArrangeOverride(finalSize);
            _drawingSurface.Arrange(new(result));
            RefreshTextEditor();
            return result;
        }

        private void InvalidateAnnotations()
        {
            _drawingSurface.InvalidateVisual();
            InvalidateVisual();
            InputCoverageChanged?.Invoke();
        }

        private void RenderAnnotations(DrawingContext context)
        {
            using var clip = context.PushClip(new(Bounds.Size));
            using var transform =
                context.PushTransform(new(1 / Scale, 0, 0, 1 / Scale, -SourceOrigin.X / Scale,
                    -SourceOrigin.Y / Scale));
            if (RenderContent) _session.Render(context);
            if (!_session.CanAnnotate) return;
            _session.RenderSelection(context, EffectiveControlScale);
            if (_pointerPosition is { } point)
                _session.RenderPencilCursor(context, new(point.X, point.Y), EffectiveControlScale);
        }
    }
}
