using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal readonly record struct RecordingInputGeometry(
        PixelRect Region,
        SKRect Destination,
        bool OwnsCursor = true,
        string? ClipPathData = null,
        bool ClipEvenOdd = false);
}
