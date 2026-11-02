namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichRun(int Start, string Text, PinnedRichStyle Style, PinnedRichImage? Image = null);
}
