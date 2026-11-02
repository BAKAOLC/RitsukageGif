namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedInputScope(PinnedInputOrigin previous) : IDisposable
    {
        public void Dispose()
        {
            PinnedInputContext.Restore(previous);
        }
    }
}
