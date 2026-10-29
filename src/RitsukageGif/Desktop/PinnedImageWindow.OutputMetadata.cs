namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private double OriginalCaptureScaling { get; set; } = 1;

        internal void SetOriginalCaptureScaling(double captureScaling)
        {
            if (!double.IsFinite(captureScaling) || captureScaling is < 1 or > 16)
                throw new ArgumentOutOfRangeException(nameof(captureScaling));
            OriginalCaptureScaling = captureScaling;
        }
    }
}
