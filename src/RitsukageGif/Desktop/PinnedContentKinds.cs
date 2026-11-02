namespace RitsukageGif.Desktop
{
    [Flags]
    public enum PinnedContentKinds
    {
        None = 0,
        Images = 1,
        Text = 2,
        Files = 4,
        Color = 8,
        Formula = 16,
        All = Images | Text | Files | Color | Formula,
    }
}
