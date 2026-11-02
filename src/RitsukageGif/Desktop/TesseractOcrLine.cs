namespace RitsukageGif.Desktop
{
    internal sealed record TesseractOcrLine(OcrBounds Bounds)
    {
        internal List<OcrWord> Words { get; } = [];
    }
}
