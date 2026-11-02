namespace RitsukageGif.Desktop
{
    internal sealed class ImageTextSelectionDocument(string text, ImageTextSelectionSpan[] spans)
    {
        internal static ImageTextSelectionDocument Empty { get; } = new("", []);
        internal string Text { get; } = text;
        internal ReadOnlySpan<ImageTextSelectionSpan> Spans => spans;
    }
}
