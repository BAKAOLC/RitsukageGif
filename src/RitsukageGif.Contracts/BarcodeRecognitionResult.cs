namespace RitsukageGif.Contracts
{
    public sealed record BarcodeRecognitionResult(
        string Text,
        string Format,
        IReadOnlyList<BarcodeRecognitionPoint> Points);
}
