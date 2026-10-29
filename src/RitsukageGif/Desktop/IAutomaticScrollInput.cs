namespace RitsukageGif.Desktop
{
    internal interface IAutomaticScrollInput
    {
        AutomaticScrollInputResult Scroll(ScrollCaptureAxis axis, PixelRect region, CancellationToken token);
    }
}
