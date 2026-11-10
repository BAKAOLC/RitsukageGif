namespace RitsukageGif.Desktop
{
    internal sealed record PinnedClosedLiveImage(PinnedImageState State, WindowRoiPinSnapshot Snapshot, long ClosedAt)
        : PinnedClosedImage(State, ClosedAt)
    {
        internal override long EstimatedBytes => Snapshot.StorageBytes + (State.Annotations?.EstimatedBytes ?? 0) +
                                                 (State.SourceMime?.EstimatedBytes ?? 0);

        public override void Dispose()
        {
            Snapshot.Dispose();
        }
    }
}
