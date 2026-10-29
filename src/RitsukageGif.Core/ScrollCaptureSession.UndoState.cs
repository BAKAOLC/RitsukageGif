namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private sealed record UndoState(
            int Start,
            int End,
            int LastOffset,
            bool Prepend,
            List<Strip>? RestoreStrips = null);
    }
}
