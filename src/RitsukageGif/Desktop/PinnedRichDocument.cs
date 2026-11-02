namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichDocument(
        string PlainText,
        IReadOnlyList<PinnedRichBlock> Blocks,
        Uri? BaseUri,
        PinnedRichBlockStyle Style);
}
