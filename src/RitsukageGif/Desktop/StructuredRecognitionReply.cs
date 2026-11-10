namespace RitsukageGif.Desktop
{
    internal sealed record StructuredRecognitionReply(
        TableRecognitionResult? Table = null,
        FormulaRecognitionResult? Formula = null,
        string? Error = null,
        FormulaRenderResult? Rendered = null,
        OcrRecognitionResult? Text = null);
}
