namespace RitsukageGif.Desktop
{
    [Flags]
    public enum PinnedLockedOperations
    {
        // ReSharper disable once UnusedMember.Global
        None = 0,
        Move = 1,
        Resize = 2,
        Close = 4,
        Shortcuts = 8,
        Annotation = 16,
    }
}
