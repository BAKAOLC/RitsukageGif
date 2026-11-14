namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private double RecordingOutputDensity(PixelRect region, DpiOutputFeature feature, double nativeDensity)
        {
            return nativeDensity * CaptureOutputScale(region, feature);
        }

        private double CaptureOutputScale(PixelRect region, DpiOutputFeature feature,
            NativeCaptureSession? captureSession = null)
        {
            var preferences = Settings.DpiOutput;
            return preferences.UsesLogicalPixels(feature)
                ? preferences.ScaleFor(feature,
                    captureSession?.OutputScaling ?? DisplayOutputScaling.ForRegion(Screens, region))
                : 1;
        }

        private ScreenshotSaveProfile ScaleScreenshotSave(ScreenshotResult result, ScreenshotSaveProfile profile)
        {
            var feature = result.IsScrollingCapture
                ? DpiOutputFeature.LongScreenshotSave
                : DpiOutputFeature.ScreenshotSave;
            var factor = Settings.DpiOutput.ScaleFor(feature, result.CaptureScaling);
            return factor < 1
                ? profile with { OutputSize = ScaleScreenshotSize(result, profile.OutputSize, factor) }
                : profile;
        }

        private (ScreenshotResult Result, ScreenshotClipboardPreferences Preferences) ScaleScreenshotCopy(
            ScreenshotResult result)
        {
            var feature = result.IsScrollingCapture
                ? DpiOutputFeature.LongScreenshotCopy
                : DpiOutputFeature.ScreenshotCopy;
            var dpi = Settings.DpiOutput;
            var preferences = Settings.ScreenshotClipboard;
            // An explicit feature choice takes precedence over the general clipboard DPI target.
            if (dpi.OverrideFor(feature) is not null) preferences = preferences with { HdpiTargetPercent = 0 };
            var factor = dpi.ScaleFor(feature, result.CaptureScaling);
            if (factor >= 1 || preferences.HdpiTargetPercent != 0) return (result, preferences);
            var size = ScaleScreenshotSize(result, preferences.OutputSize, factor);
            return (result with { CaptureScaling = 1 }, preferences with { OutputSize = size });
        }

        private static ScreenshotOutputSize ScaleScreenshotSize(ScreenshotResult result,
            ScreenshotOutputSize size, double scale)
        {
            var original = PngImageCodec.ReadDimensions(result.Png);
            var dimensions = size.Resolve(original.Width, original.Height);
            return new()
            {
                Mode = ScreenshotOutputSizeMode.Exact,
                Width = Math.Max(1, (int)Math.Round(dimensions.Width * scale, MidpointRounding.AwayFromZero)),
                Height = Math.Max(1, (int)Math.Round(dimensions.Height * scale, MidpointRounding.AwayFromZero)),
            };
        }
    }
}
