namespace RitsukageGif.Contracts
{
    public sealed record FormulaRecognitionResult(int Width, int Height, IReadOnlyList<RecognizedFormula> Formulas);
}
