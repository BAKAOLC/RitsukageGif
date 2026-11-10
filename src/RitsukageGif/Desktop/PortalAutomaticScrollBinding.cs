namespace RitsukageGif.Desktop
{
    internal sealed record PortalPointerObservation(
        long Generation,
        uint StreamId,
        PixelPoint Position,
        bool Valid);

    internal sealed record PortalAutomaticScrollBinding(
        long Generation,
        uint StreamId,
        PixelRect CoordinateBounds,
        Func<bool> IsCurrent,
        Func<PortalPointerObservation?> ReadPointer,
        Func<PixelRect, CancellationToken, AutomaticScrollInputResult> PreparePointer,
        Func<ScrollCaptureAxis, PixelRect, CancellationToken, AutomaticScrollInputResult> SendScroll,
        Func<string?> FaultReason);
}
