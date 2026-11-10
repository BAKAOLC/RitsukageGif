namespace RitsukageGif.Desktop
{
    internal class PinnedWindowState
    {
        public string Id { get; set; } = "";
        public string Group { get; set; } = "";
        public string CustomTitle { get; set; } = "";
        public PinnedSourceMime? SourceMime { get; set; }
        public bool GroupHidden { get; set; }
        public bool VisibleInGroup { get; set; } = true;
        public int X { get; set; }
        public int Y { get; set; }
        public double Zoom { get; set; } = 1;
        public double CaptureScaling { get; set; } = 1;
        public double Opacity { get; set; } = 1;
        public bool WindowShadow { get; set; } = true;
        public CaptureWindowShape? WindowShape { get; set; }
        public bool Locked { get; set; }
        public bool Topmost { get; set; } = true;
        public bool Visible { get; set; } = true;
        public double ScrollX { get; set; }
        public double ScrollY { get; set; }
        public long CreatedAtUtcTicks { get; set; }

        internal PinnedWindowState CopyWindowState()
        {
            return (PinnedWindowState)MemberwiseClone();
        }
    }
}
