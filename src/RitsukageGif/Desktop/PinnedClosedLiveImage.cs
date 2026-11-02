namespace RitsukageGif.Desktop
{
    internal sealed record PinnedClosedLiveImage(PinnedImageState State, WindowRoiPinSnapshot Snapshot, long ClosedAt)
        : PinnedClosedImage(State, ClosedAt)
    {
        internal override long EstimatedBytes => Snapshot.StorageBytes;

        public override void Dispose()
        {
            Snapshot.Dispose();
        }
    }
}
