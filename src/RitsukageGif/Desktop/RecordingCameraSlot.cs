using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed record RecordingCameraSlot
    {
        internal RecordingCameraDevice? Device { get; init; }
        internal RecordingCameraShape Shape { get; init; }
        internal int Size { get; init; } = 300;
        internal SKColor BorderColor { get; init; } = SKColors.White;
        internal int BorderWidth { get; init; } = 2;
        internal int Rotation { get; init; }
        internal bool Mirrored { get; init; }
        internal PixelPoint? Position { get; init; }
        internal SKPoint PanOffset { get; init; }
    }
}
