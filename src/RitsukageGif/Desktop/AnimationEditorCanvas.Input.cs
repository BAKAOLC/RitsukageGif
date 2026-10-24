using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorCanvas
    {
        private bool _dragging;
        private bool _panning;
        private Point _start;
        private Point _last;
        private Point _screenLast;
        private IPointer? _pointer;
        private AnimationEditorDraftState? _gestureState;
        private Vector _gesturePan;
        private Rect? _gestureSelection;
        public bool HasOverlay => _layers.Count > 0;

        public byte[] ExportOverlayPng()
        {
            if (_bitmap is null) throw new InvalidOperationException();
            using var target = new RenderTargetBitmap(new PixelSize(_width, _height), new Vector(96, 96));
            using (var context = target.CreateDrawingContext())
                foreach (var layer in _layers)
                {
                    using var opacity = context.PushOpacity(layer.Opacity);
                    if (layer.Image is { } image) context.DrawImage(image, layer.Bounds);
                    else if (layer.Formatted is { } text) context.DrawText(text, layer.Bounds.TopLeft);
                }

            using var stream = new MemoryStream();
            target.Save(stream);
            return stream.ToArray();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_bitmap is null) return;
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed)
            {
                CancelGesture();
                Selection = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed) return;
            Focus();
            _panning = Tool == AnimationEditorTool.Pan || point.Properties.IsMiddleButtonPressed;
            _start = _last = ToImage(point.Position);
            _screenLast = point.Position;
            _gesturePan = _pan;
            _gestureSelection = Selection;
            _resizeCorner = !_panning && Tool == AnimationEditorTool.Select ? HitCorner(_start) : -1;
            if (!_panning && _resizeCorner < 0 && Tool != AnimationEditorTool.Select &&
                !new Rect(0, 0, _width, _height).Contains(_start)) return;
            _dragging = true;
            _pointer = e.Pointer;
            e.Pointer.Capture(this);
            if (!_panning)
            {
                if (Tool is not (AnimationEditorTool.Selection or AnimationEditorTool.Select))
                {
                    _gestureState = Snapshot();
                }

                if (Tool == AnimationEditorTool.Select)
                {
                    if (_resizeCorner < 0) _selected = _layers.LastOrDefault(layer => layer.Bounds.Contains(_start));
                    _layerStartBounds = _selected?.Bounds ?? default;
                    _layerChanged = false;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
                else if (Tool is AnimationEditorTool.Brush or AnimationEditorTool.Eraser)
                {
                    Stroke(_start, _start, Tool == AnimationEditorTool.Eraser);
                    Upload();
                }
                else if (Tool == AnimationEditorTool.Selection) Selection = null;
            }

            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var screen = e.GetPosition(this);
            var next = ToImage(screen);
            if (!_dragging)
            {
                UpdatePointerCursor(next);
                return;
            }

            if (_panning)
            {
                _pan += screen - _screenLast;
            }
            else if (Tool == AnimationEditorTool.Select)
            {
                UpdateLayerGesture(next, e.KeyModifiers);
            }
            else if (Tool is AnimationEditorTool.Brush or AnimationEditorTool.Eraser)
            {
                Stroke(_last, next, Tool == AnimationEditorTool.Eraser);
                Upload();
            }
            else if (Tool == AnimationEditorTool.Selection)
            {
                Selection = Region(_start, next).Intersect(new Rect(0, 0, _width, _height));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            _last = next;
            _screenLast = screen;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_dragging) return;
            if (!_panning)
            {
                if (Tool == AnimationEditorTool.Select && _layerChanged && _selected is { } layer)
                {
                    var finalBounds = layer.Bounds;
                    layer.Bounds = _layerStartBounds;
                    Remember();
                    layer.Bounds = finalBounds;
                    Changed();
                }
                else if (Tool is AnimationEditorTool.Line or AnimationEditorTool.Rectangle
                         or AnimationEditorTool.Ellipse)
                {
                    if (Tool == AnimationEditorTool.Line ||
                        Math.Abs(_start.X - _last.X) >= .01 && Math.Abs(_start.Y - _last.Y) >= .01)
                    {
                        RasterShape(_start, _last);
                        Remember(_gestureState);
                        HasRasterChanges = true;
                        Upload();
                        Changed();
                    }
                }
                else if (Tool is AnimationEditorTool.Brush or AnimationEditorTool.Eraser)
                {
                    Remember(_gestureState);
                    HasRasterChanges = true;
                    Changed();
                }
            }

            _gestureState = null;
            _layerChanged = false;
            _resizeCorner = -1;
            _dragging = false;
            _pointer?.Capture(null);
            _pointer = null;
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            CancelGesture();
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            CancelGesture();
            var location = e.GetPosition(this);
            var anchor = ToImage(location);
            Zoom = EffectiveZoom * Math.Pow(1.15, e.Delta.Y);
            var origin = Origin;
            _pan += location - new Point(origin.X + anchor.X * EffectiveZoom, origin.Y + anchor.Y * EffectiveZoom);
            InvalidateVisual();
            e.Handled = true;
        }

        private void CancelGesture()
        {
            if (_dragging && _panning) _pan = _gesturePan;
            if (_dragging && !_panning && Tool == AnimationEditorTool.Selection)
            {
                Selection = _gestureSelection;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            if (_dragging && !_panning && Tool == AnimationEditorTool.Select && _selected is { } layer)
            {
                layer.Bounds = _layerStartBounds;
                _layerChanged = false;
                _resizeCorner = -1;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }

            _dragging = false;
            if (_gestureState is { } before)
            {
                _gestureState = null;
                Restore(before);
            }

            var pointer = _pointer;
            _pointer = null;
            pointer?.Capture(null);
            InvalidateVisual();
        }

        protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            CancelGesture();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                CancelGesture();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !_dragging)
            {
                DeleteSelected();
                e.Handled = true;
            }
        }

        private static Rect Region(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y));

        private void DrawShape(DrawingContext context, Point start, Point end)
        {
            var brush = new SolidColorBrush(BrushColor);
            var pen = new Pen(brush, Math.Clamp(BrushSize, 1, 1024));
            if (Tool == AnimationEditorTool.Line) context.DrawLine(pen, start, end);
            else if (Tool == AnimationEditorTool.Rectangle)
                context.DrawRectangle(Filled ? brush : null, pen, Region(start, end));
            else context.DrawEllipse(Filled ? brush : null, pen, Region(start, end));
        }

        private void Stroke(Point start, Point end, bool erase)
        {
            if (_pixels is null) return;
            var radius = Math.Clamp(double.IsFinite(BrushSize) ? BrushSize / 2 : 2, .5, 512);
            var left = (int)Math.Clamp(Math.Floor(Math.Min(start.X, end.X) - radius), 0, _width);
            var right = (int)Math.Clamp(Math.Ceiling(Math.Max(start.X, end.X) + radius), 0, _width);
            var top = (int)Math.Clamp(Math.Floor(Math.Min(start.Y, end.Y) - radius), 0, _height);
            var bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(start.Y, end.Y) + radius), 0, _height);
            var delta = end - start;
            var lengthSquared = delta.X * delta.X + delta.Y * delta.Y;
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var px = x + .5 - start.X;
                var py = y + .5 - start.Y;
                var t = lengthSquared == 0 ? 0 : Math.Clamp((px * delta.X + py * delta.Y) / lengthSquared, 0, 1);
                var dx = px - delta.X * t;
                var dy = py - delta.Y * t;
                if (dx * dx + dy * dy > radius * radius) continue;
                BlendPixel((y * _width + x) * 4, erase);
            }
        }

        private void BlendPixel(int offset, bool erase)
        {
            var pixels = _pixels!;
            if (erase)
            {
                pixels.AsSpan(offset, 4).Clear();
                return;
            }

            var alpha = BrushColor.A / 255d;
            var previous = pixels[offset + 3] / 255d;
            var result = alpha + previous * (1 - alpha);
            if (result <= 0) return;
            pixels[offset] =
                (byte)Math.Round((BrushColor.B * alpha + pixels[offset] * previous * (1 - alpha)) / result);
            pixels[offset + 1] =
                (byte)Math.Round((BrushColor.G * alpha + pixels[offset + 1] * previous * (1 - alpha)) / result);
            pixels[offset + 2] =
                (byte)Math.Round((BrushColor.R * alpha + pixels[offset + 2] * previous * (1 - alpha)) / result);
            pixels[offset + 3] = (byte)Math.Round(result * 255);
        }

        private void RasterShape(Point start, Point end)
        {
            if (Tool == AnimationEditorTool.Line)
            {
                Stroke(start, end, false);
                return;
            }

            var region = Region(start, end);
            if (region.Width < .01 || region.Height < .01) return;
            var half = Math.Clamp(double.IsFinite(BrushSize) ? BrushSize / 2 : 2, .5, 512);
            var left = (int)Math.Clamp(Math.Floor(region.X - half), 0, _width);
            var right = (int)Math.Clamp(Math.Ceiling(region.Right + half), 0, _width);
            var top = (int)Math.Clamp(Math.Floor(region.Y - half), 0, _height);
            var bottom = (int)Math.Clamp(Math.Ceiling(region.Bottom + half), 0, _height);
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var dx = Math.Abs(x + .5 - region.Center.X);
                var dy = Math.Abs(y + .5 - region.Center.Y);
                var rx = region.Width / 2;
                var ry = region.Height / 2;
                bool inside;
                if (Tool == AnimationEditorTool.Rectangle)
                    inside = dx <= rx + half && dy <= ry + half && (Filled || dx >= rx - half || dy >= ry - half);
                else
                {
                    var outer = dx * dx / ((rx + half) * (rx + half)) + dy * dy / ((ry + half) * (ry + half)) <= 1;
                    var inner = rx > half && ry > half &&
                                dx * dx / ((rx - half) * (rx - half)) + dy * dy / ((ry - half) * (ry - half)) < 1;
                    inside = outer && (Filled || !inner);
                }

                if (inside) BlendPixel((y * _width + x) * 4, false);
            }
        }
    }
}
