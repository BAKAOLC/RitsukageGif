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
        private SelectionShape? _manipulationShape;
        private PixelRect _manipulationStartRegion;
        private double _manipulationThreshold;
        private bool _screenshotMouseManipulation;
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
                : SelectionHit(Snapshot.Region, point, SelectionPointerScale(point));
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

        private double SelectionPointerScale(PixelPoint point, bool marker = false)
        {
            if (UsesLocalSource && _windows.Count > 0)
                return _windows[0].Region.Width / Math.Max(1, _windows[0].ClientSize.Width);
            var screens = _screens.All;
            var position = new Avalonia_PixelPoint(point.X, point.Y);
            // The pointer-move path indexes IReadOnlyList to avoid allocating an interface enumerator.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var index = 0; index < screens.Count; index++)
                if (marker
                        ? screens[index].Bounds.ContainsExclusive(position)
                        : screens[index].Bounds.Contains(position))
                    return screens[index].Scaling;
            return marker ? _screens.Primary?.Scaling ?? 1 : 1;
        }

        internal double SelectionMarkerScale(PixelRect region)
        {
            return SelectionPointerScale(new(region.X + region.Width / 2 + region.Width % 2,
                region.Y + region.Height / 2 + region.Height % 2), true);
        }

        internal bool BeginScreenshotManipulation(OverlayWindow window, PixelPoint point, bool shift)
        {
            if (!ScreenshotMode || !RectangularSelection || GeometrySizeFixed || _finished || _screenshotCompleting ||
                _pendingFinish || SelectionManipulating || Annotations is { IsBusy: true }) return false;
            try
            {
                if (!TryBeginSelectionManipulation(point, window)) return false;
                _pointer = _manipulationAnchor;
                UpdateShift(shift);
                UpdatePoint();
                _left = true;
                _right = false;
                Refresh();
                return true;
            }
            catch (Exception error)
            {
                Finish(null, error);
                return true;
            }
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

                if (_screenshotMouseManipulation && !left)
                {
                    CommitSelectionManipulation();
                    return true;
                }

                var dx = (long)point.X - _manipulationAnchor.X;
                var dy = (long)point.Y - _manipulationAnchor.Y;
                _manipulationMoved |=
                    (double)dx * dx + (double)dy * dy > _manipulationThreshold * _manipulationThreshold;
                if (_manipulationMoved)
                {
                    if (_screenshotMouseManipulation && _selectionManipulation == MoveSelection && _shift)
                    {
                        if (Math.Abs(dx) > Math.Abs(dy)) dy = 0;
                        else dx = 0;
                    }

                    var transformed = TransformSelection(_manipulationStartRegion, _manipulationLimits, dx, dy,
                        _selectionManipulation);
                    transformed = ConstrainGeometryResize(transformed, _manipulationLimits, _selectionManipulation, dx,
                        dy);
                    _selectionIsWholeTarget = transformed == _manipulationOriginal && _manipulationWasWholeTarget;
                    _state.Set(transformed);
                    TransformShape(_manipulationShape, _manipulationOriginal, transformed);
                }

                if (!left) _selectionManipulation = 0;
                return true;
            }

            return pressed && left && TryBeginSelectionManipulation(point);
        }

        private bool TryBeginSelectionManipulation(PixelPoint point, OverlayWindow? pressWindow = null)
        {
            if (_left || _state.Dragging || _state.Selected.IsEmpty || ScreenshotDrawing) return false;
            var snapshot = Snapshot;
            var scale = SelectionPointerScale(point);
            var handle = SelectionHit(snapshot.Region, point, scale);
            var limits = WindowSource && !UsesLocalSource && ShapeMode != SelectionShapeMode.MultiWindow
                ? snapshot.Target?.Client ?? default
                : SelectionBounds();
            if (limits.IsEmpty) return false;
            var startRegion = snapshot.Region;
            var expanding = handle == 0;
            if (expanding)
            {
                if (pressWindow is null || !CapturePreferences.ExpandOnOutsideClick || !limits.Contains(point))
                    return false;
                handle = OutsideResizeHandle(startRegion, point);
                if (handle == 0) return false;
                var left = Math.Min(startRegion.X, point.X);
                var top = Math.Min(startRegion.Y, point.Y);
                startRegion = new(left, top, Math.Max(startRegion.Right, point.X + 1) - left,
                    Math.Max(startRegion.Bottom, point.Y + 1) - top);
            }

            if (GeometrySizeFixed) handle = MoveSelection;
            _selectionManipulation = handle;
            _manipulationOriginal = snapshot.Region;
            _manipulationStartRegion = startRegion;
            _manipulationShape = _selectionShape;
            _manipulationAnchor = point;
            _manipulationLimits = limits;
            _manipulationThreshold = pressWindow is null ? 4 * scale : 0;
            _screenshotMouseManipulation = pressWindow is not null;
            _manipulationMoved = false;
            _manipulationWasWholeTarget = _selectionIsWholeTarget;
            if (expanding)
            {
                _state.Set(startRegion);
                _selectionIsWholeTarget = false;
            }
            else if (pressWindow is not null && handle != MoveSelection)
            {
                var region = snapshot.Region;
                var edge = new PixelPoint(
                    (handle & 1) != 0 ? region.X : (handle & 4) != 0 ? region.Right - 1 : point.X,
                    (handle & 2) != 0 ? region.Y : (handle & 8) != 0 ? region.Bottom - 1 : point.Y);
                if (edge != point && MoveCapturePointer(CapturePointerScreenPoint(pressWindow, edge)))
                    _manipulationAnchor = edge;
            }

            return true;
        }

        private static int OutsideResizeHandle(PixelRect region, PixelPoint point)
        {
            var horizontal = point.X < region.X ? 1 : point.X >= region.Right ? 4 : 0;
            var vertical = point.Y < region.Y ? 2 : point.Y >= region.Bottom ? 8 : 0;
            return horizontal | vertical;
        }

        private bool CancelSelectionManipulation()
        {
            if (!SelectionManipulating) return false;
            _selectionManipulation = 0;
            _state.Set(_manipulationOriginal);
            _selectionShape = _manipulationShape;
            _selectionIsWholeTarget = _manipulationWasWholeTarget;
            _manipulationMoved = false;
            return true;
        }

        private void CommitSelectionManipulation()
        {
            _selectionManipulation = 0;
            _manipulationMoved = false;
        }

        private int SelectionHit(PixelRect region, PixelPoint point, double scale)
        {
            return ScreenshotMode && RectangularSelection
                ? HitScreenshotRectangle(region, point)
                : HitSelectionHandle(region, point, 6 * scale);
        }

        private bool IgnoreOutsideScreenshotPress(PixelPoint point)
        {
            if (ShapeMode == SelectionShapeMode.MultiWindow && _regionEditBase is null &&
                _selectionShape is { } shape && !shape.Bounds.Contains(point))
                return true;

            return ScreenshotMode && RectangularSelection && !CapturePreferences.ExpandOnOutsideClick &&
                   !_state.Selected.IsEmpty && HitScreenshotRectangle(_state.Selected, point) == 0;
        }

        private static int HitScreenshotRectangle(PixelRect region, PixelPoint point)
        {
            const int edgeDistance = 8;
            if (region.IsEmpty || point.X < region.X - edgeDistance || point.X > region.Right + edgeDistance ||
                point.Y < region.Y - edgeDistance || point.Y > region.Bottom + edgeDistance) return 0;
            var hit = 0;
            if (Math.Abs(point.X - region.X) <= edgeDistance) hit |= 1;
            if (Math.Abs(point.Y - region.Y) <= edgeDistance) hit |= 2;
            if (Math.Abs(point.X - region.Right) <= edgeDistance) hit |= 4;
            if (Math.Abs(point.Y - region.Bottom) <= edgeDistance) hit |= 8;
            return hit == 0 ? MoveSelection : hit;
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
