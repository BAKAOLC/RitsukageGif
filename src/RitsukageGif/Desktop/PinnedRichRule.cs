namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichRule(int Start, PinnedRichBlockStyle Style) : PinnedRichBlock(Start, 0, Style);
}
