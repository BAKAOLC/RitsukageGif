namespace RitsukageGif.Desktop
{
    internal sealed record PinnedFileListMaximumSize(double Width, double Height)
    {
        internal bool IsValid => double.IsFinite(Width) && Width > 0 && double.IsFinite(Height) && Height > 0;
    }
}
