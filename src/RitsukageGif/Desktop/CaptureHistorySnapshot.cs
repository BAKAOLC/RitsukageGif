namespace RitsukageGif.Desktop
{
    internal sealed class CaptureHistorySnapshot(
        bool enabled,
        int regionCapacity,
        int imageCapacity,
        long maximumBytes,
        CaptureRegionHistoryEntry[] regions,
        CaptureImageHistoryEntry[] images,
        long clearRevision = 0)
    {
        internal long ClearRevision { get; } = clearRevision;
        internal bool Enabled { get; } = enabled;
        internal int RegionCapacity { get; } = regionCapacity;
        internal int ImageCapacity { get; } = imageCapacity;
        internal long MaximumBytes { get; } = maximumBytes;
        internal CaptureRegionHistoryEntry[] Regions { get; } = regions;
        internal CaptureImageHistoryEntry[] Images { get; } = images;
    }
}
