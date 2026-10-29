namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private PixelPoint _geometryAnchor;
        private PixelRect _geometryFixed;
        private double? _geometryRatio;
        internal bool GeometryRatioLocked => _geometryRatio.HasValue;
        internal bool GeometrySizeFixed => !_geometryFixed.IsEmpty;

        internal void RefreshGeometryOptions()
        {
            Refresh();
        }

        internal void LockGeometryRatio(bool enabled)
        {
            var region = Snapshot.Region;
            _geometryRatio = enabled && !region.IsEmpty ? (double)region.Width / region.Height : null;
            Refresh();
        }

        internal void FixGeometrySize(bool enabled)
        {
            _geometryFixed = enabled ? Snapshot.Region : default;
            Refresh();
        }

        internal void ChangeGeometry(int x, int y, int width, int height, bool widthChanged)
        {
            if (_finished || _screenshotCompleting || _state.Dragging || SelectionManipulating || ScreenshotDrawing ||
                _state.Selected.IsEmpty || Annotations is { IsBusy: true }) return;
            var limits = WindowSource && !UsesLocalSource ? Snapshot.Target?.Client ?? default : SelectionBounds();
            if (limits.IsEmpty) return;
            width = Math.Clamp(width, 1, limits.Width);
            height = Math.Clamp(height, 1, limits.Height);
            if (_geometryRatio is { } ratio)
            {
                var desiredWidth = widthChanged ? width : height * ratio;
                var desiredHeight = widthChanged ? width / ratio : height;
                var factor = Math.Min(1, Math.Min(limits.Width / desiredWidth, limits.Height / desiredHeight));
                width = Math.Max(1, (int)Math.Round(desiredWidth * factor));
                height = Math.Max(1, (int)Math.Round(desiredHeight * factor));
            }

            _state.Set(new(Math.Clamp(x, limits.X, limits.Right - width),
                Math.Clamp(y, limits.Y, limits.Bottom - height), width, height));
            _selectionIsWholeTarget = false;
            if (GeometrySizeFixed) _geometryFixed = _state.Selected;
            PrepareWindowScreenshot();
            Refresh();
        }

        internal void SwapGeometryDimensions()
        {
            var region = Snapshot.Region;
            if (_geometryRatio is { } ratio) _geometryRatio = 1 / ratio;
            ChangeGeometry(region.X, region.Y, region.Height, region.Width, true);
        }

        private PixelPoint ConstrainGeometryPoint(PixelPoint point)
        {
            if (!_state.Dragging || (!GeometryRatioLocked && !GeometrySizeFixed)) return point;
            var limits = WindowSource && !UsesLocalSource ? _selectedTarget?.Client ?? default : SelectionBounds();
            if (limits.IsEmpty) return point;
            var dx = (long)point.X - _geometryAnchor.X;
            var dy = (long)point.Y - _geometryAnchor.Y;
            var width = Math.Abs((double)dx);
            var height = Math.Abs((double)dy);
            if (GeometrySizeFixed)
            {
                width = _geometryFixed.Width;
                height = _geometryFixed.Height;
            }
            else if (_geometryRatio is { } ratio)
            {
                width = Math.Max(width, height * ratio);
                height = width / ratio;
            }

            var maxWidth = dx < 0 ? (long)_geometryAnchor.X - limits.X : (long)limits.Right - _geometryAnchor.X;
            var maxHeight = dy < 0 ? (long)_geometryAnchor.Y - limits.Y : (long)limits.Bottom - _geometryAnchor.Y;
            var fit = Math.Min(1, Math.Min(maxWidth / Math.Max(1, width), maxHeight / Math.Max(1, height)));
            return new(_geometryAnchor.X + (int)(Math.Round(width * fit) * (dx < 0 ? -1 : 1)),
                _geometryAnchor.Y + (int)(Math.Round(height * fit) * (dy < 0 ? -1 : 1)));
        }

        private void MoveGeometrySelection()
        {
            _state.Move(_mouse, _pointer);
            if (_state.DragThresholdReached && GeometrySizeFixed)
                _state.UpdateDraggedRegion(FitFixedGeometry(_state.Selected));
        }

        private PixelRect FitFixedGeometry(PixelRect region)
        {
            var limits = WindowSource && !UsesLocalSource ? _selectedTarget?.Client ?? default : SelectionBounds();
            if (limits.IsEmpty) return region;
            var width = Math.Min(_geometryFixed.Width, limits.Width);
            var height = Math.Min(_geometryFixed.Height, limits.Height);
            return new(Math.Clamp(region.X, limits.X, limits.Right - width),
                Math.Clamp(region.Y, limits.Y, limits.Bottom - height), width, height);
        }

        private PixelRect ConstrainGeometryResize(PixelRect region, PixelRect limits, int handle)
        {
            if (_geometryRatio is not { } ratio || handle == MoveSelection) return region;
            var width = (double)region.Width;
            var height = (double)region.Height;
            if ((handle & 5) == 0) width = height * ratio;
            else height = width / ratio;
            var horizontal = (handle & 5) != 0;
            var vertical = (handle & 10) != 0;
            var anchorX = (handle & 1) != 0 ? region.Right :
                horizontal ? region.X :
                _manipulationOriginal.X + _manipulationOriginal.Width / 2d;
            var anchorY = (handle & 2) != 0 ? region.Bottom :
                vertical ? region.Y :
                _manipulationOriginal.Y + _manipulationOriginal.Height / 2d;
            var maxWidth = horizontal
                ? (handle & 1) != 0 ? anchorX - limits.X : limits.Right - anchorX
                : Math.Min(anchorX - limits.X, limits.Right - anchorX) * 2;
            var maxHeight = vertical
                ? (handle & 2) != 0 ? anchorY - limits.Y : limits.Bottom - anchorY
                : Math.Min(anchorY - limits.Y, limits.Bottom - anchorY) * 2;
            var fit = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
            var w = Math.Max(1, (int)Math.Round(width * fit));
            var h = Math.Max(1, (int)Math.Round(height * fit));
            var x = (handle & 1) != 0 ? anchorX - w : horizontal ? anchorX : anchorX - w / 2d;
            var y = (handle & 2) != 0 ? anchorY - h : vertical ? anchorY : anchorY - h / 2d;
            return new((int)Math.Clamp(Math.Round(x), limits.X, limits.Right - w),
                (int)Math.Clamp(Math.Round(y), limits.Y, limits.Bottom - h), w, h);
        }
    }
}
