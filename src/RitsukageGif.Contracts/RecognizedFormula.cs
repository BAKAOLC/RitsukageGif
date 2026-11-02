namespace RitsukageGif.Contracts
{
    public sealed record RecognizedFormula(string Latex, OcrBounds Bounds, float? DetectionConfidence = null);
}
