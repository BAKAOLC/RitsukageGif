namespace RitsukageGif.Desktop
{
    internal interface IAutomaticScrollInput
    {
        string? UnavailableReason { get; }
        AutomaticScrollInputResult Scroll(ScrollCaptureAxis axis, PixelRect region, CancellationToken token);
    }
}
