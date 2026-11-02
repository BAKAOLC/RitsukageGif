namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureCursor
    {
        public bool Valid { get; set; }
        public bool Visible { get; set; }
        public uint BitmapId { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int HotspotX { get; set; }
        public int HotspotY { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double PixelScale { get; set; } = 1;
        public long ObservedNanoseconds { get; set; }
    }
}
