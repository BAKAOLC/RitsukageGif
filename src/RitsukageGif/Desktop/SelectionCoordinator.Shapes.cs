using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private readonly List<WindowTarget> _selectionWindows = [];
        private readonly List<SKPoint> _shapePoints = [];
        private SelectionShapeMode? _previewShapeMode;
        private SelectionShape? _regionEditBase;
        private WindowTarget? _regionEditTarget;
        private SelectionShape? _selectionShape;
        private long _shapeCountdownStarted;
        private KeyModifiers _shapeHeldModifiers;
        private IDisposable? _shapeModeTimer;
        private SKRect _shapePointBounds;
        private bool _shapePreviewDirty;
        private PixelPoint _shapePreviewPoint;
        private bool _shapeRefreshQueued;
        private bool _subtractRegion;
        private SelectionShapeMode ShapeMode { get; set; }
        internal SelectionShapeMode DisplayShapeMode => _previewShapeMode ?? ShapeMode;
        internal bool ShapeModePreviewVisible => _previewShapeMode.HasValue;
        internal bool ShapeModeCountdownVisible => ShapeModePreviewVisible && _shapeHeldModifiers == KeyModifiers.None;

        internal double ShapeModeCountdownProgress => Math.Clamp(1 -
                                                                 Stopwatch.GetElapsedTime(_shapeCountdownStarted)
                                                                     .TotalSeconds, 0, 1);

        private bool ShapeDrawing => _shapePoints.Count > 0;

        internal bool CanEditShapeRegions => ShapeMode != SelectionShapeMode.Rectangle ||
                                             _selectionShape is not null || _regionEditBase is not null;

        internal void SetShapeMode(SelectionShapeMode mode)
        {
            _shapeModeTimer?.Dispose();
            _shapeModeTimer = null;
            _previewShapeMode = null;
            _shapeHeldModifiers = KeyModifiers.None;
            if (mode == ShapeMode || _finished || _screenshotCompleting || ShapeDrawing || _state.Dragging ||
                SelectionManipulating || ScreenshotDrawing)
            {
                Refresh();
                return;
            }

            ClearSelection();
            ShapeMode = mode;
            SelectionLogger.ZLogDebug($"Selection shape mode changed. SelectionId={_selectionId} Mode={mode}");
            UpdatePoint();
            Refresh();
        }

        internal void BeginRegionEdit(bool subtract)
        {
            if (Snapshot.Region.IsEmpty || ShapeDrawing || _state.Dragging) return;
            _regionEditBase = CurrentSelectionShape();
            _regionEditTarget = _selectedTarget;
            SelectionLogger.ZLogDebug($"Selection region edit started. SelectionId={_selectionId} Subtract={subtract}");
            _subtractRegion = subtract;
            _selectionShape = null;
            _state.Clear();
            Refresh();
        }

        private bool HandleShapeModeKey(bool down, KeyModifiers modifiers)
        {
            if (!down && _shapeHeldModifiers != KeyModifiers.None &&
                (modifiers & _shapeHeldModifiers) == KeyModifiers.None && _previewShapeMode is { } selected)
            {
                SetShapeMode(selected);
                return true;
            }

            return false;
        }

        private void PreviewShapeMode(int direction, KeyModifiers modifiers)
        {
            if (_state.Dragging || ShapeDrawing || SelectionManipulating || ScreenshotDrawing) return;
            _shapeHeldModifiers = modifiers & ~KeyModifiers.Shift;
            if (_shapeHeldModifiers == KeyModifiers.None) _shapeHeldModifiers = modifiers;
            _previewShapeMode = (SelectionShapeMode)(((int)DisplayShapeMode + (direction < 0 ? 3 : 1)) % 4);
            _shapeCountdownStarted = Stopwatch.GetTimestamp();
            _shapeModeTimer?.Dispose();
            _shapeModeTimer = DispatcherTimer.RunOnce(() =>
            {
                if (_previewShapeMode is { } mode && !_finished && _shapeHeldModifiers == KeyModifiers.None)
                    SetShapeMode(mode);
            }, TimeSpan.FromSeconds(1));
            Refresh();
        }

        private bool HandleShapePointer(PixelPoint point, bool left, bool right, bool pressed, int clicks)
        {
            if (ShapeMode is not (SelectionShapeMode.Free or SelectionShapeMode.Polygon)) return false;
            if (!ShapeDrawing && !_state.Selected.IsEmpty) return false;
            if (pressed && left && !_left)
            {
                if (_shapePoints.Count == 0)
                {
                    _selectedTarget = _hoveredTarget;
                    _selectionIsWholeTarget = false;
                }

                AddShapePoint(point);
                if (ShapeMode == SelectionShapeMode.Polygon && clicks >= 2) CompleteShapeDrawing();
            }
            else if (ShapeMode == SelectionShapeMode.Free && left && _shapePoints.Count > 0)
            {
                var last = _shapePoints[^1];
                if (!last.X.Equals(point.X) || !last.Y.Equals(point.Y)) AddShapePoint(point);
            }

            if (!left && _left && ShapeMode == SelectionShapeMode.Free)
            {
                if (_shapePoints.Count > 0 && _shapePoints[^1] != new SKPoint(point.X, point.Y))
                    AddShapePoint(point);
                CompleteShapeDrawing();
            }

            if (!right && _right)
            {
                if (ShapeDrawing && ShapeMode == SelectionShapeMode.Polygon) CompleteShapeDrawing();
                else if (!CancelShapeDrawing()) ClearSelection();
            }

            if (ShapeDrawing) UpdateShapePreview(point);
            _left = left;
            _right = right;
            if (!left && !right) ReleaseCaptures();
            if (ShapeDrawing) QueueShapeRefresh();
            else Refresh();
            return true;
        }

        private void AddShapePoint(PixelPoint point)
        {
            _shapePointBounds = _shapePoints.Count == 0
                ? new(point.X, point.Y, point.X, point.Y)
                : new(Math.Min(_shapePointBounds.Left, point.X), Math.Min(_shapePointBounds.Top, point.Y),
                    Math.Max(_shapePointBounds.Right, point.X), Math.Max(_shapePointBounds.Bottom, point.Y));
            _shapePoints.Add(new(point.X, point.Y));
        }

        private void UpdateShapePreview(PixelPoint point)
        {
            _shapePreviewDirty = true;
            _shapePreviewPoint = point;
            var bounds = _shapePointBounds;
            if (ShapeMode == SelectionShapeMode.Polygon)
                bounds = new(Math.Min(bounds.Left, point.X), Math.Min(bounds.Top, point.Y),
                    Math.Max(bounds.Right, point.X), Math.Max(bounds.Bottom, point.Y));
            _state.Set(ShapeBounds(bounds));
        }

        private void QueueShapeRefresh()
        {
            if (_shapeRefreshQueued) return;
            _shapeRefreshQueued = true;
            var window = _windows.FirstOrDefault(candidate => candidate.Region.Contains(_pointer)) ??
                         _windows.FirstOrDefault();
            if (window is not null) window.RequestAnimationFrame(_ => PublishShapePreview());
            else Dispatcher.UIThread.Post(PublishShapePreview, DispatcherPriority.Render);
        }

        private void PublishShapePreview()
        {
            _shapeRefreshQueued = false;
            Refresh();
        }

        private void EnsureShapePreview()
        {
            if (!_shapePreviewDirty) return;
            _shapePreviewDirty = false;
            using var path = BuildShapePath();
            if (ShapeMode == SelectionShapeMode.Polygon) path.LineTo(_shapePreviewPoint.X, _shapePreviewPoint.Y);
            path.Close();
            _selectionShape = DescribeShape(path);
        }

        private SKPath BuildShapePath()
        {
            var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            if (_shapePoints.Count == 0) return path;
            path.MoveTo(_shapePoints[0]);
            for (var index = 1; index < _shapePoints.Count; index++) path.LineTo(_shapePoints[index]);
            return path;
        }

        private void CompleteShapeDrawing()
        {
            using var path = BuildShapePath();
            path.Close();
            _shapePoints.Clear();
            _shapePreviewDirty = false;
            CommitShape(path);
        }

        private void CommitShape(SKPath path)
        {
            if (_regionEditBase is { } previous)
            {
                using var original = SKPath.ParseSvgPathData(previous.PathData);
                original.FillType = previous.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                using var combined = new SKPath();
                _selectionShape = original.Op(path, _subtractRegion ? SKPathOp.Difference : SKPathOp.Union, combined)
                    ? DescribeShape(combined)
                    : previous;
                _regionEditBase = null;
            }
            else
            {
                _selectionShape = DescribeShape(path);
            }

            _state.Set(_selectionShape?.Bounds ?? default);
            _selectionIsWholeTarget = false;
            if (ShapeMode == SelectionShapeMode.MultiWindow && _selectedTarget is { } target)
            {
                if (_subtractRegion)
                {
                    using var selected = _selectionShape is { } shape ? SKPath.ParseSvgPathData(shape.PathData) : new();
                    selected.FillType = _selectionShape?.EvenOdd == true
                        ? SKPathFillType.EvenOdd
                        : SKPathFillType.Winding;
                    _selectionWindows.RemoveAll(window =>
                    {
                        var bounds = WindowSource ? window.Client : window.Bounds;
                        using var rectangle = new SKPath();
                        rectangle.AddRect(new(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
                        using var intersection = new SKPath();
                        return selected.Op(rectangle, SKPathOp.Intersect, intersection) && intersection.Bounds.IsEmpty;
                    });
                    if (_selectionWindows.All(window => window.Handle != target.Handle))
                        _selectedTarget = _selectionWindows.FirstOrDefault();
                }
                else if (_selectionWindows.All(item => item.Handle != target.Handle))
                {
                    _selectionWindows.Add(target);
                }

                _windowShapeRevision++;
            }

            PrepareWindowScreenshot();
            SelectionLogger.ZLogDebug(
                $"Selection shape committed. SelectionId={_selectionId} Mode={ShapeMode} Region={_state.Selected} WindowCount={_selectionWindows.Count}");
        }

        private bool CancelShapeDrawing()
        {
            if (!ShapeDrawing && _regionEditBase is null) return false;
            _shapePoints.Clear();
            _shapePreviewDirty = false;
            _selectionShape = _regionEditBase;
            if (_regionEditBase is not null) _selectedTarget = _regionEditTarget;
            _regionEditBase = null;
            _regionEditTarget = null;
            _state.Set(_selectionShape?.Bounds ?? default);
            return true;
        }

        private static SelectionShape? DescribeShape(SKPath path)
        {
            var region = ShapeBounds(path.Bounds);
            return region.IsEmpty ? null : new(region, path.ToSvgPathData(), path.FillType == SKPathFillType.EvenOdd);
        }

        private static PixelRect ShapeBounds(SKRect bounds)
        {
            var left = (int)Math.Floor(bounds.Left);
            var top = (int)Math.Floor(bounds.Top);
            return new(left, top, (int)Math.Ceiling(bounds.Right) - left,
                (int)Math.Ceiling(bounds.Bottom) - top);
        }

        private SelectionShape CurrentSelectionShape()
        {
            if (_selectionShape is { } shape) return shape;
            var region = Snapshot.Region;
            using var rectangle = new SKPath();
            rectangle.AddRect(new(region.X, region.Y, region.Right, region.Bottom));
            return new(region, rectangle.ToSvgPathData());
        }

        private void TransformShape(SelectionShape? shape, PixelRect original, PixelRect destination)
        {
            if (shape is null || original.IsEmpty || destination.IsEmpty) return;
            using var path = SKPath.ParseSvgPathData(shape.PathData);
            path.FillType = shape.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            var sx = (float)destination.Width / original.Width;
            var sy = (float)destination.Height / original.Height;
            path.Transform(new()
            {
                ScaleX = sx, ScaleY = sy,
                TransX = destination.X - original.X * sx, TransY = destination.Y - original.Y * sy, Persp2 = 1,
            });
            _selectionShape = new(destination, path.ToSvgPathData(), shape.EvenOdd);
        }

        private WindowTarget[] SelectedShapeWindows()
        {
            return
            [
                .. _targets.Where(candidate => _selectionWindows.Any(selected => selected.Handle == candidate.Handle)),
            ];
        }

        internal bool OwnsRegionInformation(OverlayWindow window)
        {
            var region = Snapshot.Region;
            if (region.IsEmpty) return false;
            var center = new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
            var owner = _windows.FirstOrDefault(candidate => candidate.Region.Contains(center)) ??
                        _windows.FirstOrDefault(candidate => !candidate.Region.Intersect(region).IsEmpty);
            return ReferenceEquals(owner, window);
        }
    }
}
