namespace RitsukageGif.Desktop
{
    internal sealed record QrDetectionSnapshot(PixelRect Region, IReadOnlyList<BarcodeRecognitionResult> Codes);
}
