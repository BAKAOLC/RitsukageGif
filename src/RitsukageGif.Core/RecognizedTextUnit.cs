namespace RitsukageGif.Core
{
    internal readonly record struct RecognizedTextUnit(string Text, double Start, double End, double Confidence);
}
