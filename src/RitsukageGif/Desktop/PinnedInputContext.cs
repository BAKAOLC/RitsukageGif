namespace RitsukageGif.Desktop
{
    internal static class PinnedInputContext
    {
        private static readonly AsyncLocal<PinnedInputOrigin> Origin = new();

        internal static PinnedInputOrigin Current => Origin.Value;

        internal static PinnedInputScope Use(PinnedInputOrigin origin)
        {
            var previous = Origin.Value;
            Origin.Value = origin;
            return new(previous);
        }

        internal static bool IsCloseBlocked(bool locked, PinnedLockRestrictions operations)
        {
            return locked && (((Current & PinnedInputOrigin.Pointer) != 0 &&
                               (operations & PinnedLockRestrictions.PointerClose) != 0) ||
                              ((Current & PinnedInputOrigin.Keyboard) != 0 &&
                               (operations & PinnedLockRestrictions.KeyboardClose) != 0));
        }

        internal static void Restore(PinnedInputOrigin origin)
        {
            Origin.Value = origin;
        }
    }
}
