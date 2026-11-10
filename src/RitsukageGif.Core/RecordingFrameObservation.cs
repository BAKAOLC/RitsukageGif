namespace RitsukageGif.Core
{
    public readonly record struct RecordingFrameObservation(
        TimeSpan Position,
        long CaptureTimestamp,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        PixelRect Region,
        int Width,
        int Height,
        string? StreamName = null,
        long CaptureSequence = 0,
        object? SourceMetadata = null);
}
