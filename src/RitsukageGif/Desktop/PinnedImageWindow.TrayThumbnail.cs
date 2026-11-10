namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        public void SetThumbnailDisplay(bool value, Avalonia.PixelPoint? screenAnchor = null)
        {
            if (_closed || LiveCaptureActive) return;
            if (value == _isThumbnail) return;
            if (_annotation is not null) EndAnnotation();
            PrepareThumbnailLayout();
            var region = new PixelRect(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height);
            if (value)
            {
                if (screenAnchor is { } anchor)
                {
                    var local = PinnedWindowCoordinates.PointToClient(this, Presentation.Frame, _image, anchor);
                    var source = PinnedViewportGeometry.SourcePoint(DisplayRegion, CurrentPinZoom, RenderScaling,
                        default, local);
                    region = QuickThumbnailRegionAt(source);
                }
                else
                {
                    region = InitialQuickThumbnailRegion();
                }
            }

            ApplyThumbnailView(region, value);
        }
    }
}
