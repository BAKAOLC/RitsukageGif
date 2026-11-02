namespace RitsukageGif.Desktop
{
    [Flags]
    public enum PinnedLockRestrictions
    {
        None = 0,
        Move = 1,
        PointerClose = 2,
        KeyboardClose = 4,
        IncomingDrop = 8,
    }
}
