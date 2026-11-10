namespace RitsukageGif.Core
{
    internal sealed record RecognizedTextLine(
        string Text,
        double Confidence,
        IReadOnlyList<RecognizedTextUnit> Units);
}
