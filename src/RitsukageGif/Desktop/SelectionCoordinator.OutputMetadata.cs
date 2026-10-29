namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private double _previewCaptureScaling = 1;
        private byte[]? _previewOriginalPng;
        private PixelPoint? _previewOutputOrigin;

        private PixelPoint? ScreenshotOutputOrigin
        {
            get
            {
                var region = Snapshot.Region;
                if (ScreenshotPreview)
                    return _previewOutputOrigin is { } origin
                        ? new PixelPoint(checked(origin.X + region.X), checked(origin.Y + region.Y))
                        : null;
                return DesktopCapture.UsesSourceSelection ? null : new PixelPoint(region.X, region.Y);
            }
        }

        internal double ScreenshotCaptureScaling
        {
            get
            {
                if (ScreenshotPreview) return _previewCaptureScaling;
                if (DesktopCapture.UsesSourceSelection) return 1;
                var snapshot = Snapshot;
                var bounds = snapshot is { WindowSource: true, Target: not null }
                    ? snapshot.Target.Bounds
                    : snapshot.Region;
                long largest = 0;
                double scaling = 1;
                foreach (var screen in _screens.All)
                {
                    var intersect = bounds.Intersect(new(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width,
                        screen.Bounds.Height));
                    var area = (long)intersect.Width * intersect.Height;
                    if (intersect.IsEmpty || area <= largest) continue;
                    largest = area;
                    scaling = screen.Scaling;
                }

                return double.IsFinite(scaling) && scaling is >= 1 and <= 16 ? scaling : 1;
            }
        }
    }
}
