namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichContainer(
        int Start,
        int Length,
        PinnedRichBlockStyle Style,
        IReadOnlyList<PinnedRichBlock> Blocks) : PinnedRichBlock(Start, Length, Style);
}
