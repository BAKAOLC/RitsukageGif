using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSurface : Canvas, IDisposable, ICustomHitTest
    {
        private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
        private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
        private static readonly Cursor PencilCursor = new(StandardCursorType.None);
        private readonly ScreenshotAnnotationDrawingSurface _drawingSurface;
        private readonly PixelRect _monitor;
        private readonly ScreenshotAnnotationSession _session;
        private IPointer? _captured;
        private bool _completing;
        private PixelPoint? _pointerPosition;

        public ScreenshotAnnotationSurface(ScreenshotAnnotationSession session, PixelRect monitor, double scale,
            Func<PointerEventArgs, PixelPoint> sourcePoint, SelectionCoordinator? coordinator = null)
        {
            _session = session;
            _monitor = monitor;
            Scale = scale;
            _drawingSurface = new(RenderAnnotations) { IsHitTestVisible = false };
            Children.Add(_drawingSurface);
            Cursor = CrossCursor;
            _session.Changed += Refresh;
            _session.TextEditStateChanged += RefreshTextEditor;
            Refresh();
            ConfigureMouseInput(sourcePoint, coordinator);
            RefreshTextEditor();
            PointerExited += (_, _) =>
            {
                _pointerPosition = null;
                _session.NotifyHover(null);
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

        public double Scale { get; set; }
        public bool RenderContent { get; set; } = true;

        public bool HitTest(Point point)
        {
            return _session.Tool != ScreenshotTool.Select || _session.HasSelectedAnnotation ||
                   _session.HitTestAnnotation(new((int)(point.X * Scale + _monitor.X),
                       (int)(point.Y * Scale + _monitor.Y)));
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

        private void Refresh()
        {
            IsHitTestVisible = _session.CanAnnotate;
            Cursor = _session.Tool switch
            {
                ScreenshotTool.Select => ArrowCursor,
                ScreenshotTool.Brush or ScreenshotTool.Highlight or ScreenshotTool.Eraser => PencilCursor,
                _ => CrossCursor,
            };
            InvalidateAnnotations();
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var result = base.ArrangeOverride(finalSize);
            _drawingSurface.Arrange(new(result));
            return result;
        }

        private void InvalidateAnnotations()
        {
            _drawingSurface.InvalidateVisual();
            InvalidateVisual();
        }

        private void RenderAnnotations(DrawingContext context)
        {
            using var clip = context.PushClip(new(Bounds.Size));
            using var transform =
                context.PushTransform(new(1 / Scale, 0, 0, 1 / Scale, -_monitor.X / Scale, -_monitor.Y / Scale));
            if (RenderContent) _session.Render(context);
            _session.RenderSelection(context);
            if (_pointerPosition is { } point) _session.RenderPencilCursor(context, new(point.X, point.Y));
        }
    }
}
