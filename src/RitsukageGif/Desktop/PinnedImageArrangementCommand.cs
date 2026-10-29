namespace RitsukageGif.Desktop
{
    internal sealed record PinnedImageArrangementCommand(
        PinnedImageArrangement Arrangement,
        string Action,
        string TextKey);
}
