namespace RitsukageGif.Desktop
{
    internal sealed record RecordingInputTraceFrame(
        TimeSpan Position,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        long CaptureTimestamp,
        IReadOnlyList<RecordingInputGeometry> Geometry,
        RecordingCursorSample Cursor,
        int Width,
        int Height,
        long CaptureSequence,
        long KeyboardStartTimestamp = 0,
        long MouseStartTimestamp = 0);
}
