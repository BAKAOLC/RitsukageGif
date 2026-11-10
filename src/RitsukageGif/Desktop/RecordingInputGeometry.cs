namespace RitsukageGif.Desktop
{
    internal readonly record struct RecordingInputGeometry(
        PixelRect Region,
        PixelRect Destination,
        bool OwnsCursor = true,
        string? ClipPathData = null,
        bool ClipEvenOdd = false);
}
