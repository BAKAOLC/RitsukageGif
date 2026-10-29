namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotResult(
        byte[] Png,
        ScreenshotOutputAction Action,
        CapturedFrame? Hdr = null,
        string? HdrUnavailableReason = null)
    {
        public OutputColorMode ColorMode { get; init; }
        public double CaptureScaling { get; init; } = 1;
        public CaptureDecoration Decoration { get; init; } = CaptureDecoration.None;
        public PixelPoint ContentOffset { get; init; }
        public PixelPoint? OutputOrigin { get; init; }
        public PixelRect? OutputRegion { get; init; }
    }
}
