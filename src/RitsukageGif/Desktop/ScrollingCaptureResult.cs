namespace RitsukageGif.Desktop
{
    internal sealed record ScrollingCaptureResult(ScrollCaptureImage Image, ScrollingCaptureAction Action)
    {
        public PixelPoint? OutputOrigin { get; init; }
    }
}
