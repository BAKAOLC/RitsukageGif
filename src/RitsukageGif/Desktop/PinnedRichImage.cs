namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichImage(
        string Source,
        string Alternative,
        PinnedRichLength Width,
        PinnedRichLength Height);
}
