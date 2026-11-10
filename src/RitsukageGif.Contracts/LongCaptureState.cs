namespace RitsukageGif.Contracts
{
    public sealed record LongCaptureState(
        bool Running,
        bool Stopped,
        PixelRect Region,
        ScrollCaptureAxis Axis,
        int Width,
        int Height,
        ScrollCaptureResult? LastResult,
        string? Error);
}
