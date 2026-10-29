namespace RitsukageGif.Desktop
{
    internal sealed class PinnedImageState
    {
        public string Id { get; set; } = "";
        public string Group { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public double Zoom { get; set; } = 1;
        public double CaptureScaling { get; set; } = 1;
        public double Opacity { get; set; } = 1;
        public bool Locked { get; set; }
        public bool Topmost { get; set; } = true;
        public bool Visible { get; set; } = true;
        public double ScrollX { get; set; }
        public double ScrollY { get; set; }
        public long CreatedAtUtcTicks { get; set; }
        public bool Thumbnail { get; set; }
        public int ThumbnailX { get; set; }
        public int ThumbnailY { get; set; }
        public int ThumbnailWidth { get; set; }
        public int ThumbnailHeight { get; set; }
    }
}
