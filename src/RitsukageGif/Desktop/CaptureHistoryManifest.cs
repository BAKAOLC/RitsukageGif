namespace RitsukageGif.Desktop
{
    internal sealed class CaptureHistoryManifest
    {
        public int Version { get; set; } = 1;
        public List<CaptureHistoryRegionData> Regions { get; set; } = [];
        public List<CaptureHistoryImageData> Images { get; set; } = [];
    }
}
