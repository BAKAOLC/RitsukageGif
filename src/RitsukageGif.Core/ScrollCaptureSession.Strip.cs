namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private sealed record Strip(
            int Start,
            int End,
            byte[]? Pixels,
            int DataOffset = 0,
            byte[]? HdrPixels = null,
            IReadOnlyList<FrameColorRegion>? HdrRegions = null,
            DiskStrip? Disk = null)
        {
            internal long StorageBytes =>
                Disk?.LogicalLength ?? (Pixels?.LongLength ?? 0) + (HdrPixels?.LongLength ?? 0);
        }
    }
}
