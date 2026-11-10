namespace RitsukageGif.Desktop
{
    internal sealed record RecordingInputFrameSnapshot(
        RecordingCursorSample Cursor,
        IReadOnlyList<RecordingInputGeometry> Geometry,
        long KeyboardStartTimestamp = 0,
        long MouseStartTimestamp = 0);
}
