using Avalonia.Input;
using Avalonia_PixelPoint = Avalonia.PixelPoint;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private const int MoveSelection = 16;
        private PixelPoint _manipulationAnchor;
        private bool _manipulationMoved, _manipulationWasWholeTarget, _manipulationRightRelease;
        private PixelRect _manipulationOriginal, _manipulationLimits;
        private double _manipulationThreshold;
        private int _selectionManipulation;

        internal bool SelectionManipulating => _selectionManipulation != 0;

        private SelectionPhase? ManipulationPhase => SelectionManipulating
            ? _manipulationMoved ? SelectionPhase.Dragging : SelectionPhase.Pressed
            : null;

        internal StandardCursorType? ManipulationCursor(PixelPoint point)
        {
            if (ScreenshotDrawing || _state.Selected.IsEmpty || _state.Dragging) return null;
            var handle = SelectionManipulating
                ? _selectionManipulation
                : HitSelectionHandle(Snapshot.Region, point,
                    6 * SelectionPointerScale(point));
            if (GeometrySizeFixed && handle != 0) return StandardCursorType.SizeAll;
            return handle switch
            {
                1 or 4 => StandardCursorType.SizeWestEast,
                2 or 8 => StandardCursorType.SizeNorthSouth,
                3 or 12 => StandardCursorType.TopLeftCorner,
                6 or 9 => StandardCursorType.TopRightCorner,
                MoveSelection => StandardCursorType.SizeAll,
                _ => null,
            };
        }

        private double SelectionPointerScale(PixelPoint point)
        {
            if (UsesLocalSource && _windows.Count > 0)
                return _windows[0].Region.Width / Math.Max(1, _windows[0].ClientSize.Width);
            var screens = _screens.All;
            var position = new Avalonia_PixelPoint(point.X, point.Y);
            // The pointer-move path indexes IReadOnlyList to avoid allocating an interface enumerator.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var index = 0; index < screens.Count; index++)
                if (screens[index].Bounds.Contains(position))
                    return screens[index].Scaling;
            return 1;
        }

        private bool HandleSelectionManipulation(PixelPoint point, bool left, bool right, bool pressed)
        {
            if (_manipulationRightRelease)
            {
                if (!right) _manipulationRightRelease = false;
                return true;
            }

            if (SelectionManipulating)
            {
                if (right)
                {
                    CancelSelectionManipulation();
                    _manipulationRightRelease = true;
                    _rightConfirm = false;
                    return true;
                }

                var dx = (long)point.X - _manipulationAnchor.X;
                var dy = (long)point.Y - _manipulationAnchor.Y;
                _manipulationMoved |=
                    (double)dx * dx + (double)dy * dy > _manipulationThreshold * _manipulationThreshold;
                if (_manipulationMoved)
                {
                    var transformed = TransformSelection(_manipulationOriginal, _manipulationLimits, dx, dy,
                        _selectionManipulation);
                    transformed = ConstrainGeometryResize(transformed, _manipulationLimits, _selectionManipulation);
                    _selectionIsWholeTarget = transformed == _manipulationOriginal && _manipulationWasWholeTarget;
                    _state.Set(transformed);
                }

                if (!left) _selectionManipulation = 0;
                return true;
            }

            if (!pressed || !left || _left || _state.Dragging || _state.Selected.IsEmpty || ScreenshotDrawing)
                return false;
            var snapshot = Snapshot;
            var scale = SelectionPointerScale(point);
            var handle = HitSelectionHandle(snapshot.Region, point, 6 * scale);
            if (handle == 0) return false;
            if (GeometrySizeFixed) handle = MoveSelection;
            var limits = WindowSource && !UsesLocalSource ? snapshot.Target?.Client ?? default : SelectionBounds();
            if (limits.IsEmpty) return false;
            _selectionManipulation = handle;
            _manipulationOriginal = snapshot.Region;
            _manipulationAnchor = point;
            _manipulationLimits = limits;
            _manipulationThreshold = 4 * scale;
            _manipulationMoved = false;
            _manipulationWasWholeTarget = _selectionIsWholeTarget;
            return true;
        }

        private bool CancelSelectionManipulation()
        {
            if (!SelectionManipulating) return false;
            _selectionManipulation = 0;
            _state.Set(_manipulationOriginal);
            _selectionIsWholeTarget = _manipulationWasWholeTarget;
            _manipulationMoved = false;
            return true;
        }

        private void CommitSelectionManipulation()
        {
            _selectionManipulation = 0;
            _manipulationMoved = false;
        }

        private static int HitSelectionHandle(PixelRect region, PixelPoint point, double radius)
        {
            if (region.IsEmpty) return 0;
            var nearest = radius * radius;
            var result = 0;
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
            {
                if (x == 1 && y == 1) continue;
                var px = region.X + region.Width * (x * .5);
                var py = region.Y + region.Height * (y * .5);
                var dx = point.X - px;
                var dy = point.Y - py;
                var distance = dx * dx + dy * dy;
                if (distance > nearest) continue;
                nearest = distance;
                result = x switch { 0 => 1, 2 => 4, _ => 0 } | y switch { 0 => 2, 2 => 8, _ => 0 };
            }

            return result != 0 ? result : region.Contains(point) ? MoveSelection : 0;
        }

        private static PixelRect TransformSelection(PixelRect region, PixelRect limits, long dx, long dy, int handle)
        {
            var width = Math.Min(region.Width, limits.Width);
            var height = Math.Min(region.Height, limits.Height);
            var left = (long)Math.Clamp(region.X, limits.X, limits.Right - width);
            var top = (long)Math.Clamp(region.Y, limits.Y, limits.Bottom - height);
            if (handle == MoveSelection)
                return new((int)Math.Clamp(left + dx, limits.X, (long)limits.Right - width),
                    (int)Math.Clamp(top + dy, limits.Y, (long)limits.Bottom - height), width, height);
            var right = left + width;
            var bottom = top + height;
            if ((handle & 1) != 0) left = Math.Clamp(left + dx, limits.X, right - 1);
            if ((handle & 2) != 0) top = Math.Clamp(top + dy, limits.Y, bottom - 1);
            if ((handle & 4) != 0) right = Math.Clamp(right + dx, left + 1, limits.Right);
            if ((handle & 8) != 0) bottom = Math.Clamp(bottom + dy, top + 1, limits.Bottom);
            return new((int)left, (int)top, (int)(right - left), (int)(bottom - top));
        }
    }
}
