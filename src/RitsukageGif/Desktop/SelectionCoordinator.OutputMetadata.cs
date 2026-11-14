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

        internal string SelectionCoordinateUnit => OperatingSystem.IsMacOS() && !ScreenshotPreview ? "pt" : "px";

        internal double ScreenshotCaptureScaling
        {
            get
            {
                if (ScreenshotPreview) return _previewCaptureScaling;
                if (DesktopCapture.UsesSourceSelection && _sourceSelection is { } source) return source.OutputScaling;
                var snapshot = Snapshot;
                var bounds = snapshot is { WindowSource: true, Target: not null }
                    ? snapshot.Target.Bounds
                    : snapshot.Region;
                return DisplayOutputScaling.ForRegion(_screens, bounds);
            }
        }
    }
}
