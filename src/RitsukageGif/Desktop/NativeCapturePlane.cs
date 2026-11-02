namespace RitsukageGif.Desktop
{
    internal sealed class NativeCapturePlane
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public string Format { get; set; } = "bgra8";
        public string Alpha { get; set; } = "opaque";
        public NativeCaptureColor Color { get; set; } = new();
    }
}
