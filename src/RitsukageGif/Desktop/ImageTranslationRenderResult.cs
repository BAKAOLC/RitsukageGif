namespace RitsukageGif.Desktop
{
    internal sealed record ImageTranslationRenderResult(byte[] Png, ImageTextSelectionDocument Text)
    {
        internal byte[]? TextOverlay { get; init; }
    }
}
