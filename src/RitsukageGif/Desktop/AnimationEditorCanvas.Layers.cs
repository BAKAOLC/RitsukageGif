using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorCanvas
    {
        private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
        private static readonly Cursor PanCursor = new(StandardCursorType.Hand);
        private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
        private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
        private static readonly Cursor DiagonalCursor = new(StandardCursorType.TopLeftCorner);
        private static readonly Cursor OppositeDiagonalCursor = new(StandardCursorType.TopRightCorner);
        private bool _layerChanged;
        private Rect _layerStartBounds;
        private int _resizeCorner = -1;

        public void SetSelectedBounds(double x, double y, double width, double height)
        {
            if (_selected is not { } layer || !double.IsFinite(x) || !double.IsFinite(y) ||
                !double.IsFinite(width) || !double.IsFinite(height)) return;
            var size = layer.Image is null
                ? layer.Bounds.Size
                : new(Math.Clamp(width, 1, 65536), Math.Clamp(height, 1, 65536));
            var bounds = new Rect(Math.Clamp(x, -65536, 65536), Math.Clamp(y, -65536, 65536), size.Width, size.Height);
            if (bounds == layer.Bounds) return;
            Remember();
            layer.Bounds = bounds;
            Changed();
        }

        public void BringSelectedToFront()
        {
            ReorderSelected(true);
        }

        public void SendSelectedToBack()
        {
            ReorderSelected(false);
        }

        private void ReorderSelected(bool front)
        {
            if (_selected is not { } layer || _layers.IndexOf(layer) == (front ? _layers.Count - 1 : 0)) return;
            Remember();
            _layers.Remove(layer);
            _layers.Insert(front ? _layers.Count : 0, layer);
            Changed();
        }

        private static Point Corner(Rect bounds, int index)
        {
            return index switch
            {
                0 => bounds.TopLeft,
                1 => bounds.TopRight,
                2 => bounds.BottomRight,
                _ => bounds.BottomLeft,
            };
        }

        private int HitCorner(Point point)
        {
            if (_selected is not { Image: not null } layer) return -1;
            var radius = 6 / EffectiveZoom;
            for (var index = 0; index < 4; index++)
            {
                var corner = Corner(layer.Bounds, index);
                if (Math.Abs(point.X - corner.X) <= radius && Math.Abs(point.Y - corner.Y) <= radius) return index;
            }

            return -1;
        }

        private void DrawLayerSelection(DrawingContext context, double zoom)
        {
            if (_selected is not { } selected) return;
            var pen = new Pen(Brushes.DeepSkyBlue, 1 / zoom);
            context.DrawRectangle(null, pen, selected.Bounds);
            if (selected.Image is null || Tool != AnimationEditorTool.Select) return;
            for (var index = 0; index < 4; index++)
            {
                var corner = Corner(selected.Bounds, index);
                context.DrawRectangle(Brushes.White, pen, new(corner.X - 4 / zoom, corner.Y - 4 / zoom,
                    8 / zoom, 8 / zoom));
            }
        }

        private void UpdateLayerGesture(Point next, KeyModifiers modifiers)
        {
            if (_selected is not { } layer) return;
            if (_resizeCorner < 0)
            {
                layer.Bounds = _layerStartBounds.Translate(next - _start);
            }
            else
            {
                var anchor = Corner(_layerStartBounds, (_resizeCorner + 2) % 4);
                var left = _resizeCorner is 0 or 3;
                var top = _resizeCorner is 0 or 1;
                var width = Math.Clamp((next.X - anchor.X) * (left ? -1 : 1), 1, 65536);
                var height = Math.Clamp((next.Y - anchor.Y) * (top ? -1 : 1), 1, 65536);
                if (!modifiers.HasFlag(KeyModifiers.Shift))
                {
                    var scale = Math.Clamp(Math.Max(width / _layerStartBounds.Width, height / _layerStartBounds.Height),
                        Math.Max(1 / _layerStartBounds.Width, 1 / _layerStartBounds.Height),
                        Math.Min(65536 / _layerStartBounds.Width, 65536 / _layerStartBounds.Height));
                    width = _layerStartBounds.Width * scale;
                    height = _layerStartBounds.Height * scale;
                }

                layer.Bounds = new(left ? anchor.X - width : anchor.X, top ? anchor.Y - height : anchor.Y, width,
                    height);
            }

            _layerChanged = layer.Bounds != _layerStartBounds;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdatePointerCursor(Point imagePoint)
        {
            var corner = Tool == AnimationEditorTool.Select ? HitCorner(imagePoint) : -1;
            Cursor = corner >= 0
                ? corner is 0 or 2 ? DiagonalCursor : OppositeDiagonalCursor
                : Tool == AnimationEditorTool.Pan
                    ? PanCursor
                    : Tool == AnimationEditorTool.Select
                        ? _layers.Any(layer => layer.Bounds.Contains(imagePoint)) ? MoveCursor : ArrowCursor
                        : CrossCursor;
        }
    }
}
