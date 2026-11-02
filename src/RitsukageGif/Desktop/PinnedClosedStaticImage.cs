namespace RitsukageGif.Desktop
{
    internal sealed record PinnedClosedStaticImage(
        PinnedImageState State,
        ReadOnlyMemory<byte> Png,
        string ContentHash,
        PinnedImageOriginal Original,
        bool AnimationActive,
        long ClosedAt)
        : PinnedClosedImage(State, ClosedAt)
    {
        internal override long EstimatedBytes => (long)Png.Length + Original.Png.Length +
                                                 (Original.SdrPng?.Length ?? 0) + (Original.Hdr?.Pixels.Length ?? 0) +
                                                 (Original.Encoded?.Length ?? 0);

        public override void Dispose()
        {
        }
    }
}
