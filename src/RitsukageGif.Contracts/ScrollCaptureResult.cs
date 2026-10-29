namespace RitsukageGif.Contracts
{
    public sealed record ScrollCaptureResult(
        ScrollCaptureStatus Status,
        int Offset,
        int AddedPixels,
        double Confidence,
        int Width,
        int Height);
}
