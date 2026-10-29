namespace RitsukageGif.Contracts
{
    public sealed record OcrLine(string Text, OcrBounds Bounds, IReadOnlyList<OcrWord> Words);
}
