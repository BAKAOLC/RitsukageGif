using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureGeometry
    {
        public string? SourceId { get; set; }
        public string StreamId { get; set; } = string.Empty;
        public bool StableIdentity { get; set; }
        public long Generation { get; set; }
        public string Space { get; set; } = "local";
        public uint DisplayId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public double BackingScale { get; set; } = 1;

        [JsonIgnore] public PixelRect Bounds => new(X, Y, Width, Height);

        public PixelRect ToPixels(PixelRect region)
        {
            var clipped = region.Intersect(Bounds);
            if (clipped.IsEmpty) return default;
            var left = (int)Math.Floor((double)(clipped.X - X) * PixelWidth / Width);
            var top = (int)Math.Floor((double)(clipped.Y - Y) * PixelHeight / Height);
            var right = (int)Math.Ceiling((double)(clipped.Right - X) * PixelWidth / Width);
            var bottom = (int)Math.Ceiling((double)(clipped.Bottom - Y) * PixelHeight / Height);
            return new(left, top, right - left, bottom - top);
        }
    }
}
