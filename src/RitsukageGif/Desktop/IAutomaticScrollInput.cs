namespace RitsukageGif.Desktop
{
    internal interface IAutomaticScrollInput
    {
        string? UnavailableReason { get; }
        bool Prepare(PixelRect region, CancellationToken token);
        AutomaticScrollInputResult Scroll(ScrollCaptureAxis axis, PixelRect region, CancellationToken token);
    }
}
