using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private static readonly SemaphoreSlim CropGate = new(2);
        private PinnedImageCrop? _croppedImage;
        private bool _isThumbnail;
        private PixelRect _regionDragOrigin;
        private Point _regionDragStart;
        private Border? _regionOutline;
        private bool _regionSelectionMode;
        private bool _thumbnailBeforeAnnotation;
        private PixelRect? _thumbnailRegion;

        private PixelRect DisplayRegion => _isThumbnail && _thumbnailRegion is { } region
            ? region
            : new(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height);

        private void ToggleThumbnail()
        {
            if (IsOperationLocked(PinnedLockedOperations.Resize) || _annotation is not null) return;
            _thumbnailRegion ??= new(0, 0, Math.Min(160, (int)_bitmap.Size.Width),
                Math.Min(160, (int)_bitmap.Size.Height));
            _isThumbnail = !_isThumbnail;
            RefreshThumbnail();
        }

        private void SetThumbnailRegion(PixelRect region)
        {
            region = region.Intersect(new(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height));
            if (region.IsEmpty) return;
            _thumbnailRegion = region;
            _isThumbnail = true;
            RefreshThumbnail();
        }

        private void RefreshThumbnail()
        {
            if (CanSelectImageText) ClearTextSelection();
            else ResetTextRecognition();
            RefreshHdrPreview();
            _viewport.Offset = default;
            UpdateSize();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BeginRegionDrag(Point point)
        {
            _regionDragOrigin = DisplayRegion;
            _regionDragStart = point;
            _hdrPreview?.SetFrame(null);
        }

        private PixelRect RegionDragSelection(Point point)
        {
            var scale = RenderScaling / _zoom;
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
            var scale = _zoom / RenderScaling;
            _regionOutline.Margin = new((region.X - _regionDragOrigin.X) * scale - _viewport.Offset.X,
                (region.Y - _regionDragOrigin.Y) * scale - _viewport.Offset.Y, 0, 0);
            _regionOutline.Width = region.Width * scale;
            _regionOutline.Height = region.Height * scale;
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
            var scale = RenderScaling / _zoom;
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

        private async Task<byte[]> CurrentImageAsync()
        {
            if (_annotation is not null) return await ComposeAnnotationAsync();
            if (!_isThumbnail || !CurrentPreferences.CopyVisibleRegion) return _png;
            var region = DisplayRegion;
            var hdr = _hdrSource;
            var png = hdr is null ? _png : _sdrPng ?? _png;
            var token = _lifetime.Token;
            await CropGate.WaitAsync(token);
            try
            {
                return await Task.Run(async () =>
                {
                    if (hdr is not null)
                    {
                        var pixels = new byte[checked(region.Width * region.Height * 8)];
                        for (var y = 0; y < region.Height; y++)
                        {
                            token.ThrowIfCancellationRequested();
                            hdr.Pixels.Span.Slice((region.Y + y) * hdr.Stride + region.X * 8, region.Width * 8)
                                .CopyTo(pixels.AsSpan(y * region.Width * 8));
                        }

                        return await HdrImageService.EncodePngAsync(hdr with
                        {
                            Pixels = pixels, Width = region.Width, Height = region.Height, Stride = region.Width * 8,
                            ColorRegions = [],
                        }, token);
                    }

                    return PngImageCodec.Crop(png, region, token);
                }, token);
            }
            finally
            {
                CropGate.Release();
            }
        }
    }
}
