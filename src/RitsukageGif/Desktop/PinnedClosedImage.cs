namespace RitsukageGif.Desktop
{
    internal abstract record PinnedClosedImage(PinnedImageState State, long ClosedAt) : IDisposable
    {
        internal abstract long EstimatedBytes { get; }
        public abstract void Dispose();
    }
}
