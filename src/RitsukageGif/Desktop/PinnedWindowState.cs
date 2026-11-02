namespace RitsukageGif.Desktop
{
    internal class PinnedWindowState
    {
        public string Id { get; set; } = "";
        public string Group { get; set; } = "";
        public string CustomTitle { get; set; } = "";
        public bool GroupHidden { get; set; }
        public bool VisibleInGroup { get; set; } = true;
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
    }
}
