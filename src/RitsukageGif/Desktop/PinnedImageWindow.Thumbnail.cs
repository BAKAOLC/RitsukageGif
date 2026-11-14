using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PinnedImageCrop? _croppedImage;
        private bool _isThumbnail;
        private PixelRect _regionDragOrigin;
        private Point _regionDragStart;
        private Border? _regionOutline;
        private bool _regionSelectionMode;
        private PixelRect? _thumbnailRegion;

        private PixelRect DisplayRegion => _isThumbnail && _thumbnailRegion is { } region
            ? region
            : new(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height);

        private void ToggleThumbnail(Avalonia.PixelPoint? screenAnchor = null)
        {
            if (LiveCaptureActive || IsOperationLocked(PinnedLockRestrictions.Move) || _annotation is not null) return;
            if (screenAnchor is { } anchor)
            {
                SetThumbnailDisplay(!_isThumbnail, anchor);
                return;
            }

            var region = _isThumbnail
                ? new(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height)
                : InitialQuickThumbnailRegion();
            ApplyThumbnailView(region, !_isThumbnail);
        }

        private void SetThumbnailRegion(PixelRect region)
        {
            if (LiveCaptureActive) return;
            region = region.Intersect(new(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height));
            if (region.IsEmpty) return;
            ApplyThumbnailView(region, true, true);
        }

        private void RefreshThumbnail(Vector scrollOffset = default, bool notify = true)
        {
            ClearTextSelection();
            RefreshHdrPreview();
            _viewport.Offset = default;
            UpdateSize();
            if (scrollOffset != default)
            {
                UpdateLayout();
                _viewport.Offset = scrollOffset;
            }

            if (notify) StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BeginRegionDrag(Point point)
        {
            _regionDragOrigin = DisplayRegion;
            _regionDragStart = point;
            _hdrPreview?.SetFrame(null);
        }

        private PixelRect RegionDragSelection(Point point)
        {
            var scale = RenderScaling / CurrentPinZoom;
            var left = Math.Clamp((int)Math.Floor(Math.Min(_regionDragStart.X, point.X) * scale), 0,
                _regionDragOrigin.Width - 1);
            var top = Math.Clamp((int)Math.Floor(Math.Min(_regionDragStart.Y, point.Y) * scale), 0,
                _regionDragOrigin.Height - 1);
            var right = Math.Clamp((int)Math.Ceiling(Math.Max(_regionDragStart.X, point.X) * scale), left + 1,
                _regionDragOrigin.Width);
            var bottom = Math.Clamp((int)Math.Ceiling(Math.Max(_regionDragStart.Y, point.Y) * scale), top + 1,
                _regionDragOrigin.Height);
            return new(_regionDragOrigin.X + left, _regionDragOrigin.Y + top, right - left, bottom - top);
        }

        private void PreviewRegionDrag(Point point)
        {
            var region = RegionDragSelection(point);
            _regionOutline ??= new()
            {
                BorderBrush = AppTheme.Accent, BorderThickness = new(1), IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            if (!_content.Children.Contains(_regionOutline)) _content.Children.Add(_regionOutline);
            var scale = CurrentPinZoom / RenderScaling;
            if (_image.TransformToVisual(_content) is not { } matrix) return;
            var bounds = new Rect((region.X - _regionDragOrigin.X) * scale,
                    (region.Y - _regionDragOrigin.Y) * scale, region.Width * scale, region.Height * scale)
                .TransformToAABB(matrix);
            _regionOutline.Margin = new(bounds.X, bounds.Y, 0, 0);
            _regionOutline.Width = bounds.Width;
            _regionOutline.Height = bounds.Height;
        }

        private void FinishRegionDrag(Point point)
        {
            _regionSelectionMode = false;
            if (_regionOutline is not null) _content.Children.Remove(_regionOutline);
            SetThumbnailRegion(RegionDragSelection(point));
        }

        private void MoveThumbnailRegion(Point point)
        {
            if (!_isThumbnail) return;
            var scale = RenderScaling / CurrentPinZoom;
            var x = _regionDragOrigin.X - (int)Math.Round((point.X - _regionDragStart.X) * scale);
            var y = _regionDragOrigin.Y - (int)Math.Round((point.Y - _regionDragStart.Y) * scale);
            _thumbnailRegion = _regionDragOrigin with
            {
                X = Math.Clamp(x, 0, (int)_bitmap.Size.Width - _regionDragOrigin.Width),
                Y = Math.Clamp(y, 0, (int)_bitmap.Size.Height - _regionDragOrigin.Height),
            };
            UpdateVisibleRegion();
            RefreshHdrPreview();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
