using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageEditorWindow
    {
        private static readonly IBrush StripFill = new SolidColorBrush(Color.FromArgb(70, 255, 95, 95));
        private static readonly IBrush StripOutline = new SolidColorBrush(Color.FromRgb(255, 95, 95));
        private static readonly Cursor StripArrow = new(StandardCursorType.Arrow);
        private static readonly Cursor StripVertical = new(StandardCursorType.SizeNorthSouth);
        private static readonly Cursor StripHorizontal = new(StandardCursorType.SizeWestEast);
        private readonly Button _cropStrip = new() { Padding = new(8, 4), IsVisible = false, Focusable = false };
        private readonly ImageEditorStripSelection _strip = new();
        private readonly Canvas _stripHud = new();
        private Button? _rowsButton, _columnsButton, _topmostButton;
        private Pen? _stripPen;
        private double _stripPenScale;
        private IPointer? _stripPointer;
        private ImageEditorStripSurface? _stripSurface;
        private bool IsStripEditing => _strip.Axis is not null;
        private bool RequiresSdrEditorPreview => Session.IsEditingText || IsStripEditing;

        private void InitializeStripEditing()
        {
            _cropStrip.Bind(ContentProperty, LiveText.Resource("ImageEditorStripCrop"));
            _cropStrip.Click += (_, _) => _ = RunAsync(ApplyStripAsync);
            _stripHud.Children.Add(_cropStrip);
            Grid.SetRow(_stripHud, 1);
            _editorRoot.Children.Add(_stripHud);
            AddHandler(PointerPressedEvent, StripPressed, RoutingStrategies.Tunnel);
            AddHandler(PointerMovedEvent, StripMoved, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, StripReleased, RoutingStrategies.Tunnel);
            AttachStripSurface();
            AddDocumentButtons();
        }

        private void AttachStripSurface()
        {
            _stripSurface = new(RenderStrip)
            {
                Width = _sourceBounds.Width, Height = _sourceBounds.Height, IsHitTestVisible = IsStripEditing,
            };
            _stripSurface.PointerCaptureLost += (_, _) =>
            {
                _stripPointer = null;
                _strip.FinishDrag();
                if (!_closed) PositionStripButton();
            };
            _imageLayer.Children.Add(_stripSurface);
        }

        private void AddDocumentButtons()
        {
            _rowsButton = _toolbar.AddDocumentAction("ImageEditorDeleteRows", () => ToggleStrip(ImageStripAxis.Rows));
            _columnsButton =
                _toolbar.AddDocumentAction("ImageEditorDeleteColumns", () => ToggleStrip(ImageStripAxis.Columns));
            _topmostButton = _toolbar.AddDocumentAction("PinnedImageTopmost", () =>
            {
                ApplyTopmostPreference(!Topmost);
                TopmostChanged?.Invoke(Topmost);
            }, "ScreenshotPin");
            RefreshDocumentButtons();
        }

        internal void ApplyTopmostPreference(bool topmost)
        {
            Topmost = topmost;
            RefreshDocumentButtons();
        }

        private void RefreshDocumentButtons()
        {
            if (_rowsButton is not { } rows || _columnsButton is not { } columns ||
                _topmostButton is not { } topmost) return;
            rows.IsEnabled = columns.IsEnabled = !_outputBusy && !Session.IsBusy && !_documentChanging;
            SetDocumentButtonState(rows, _strip.Axis == ImageStripAxis.Rows);
            SetDocumentButtonState(columns, _strip.Axis == ImageStripAxis.Columns);
            SetDocumentButtonState(topmost, Topmost);
        }

        private static void SetDocumentButtonState(Button button, bool active)
        {
            button.Background = active ? AppTheme.Accent : Brushes.Transparent;
            button.Foreground = active ? Brushes.White : AppTheme.Ink;
            button.Classes.Set("active-tool", active);
            button.Classes.Set("primary", active);
        }

        private void ToggleStrip(ImageStripAxis axis)
        {
            if (_outputBusy || Session.IsBusy || _documentChanging) return;
            SetStripMode(_strip.Axis == axis ? null : axis);
        }

        private void SetStripMode(ImageStripAxis? axis)
        {
            _stripPointer?.Capture(null);
            _stripPointer = null;
            if (axis is not null) Session.Tool = ScreenshotTool.Select;
            _strip.Activate(axis);
            if (_stripSurface is { } surface)
            {
                surface.IsHitTestVisible = axis is not null;
                surface.Cursor = StripArrow;
                surface.InvalidateVisual();
            }

            _cropStrip.IsVisible = false;
            _surface.IsHitTestVisible = axis is null && Session.Tool != ScreenshotTool.Select && Session.CanAnnotate;
            RefreshDocumentButtons();
            QueueHdrPreview();
        }

        private bool IsViewportEvent(PointerEventArgs args)
        {
            return args.Source is Visual source &&
                   !source.GetVisualAncestors().Any(ancestor => ancestor is ScrollBar) &&
                   (ReferenceEquals(source, _viewport) || source.GetVisualAncestors().Contains(_viewport));
        }

        private void StripPressed(object? sender, PointerPressedEventArgs args)
        {
            if (!IsStripEditing || _documentChanging || _outputBusy || !IsViewportEvent(args)) return;
            var properties = args.GetCurrentPoint(this).Properties;
            if (properties.IsRightButtonPressed)
            {
                args.Handled = true;
                SetStripMode(null);
                return;
            }

            if (!properties.IsLeftButtonPressed) return;
            if (args.ClickCount == 2)
            {
                args.Handled = true;
                return;
            }

            var point = args.GetPosition(_imageLayer);
            if (!new Rect(new(_sourceBounds.Width, _sourceBounds.Height)).Contains(point)) return;
            args.Handled = true;
            _strip.Begin(point, _sourceBounds, RenderScaling / _viewport.Zoom);
            _stripPointer = args.Pointer;
            args.Pointer.Capture(_stripSurface);
            _cropStrip.IsVisible = false;
            _stripSurface?.InvalidateVisual();
        }

        private void StripMoved(object? sender, PointerEventArgs args)
        {
            if (!IsStripEditing || _documentChanging || !IsViewportEvent(args)) return;
            var point = args.GetPosition(_imageLayer);
            if (_stripPointer is not null)
            {
                args.Handled = true;
                _strip.Move(point, _sourceBounds);
                _stripSurface?.InvalidateVisual();
            }

            if (_stripSurface is { } surface)
                surface.Cursor = _strip.CursorAt(point, RenderScaling / _viewport.Zoom) switch
                {
                    StandardCursorType.SizeNorthSouth => StripVertical,
                    StandardCursorType.SizeWestEast => StripHorizontal,
                    _ => StripArrow,
                };
        }

        private void StripReleased(object? sender, PointerReleasedEventArgs args)
        {
            if (_stripPointer is null || args.InitialPressMouseButton != MouseButton.Left) return;
            args.Handled = true;
            _strip.EndDrag(args.GetPosition(_imageLayer), _sourceBounds);
            var pointer = _stripPointer;
            _stripPointer = null;
            pointer.Capture(null);
            _stripSurface?.InvalidateVisual();
            PositionStripButton();
        }

        private void RenderStrip(DrawingContext context)
        {
            if (!IsStripEditing || !_strip.HasSelection) return;
            var scale = RenderScaling / _viewport.Zoom;
            if (_stripPen is null || !_stripPenScale.Equals(scale))
            {
                _stripPenScale = scale;
                _stripPen = new(StripOutline, scale, DashStyle.Dash);
            }

            context.DrawRectangle(StripFill, _stripPen, _strip.Bounds(_sourceBounds));
        }

        private void PositionStripButton()
        {
            if (_strip.Axis is not { } axis || !_strip.HasSelection || _strip.IsDragging || _documentChanging)
            {
                _cropStrip.IsVisible = false;
                return;
            }

            var band = _strip.Bounds(_sourceBounds);
            var a = _imageLayer.TranslatePoint(band.TopLeft, _stripHud);
            var b = _imageLayer.TranslatePoint(band.BottomRight, _stripHud);
            var viewport = new Rect(_stripHud.Bounds.Size);
            if (a is null || b is null || viewport.Width <= 0 || viewport.Height <= 0) return;
            var visualBand = new Rect(a.Value, b.Value);
            _cropStrip.IsVisible = visualBand.Intersects(viewport);
            if (!_cropStrip.IsVisible) return;
            _cropStrip.Measure(Size.Infinity);
            var placed = ImageEditorStripPlacement.Place(visualBand, viewport, _cropStrip.DesiredSize, axis);
            Canvas.SetLeft(_cropStrip, placed.X);
            Canvas.SetTop(_cropStrip, placed.Y);
            var extent = axis == ImageStripAxis.Rows ? _sourceBounds.Height : _sourceBounds.Width;
            _cropStrip.IsEnabled = _strip.PixelCount > 0 && _strip.PixelCount < extent && !_outputBusy;
        }
    }
}
