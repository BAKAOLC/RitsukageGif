namespace RitsukageGif.Desktop
{
    internal sealed class CaptureHistoryRegionData
    {
        public Guid Id { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public PixelRect Region { get; set; }
        public bool WindowSource { get; set; }
        public bool Cursor { get; set; }
        public SelectionShape? Shape { get; set; }
        public CaptureHistoryWindowData? Target { get; set; }
        public List<CaptureHistoryWindowData>? Windows { get; set; }
    }
}
