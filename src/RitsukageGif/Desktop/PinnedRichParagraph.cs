namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichParagraph(
        int Start,
        int Length,
        PinnedRichBlockStyle Style,
        IReadOnlyList<PinnedRichRun> Runs)
        : PinnedRichBlock(Start, Length, Style);
}
