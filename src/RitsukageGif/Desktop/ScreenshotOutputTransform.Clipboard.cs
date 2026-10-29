namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotOutputTransform
    {
        internal static ScreenshotClipboardPreferences ResolveClipboardSize(ScreenshotResult result,
            ScreenshotClipboardPreferences preferences)
        {
            preferences = preferences.Normalize();
            if (preferences.HdpiTargetPercent == 0 || !double.IsFinite(result.CaptureScaling) ||
                result.CaptureScaling is < 1 or > 16)
                return preferences;
            var original = PngImageCodec.ReadDimensions(result.Png);
            var size = preferences.OutputSize.Resolve(original.Width, original.Height);
            var scale = Math.Min(1, preferences.HdpiTargetPercent / (100 * result.CaptureScaling));
            return preferences with
            {
                OutputSize = new()
                {
                    Mode = ScreenshotOutputSizeMode.Exact,
                    Width = Math.Max(1, (int)Math.Round(size.Width * scale, MidpointRounding.AwayFromZero)),
                    Height = Math.Max(1, (int)Math.Round(size.Height * scale, MidpointRounding.AwayFromZero)),
                },
            };
        }
    }
}
